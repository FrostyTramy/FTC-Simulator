"""CAD pipeline: STEP -> Godot-ready meshes, colliders, manifest, detections and report (spec 02).

Usage:
    python tools/cad_pipeline/run.py --config config/cad/field.yaml
    python tools/cad_pipeline/run.py --all

Outputs (per config name N):
    generated/N/manifest.json        every part: id, path, group, transforms, bbox, mass, material
    generated/N/scene_tree.json      assembly hierarchy
    generated/N/detected.yaml        proposals: elements, wheels, motors, servos, pivots (never applied)
    generated/N/validation.json      measured vs manual dimensions
    generated/N/report.md            counts, triangles, materials, colliders, validation
    generated/N/godot/               <group>.glb, <group>.colliders.json, field_layout.json
    godot/generated/N/               copy of generated/N/godot for the Godot project (git-ignored)
Per-part tessellation and colliders are cached in generated/cache/N/ by geometry signature.
"""

from __future__ import annotations

import argparse
import collections
import hashlib
import json
import os
import re
import shutil
import sys
import time
import zipfile
from concurrent.futures import ProcessPoolExecutor
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np
import trimesh
import yaml

sys.path.insert(0, str(Path(__file__).resolve().parent))
import colliders as coll  # noqa: E402
import detect  # noqa: E402
from mesh import MassProps, Mesh, mass_props, signature, tessellate  # noqa: E402
from step_reader import StepReader  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]
INCH = 0.0254
PIPELINE_VERSION = 1  # bump to invalidate the tessellation cache when mesh.py changes
COLLIDER_VERSION = 2  # bump to invalidate the collider cache when colliders.py changes

# Rotations that bring the source up axis to Godot's +Y (right-handed, no mirroring).
UP_TRANSFORMS = {
    "Y": np.eye(4),
    "-Y": np.diag([1.0, -1.0, -1.0, 1.0]),
    "Z": np.array([[1, 0, 0, 0], [0, 0, 1, 0], [0, -1, 0, 0], [0, 0, 0, 1]], dtype=float),
    "-Z": np.array([[1, 0, 0, 0], [0, 0, -1, 0], [0, 1, 0, 0], [0, 0, 0, 1]], dtype=float),
    "X": np.array([[0, -1, 0, 0], [1, 0, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]], dtype=float),
    "-X": np.array([[0, 1, 0, 0], [-1, 0, 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]], dtype=float),
}


@dataclass
class Instance:
    id: str
    name: str
    path: str
    parent: str
    prototype: str
    world: np.ndarray
    color: tuple[float, float, float] | None
    group: str = "static"
    dropped: bool = False
    material: str = "unknown"
    props: MassProps | None = None
    sig: str = ""
    world_bbox: tuple[list[float], list[float]] = field(default_factory=lambda: ([0, 0, 0], [0, 0, 0]))


def load_yaml(path: Path) -> Any:
    return yaml.safe_load(path.read_text("utf-8"))


def rule_value(rules: dict, dotted: str) -> Any:
    node: Any = rules
    for key in dotted.split("."):
        node = node[key]
    return node["value"]


class MaterialMatcher:
    def __init__(self, cfg: dict, default: str = "unknown"):
        self.materials = cfg["materials"]
        self.default = default if default in self.materials else "unknown"
        self.rules = [
            (
                re.compile(r["match"]["name_regex"]) if "name_regex" in r["match"] else None,
                np.array(r["match"]["color"]) if "color" in r["match"] else None,
                r["material"],
            )
            for r in cfg["rules"]
        ]

    def match(self, name: str, color: tuple[float, float, float] | None) -> str:
        for regex, rgb, material in self.rules:
            if regex is not None and regex.search(name):
                return material
            if rgb is not None and color is not None and np.all(np.abs(np.array(color) - rgb) < 0.02):
                return material
        return self.default


def extract_source(cfg: dict, cache: Path, source_root: Path = REPO_ROOT) -> Path:
    src = cfg["source"]
    if "path" in src:   # a plain STEP file, read in place (the user's original, never copied or changed)
        stamp = cache / "source.sha256"
        step = source_root / src["path"]
        st = step.stat()
        stamp.write_text(hashlib.sha256(f"{step.name}:{st.st_size}:{st.st_mtime_ns}".encode()).hexdigest())
        return step
    archive = source_root / src["zip"]
    stamp = cache / "source.sha256"
    out = cache / Path(src["member"]).name
    digest = hashlib.sha256(archive.read_bytes()).hexdigest()
    if not out.exists() or not stamp.exists() or stamp.read_text() != digest:
        with zipfile.ZipFile(archive) as z, z.open(src["member"]) as fin, out.open("wb") as fout:
            shutil.copyfileobj(fin, fout, 1 << 22)
        stamp.write_text(digest)
    return out


def bbox_world(props: MassProps, world: np.ndarray) -> tuple[list[float], list[float]]:
    lo, hi = np.array(props.bbox_min), np.array(props.bbox_max)
    corners = np.array(
        [[x, y, z, 1.0] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]
    )
    pts = (corners @ world.T)[:, :3]
    return pts.min(0).round(6).tolist(), pts.max(0).round(6).tolist()


PROXY_HULL_POINTS = 256


def hull_proxy(mesh: Mesh) -> Mesh:
    """Convex hull of a part's vertices, at most PROXY_HULL_POINTS extreme points (deterministic
    direction set), smooth-shaded below 35 degrees between faces."""
    pts = trimesh.convex.convex_hull(mesh.vertices.astype(np.float64)).vertices
    if len(pts) > PROXY_HULL_POINTS:
        dirs = trimesh.sample.sample_surface_sphere(PROXY_HULL_POINTS * 4, seed=2)
        pts = pts[np.unique(np.argmax(pts @ dirs.T, axis=0))]
    hull = trimesh.convex.convex_hull(pts)
    shaded = trimesh.graph.smooth_shade(hull, angle=np.radians(35), facet_minarea=None)
    return Mesh(
        shaded.vertices.astype(np.float32),
        shaded.vertex_normals.astype(np.float32),
        shaded.faces.astype(np.int32),
    )


class Pipeline:
    def __init__(self, config_path: Path, out_root: Path = REPO_ROOT, source_root: Path = REPO_ROOT):
        """out_root: where generated/ and godot/generated/ are written (tests use a temp dir)."""
        self.cfg = load_yaml(config_path)
        self.name = self.cfg["name"]
        self.out_root = out_root
        self.source_root = source_root
        self.out = out_root / "generated" / self.name
        self.cache = out_root / "generated" / "cache" / self.name
        self.godot_out = self.out / "godot"
        for d in (self.out, self.cache / "parts", self.godot_out):
            d.mkdir(parents=True, exist_ok=True)
        self.rules = load_yaml(REPO_ROOT / "config" / "rules_biobuzz.yaml")
        self.materials_cfg = load_yaml(REPO_ROOT / "config" / "materials.yaml")
        # default_material (per CAD file): what unmatched parts get; "unknown" (magenta) makes them stand out.
        self.matcher = MaterialMatcher(self.materials_cfg, self.cfg.get("default_material", "unknown"))
        coll_cfg = dict(self.cfg.get("colliders", {}))
        self.collider_rules = [(re.compile(r["regex"]), r) for r in coll_cfg.pop("rules", [])]
        coll_cfg.pop("default", None)
        self.settings = coll.ColliderSettings(**coll_cfg)
        self.drop = [re.compile(p) for p in self.cfg.get("drop", [])]
        self._proxy_cache: dict[str, Mesh] = {}
        self._surfaces: dict[str, tuple[str, tuple[float, float, float] | None]] = {}
        self.timings: dict[str, float] = {}

    # ---------------------------------------------------------------- reading
    def read(self) -> None:
        t0 = time.perf_counter()
        step_path = extract_source(self.cfg, self.cache, self.source_root)
        self.reader = StepReader(step_path)
        self.roots = self.reader.roots()
        self.timings["read_step_s"] = round(time.perf_counter() - t0, 1)

        raw = []
        for root in self.roots:
            stack = [(root, np.eye(4), "")]
            while stack:
                node, parent_world, parent_path = stack.pop()
                world = parent_world @ node.local
                if node.prototype:
                    raw.append((node, world, parent_path))
                for child in reversed(node.children):
                    stack.append((child, world, node.path))

        up = self.cfg.get("up_axis", "Y")
        if up == "auto":
            up = detect.guess_up_axis(raw)
        self.up_axis = up
        self.up = UP_TRANSFORMS[up]

        self.instances: list[Instance] = []
        for node, world, parent_path in raw:
            proto = self.reader.prototypes[node.prototype]
            inst = Instance(
                id=hashlib.sha1(node.path.encode()).hexdigest()[:12],
                name=proto.name or node.name,
                path=node.path,
                parent=parent_path,
                prototype=node.prototype,
                world=self.up @ world,
                color=node.color or proto.color,
            )
            inst.dropped = any(p.search(inst.name) for p in self.drop)
            inst.material = self.matcher.match(inst.name, inst.color)
            self.instances.append(inst)
        self._assign_groups()

    def _assign_groups(self) -> None:
        groups = self.cfg.get("groups", [])
        self.group_cfg = {g["name"]: g for g in groups}
        default = self.cfg.get("default_group", {"name": "static", "kind": "static"})
        self.group_cfg.setdefault(default["name"], default)
        compiled = [
            (
                g,
                re.compile(g["part_regex"]) if "part_regex" in g else None,
                re.compile(g["path_regex"]) if "path_regex" in g else None,
                re.compile(g["exclude_regex"]) if "exclude_regex" in g else None,
            )
            for g in groups
        ]
        for inst in self.instances:
            inst.group = default["name"]
            for g, part_re, path_re, excl in compiled:
                if excl and excl.search(inst.name):
                    continue
                if (part_re and part_re.search(inst.name)) or (path_re and path_re.search(inst.path)):
                    inst.group = g["name"]
                    break

    # ---------------------------------------------------------------- geometry
    def _deflection(self, inst: Instance) -> tuple[float, float]:
        tess = self.cfg["tessellation"]
        if self.group_cfg[inst.group].get("kind") == "element":
            d = tess["element"]
        elif "coarse_regex" in tess and re.search(tess["coarse_regex"], inst.name):
            d = tess["coarse"]
        else:
            d = tess["default"]
        return d["linear"], d["angular"]

    def _collider_mode(self, inst: Instance) -> tuple[str, float | None, int | None] | None:
        """(mode, CoACD threshold, max hulls) for a kept part, or None when it gets no collider.
        Group settings win over the per-part rules in config `colliders.rules`."""
        group = self.group_cfg[inst.group]
        if group.get("collider") in ("none", "floor"):
            return None
        if group.get("kind") == "element":
            return ("sphere", None, None)
        if group.get("collider") == "box_each":
            return ("box", None, None)
        for regex, rule in self.collider_rules:
            if regex.search(inst.name):
                if rule["mode"] == "none":
                    return None
                return (rule["mode"], rule.get("threshold"), rule.get("max_hulls"))
        return (self.cfg.get("colliders", {}).get("default", "hull"), None, None)

    def _mesh_key(self, inst: Instance) -> str:
        lin, ang = self._deflection(inst)
        return f"v{PIPELINE_VERSION}_{inst.sig}_{lin}_{ang}"

    def _load_meshes(self, kept: list[Instance]) -> dict[str, Mesh]:
        """Tessellate every distinct (geometry, deflection) once; cached on disk."""
        meshes: dict[str, Mesh] = {}
        for inst in kept:
            key = self._mesh_key(inst)
            if key in meshes:
                continue
            path = self.cache / "parts" / f"{key}.npz"
            if path.exists():
                data = np.load(path)
                meshes[key] = Mesh(data["v"], data["n"], data["t"])
            else:
                lin, ang = self._deflection(inst)
                meshes[key] = tessellate(self.reader.prototypes[inst.prototype].shape, lin, ang)
                np.savez_compressed(
                    path, v=meshes[key].vertices, n=meshes[key].normals, t=meshes[key].triangles
                )
        return meshes

    def _render_mesh(self, inst: Instance, mesh: Mesh) -> Mesh:
        """Render mesh of a part: a smooth convex-hull proxy when a `render_proxies` rule matches its name
        or it exceeds `max_part_triangles` (fine CAD detail such as gear teeth or tyre tread that is
        invisible at our quality target, D-007). Colliders always use the full mesh."""
        cfg = self.cfg.get("render_proxies", {})
        limit = cfg.get("max_part_triangles")
        by_rule = any(
            re.search(r["regex"], inst.name) for r in cfg.get("rules", []) if r.get("mode") == "hull"
        )
        if not by_rule and not (limit and mesh.triangle_count > limit):
            return mesh
        key = f"{self._mesh_key(inst)}_hull"
        if key not in self._proxy_cache:
            self._proxy_cache[key] = hull_proxy(mesh)
        proxy = self._proxy_cache[key]
        before, after = self.proxied.get(inst.name, (0, 0))
        self.proxied[inst.name] = (before + mesh.triangle_count, after + proxy.triangle_count)
        return proxy

    def _fit_colliders(self, kept: list[Instance], meshes: dict[str, Mesh]) -> dict[str, list[dict]]:
        """Collider shapes per collider key; misses are computed in parallel worker processes."""
        settings_tag = hashlib.sha1(repr(self.settings).encode()).hexdigest()[:8]
        self.inst_collider_key: dict[str, str | None] = {}
        jobs: dict[str, coll.Job] = {}
        shapes: dict[str, list[dict]] = {}
        for inst in kept:
            mode = self._collider_mode(inst)
            if mode is None:
                self.inst_collider_key[inst.id] = None
                continue
            mesh_key = self._mesh_key(inst)
            key = f"{mesh_key}_c{COLLIDER_VERSION}_{mode[0]}_{mode[1]}_{mode[2]}_{settings_tag}"
            self.inst_collider_key[inst.id] = key
            if key in shapes or key in jobs:
                continue
            path = self.cache / "parts" / f"{key}.colliders.json"
            if path.exists():
                cached = json.loads(path.read_text())
                shapes[key] = cached["shapes"]
                self.collider_info[key] = cached["info"] | {"part": inst.name}
                continue
            m = meshes[mesh_key]
            jobs[key] = coll.Job(
                key,
                m.vertices,
                m.triangles,
                inst.props.volume,
                inst.props.bbox_min,
                inst.props.bbox_max,
                mode[0],
                self.settings,
                mode[1],
                mode[2],
            )
            self.collider_info[key] = {"part": inst.name}
        if jobs:
            workers = max(1, min(len(jobs), (os.cpu_count() or 4) - 2))
            ordered = sorted(jobs.values(), key=lambda j: -len(j.triangles))  # big jobs first
            with ProcessPoolExecutor(max_workers=workers) as pool:
                for result in pool.map(coll.fit_job, ordered):
                    shapes[result.key] = result.shapes
                    self.collider_info[result.key] = self.collider_info[result.key] | result.info
                    path = self.cache / "parts" / f"{result.key}.colliders.json"
                    path.write_text(json.dumps({"shapes": result.shapes, "info": result.info}))
        return shapes

    def build(self) -> None:
        t0 = time.perf_counter()
        # Prototype keys come from XCAF label entries, which can change when the STEP changes:
        # the cache is only valid for the exact same source file.
        digest = (self.cache / "source.sha256").read_text().strip()[:16]
        props_file = self.cache / f"props_v{PIPELINE_VERSION}_{digest}.json"
        stored = json.loads(props_file.read_text("utf-8")) if props_file.exists() else {}
        props_cache: dict[str, MassProps] = {k: MassProps(**v) for k, v in stored.items()}
        for inst in self.instances:
            if inst.prototype not in props_cache:
                props_cache[inst.prototype] = mass_props(self.reader.prototypes[inst.prototype].shape)
            inst.props = props_cache[inst.prototype]
            inst.sig = signature(inst.props)
            inst.world_bbox = bbox_world(inst.props, inst.world)
        props_file.write_text(json.dumps({k: vars(v) for k, v in sorted(props_cache.items())}), "utf-8")
        self.timings["mass_props_s"] = round(time.perf_counter() - t0, 1)

        kept = [i for i in self.instances if not i.dropped]
        t0 = time.perf_counter()
        meshes = self._load_meshes(kept)
        self.timings["tessellate_s"] = round(time.perf_counter() - t0, 1)
        t0 = time.perf_counter()
        self.collider_info: dict[str, dict] = {}
        collider_shapes = self._fit_colliders(kept, meshes)
        self.timings["colliders_s"] = round(time.perf_counter() - t0, 1)

        t0 = time.perf_counter()
        self.group_meshes: dict[str, dict[str, list[Mesh]]] = collections.defaultdict(
            lambda: collections.defaultdict(list)
        )
        self.group_colliders: dict[str, list[dict]] = collections.defaultdict(list)
        self.element_meshes: dict[str, tuple[Mesh, str, list[dict]]] = {}
        self.element_instances: dict[str, list[Instance]] = collections.defaultdict(list)
        self.part_triangles: dict[str, int] = {}
        self.proxied: dict[str, tuple[int, int]] = {}
        for inst in kept:
            mesh = self._render_mesh(inst, meshes[self._mesh_key(inst)])
            ckey = self.inst_collider_key[inst.id]
            shapes = collider_shapes[ckey] if ckey else []
            self.part_triangles[inst.id] = mesh.triangle_count
            kind = self.group_cfg[inst.group].get("kind", "static")
            if kind == "element":
                if inst.group not in self.element_meshes:
                    # Centre the shared mesh on its bbox centre so instance transforms are body poses.
                    c = (np.array(inst.props.bbox_min) + np.array(inst.props.bbox_max)) / 2
                    shift = np.eye(4)
                    shift[:3, 3] = -c
                    self.element_meshes[inst.group] = (
                        mesh.transformed(shift),
                        inst.material,
                        coll.place(shapes, shift),
                    )
                self.element_instances[inst.group].append(inst)
            else:
                self.group_meshes[inst.group][self._surface(inst)].append(mesh.transformed(inst.world))
                self.group_colliders[inst.group] += coll.place(shapes, inst.world)
        for name, g in self.group_cfg.items():
            if g.get("collider") == "floor":
                lo = np.min([i.world_bbox[0] for i in self.instances if i.group == name], axis=0)
                hi = np.max([i.world_bbox[1] for i in self.instances if i.group == name], axis=0)
                m = np.eye(4)
                m[:3, 3] = (lo + hi) / 2
                self.group_colliders[name] = [
                    {
                        "type": "box",
                        "transform": np.round(m, 7).flatten().tolist(),
                        "size": np.round(hi - lo, 6).tolist(),
                    }
                ]
        self.timings["assemble_s"] = round(time.perf_counter() - t0, 1)

    # ---------------------------------------------------------------- export
    def _material(self, name: str, color: tuple[float, float, float] | None):
        m = self.materials_cfg["materials"].get(name, self.materials_cfg["materials"]["unknown"])
        albedo = list(color or (0.7, 0.7, 0.7)) if m["albedo"] == "cad" else list(m["albedo"])
        alpha = m.get("alpha", 1.0)
        return trimesh.visual.material.PBRMaterial(
            name=name,
            baseColorFactor=[*albedo, alpha],
            metallicFactor=m["metallic"],
            roughnessFactor=m["roughness"],
            alphaMode="BLEND" if alpha < 1.0 else "OPAQUE",
            doubleSided=alpha < 1.0,
        )

    def _surface(self, inst: Instance) -> str:
        """Merge key of a part's render surface: the material, plus the CAD colour when the material takes
        its albedo from the CAD (otherwise red and blue tape would merge into one colour)."""
        m = self.materials_cfg["materials"].get(inst.material, self.materials_cfg["materials"]["unknown"])
        if m["albedo"] != "cad" or inst.color is None:
            key = inst.material
        else:
            key = f"{inst.material}__{inst.color[0]:.3f}_{inst.color[1]:.3f}_{inst.color[2]:.3f}"
        self._surfaces[key] = (inst.material, inst.color)
        return key

    def _material_color(self, group: str, material: str):
        for inst in self.instances:
            if inst.group == group and inst.material == material and not inst.dropped:
                return inst.color
        return None

    def export(self) -> None:
        t0 = time.perf_counter()
        for f in self.godot_out.glob("*"):
            f.unlink()
        layout: dict[str, Any] = {"name": self.name, "up_axis": self.up_axis, "groups": []}
        self.triangle_totals: dict[str, int] = {}
        for group, by_material in sorted(self.group_meshes.items()):
            scene = trimesh.Scene()
            tris = 0
            for surface, meshes in sorted(by_material.items()):
                material, color = self._surfaces[surface]
                merged = Mesh.concat(meshes)
                tris += merged.triangle_count
                tm = trimesh.Trimesh(
                    merged.vertices, merged.triangles, vertex_normals=merged.normals, process=False
                )
                tm.visual = trimesh.visual.TextureVisuals(material=self._material(material, color))
                scene.add_geometry(tm, geom_name=f"{group}_{surface}", node_name=f"{group}_{surface}")
            self._write_glb(scene, self.godot_out / f"{group}.glb")
            self.triangle_totals[group] = tris
            self._write_json(self.godot_out / f"{group}.colliders.json", self.group_colliders.get(group, []))
            layout["groups"].append(
                {
                    "name": group,
                    "kind": self.group_cfg[group].get("kind", "static"),
                    "mesh": f"{group}.glb",
                    "colliders": f"{group}.colliders.json",
                    "surfaces": len(by_material),
                }
            )
        for group, (mesh, material, shapes) in sorted(self.element_meshes.items()):
            scene = trimesh.Scene()
            tm = trimesh.Trimesh(mesh.vertices, mesh.triangles, vertex_normals=mesh.normals, process=False)
            tm.visual = trimesh.visual.TextureVisuals(
                material=self._material(material, self._material_color(group, material))
            )
            scene.add_geometry(tm, geom_name=group, node_name=group)
            self._write_glb(scene, self.godot_out / f"{group}.glb")
            self.triangle_totals[group] = mesh.triangle_count * len(self.element_instances[group])
            self._write_json(self.godot_out / f"{group}.colliders.json", shapes)
            poses = []
            for inst in self.element_instances[group]:
                c = (np.array(inst.props.bbox_min) + np.array(inst.props.bbox_max)) / 2
                pose = inst.world.copy()
                pose[:3, 3] = (inst.world @ np.array([*c, 1.0]))[:3]
                poses.append({"id": inst.id, "transform": np.round(pose, 7).flatten().tolist()})
            layout["groups"].append(
                {
                    "name": group,
                    "kind": "element",
                    "mesh": f"{group}.glb",
                    "colliders": f"{group}.colliders.json",
                    "instances": poses,
                }
            )
        self._write_json(self.godot_out / "layout.json", layout)
        self.timings["export_s"] = round(time.perf_counter() - t0, 1)

    @staticmethod
    def _write_glb(scene: trimesh.Scene, path: Path) -> None:
        data = trimesh.exchange.gltf.export_glb(scene, include_normals=True)
        path.write_bytes(data)

    @staticmethod
    def _write_json(path: Path, data: Any) -> None:
        path.write_text(json.dumps(data, separators=(",", ":")), "utf-8")

    # ---------------------------------------------------------------- manifest, detection, validation
    def manifest(self) -> None:
        density = {k: v["density"]["value"] for k, v in self.materials_cfg["materials"].items()}
        parts = []
        for inst in self.instances:
            p = inst.props
            com = (inst.world @ np.array([*p.center, 1.0]))[:3]
            parts.append(
                {
                    "id": inst.id,
                    "name": inst.name,
                    "path": inst.path,
                    "parent": inst.parent,
                    "group": inst.group,
                    "dropped": inst.dropped,
                    "prototype": inst.prototype,
                    "signature": inst.sig,
                    "material": inst.material,
                    "color": list(inst.color) if inst.color else None,
                    "world": np.round(inst.world, 7).flatten().tolist(),
                    "bbox_min": inst.world_bbox[0],
                    "bbox_max": inst.world_bbox[1],
                    "volume_m3": float(f"{p.volume:.6e}"),
                    "area_m2": float(f"{p.area:.6e}"),
                    "center_of_mass": np.round(com, 6).tolist(),
                    "mass_kg_estimate": round(p.volume * density.get(inst.material, 1000), 5),
                    "triangles": self.part_triangles.get(inst.id, 0),
                }
            )
        self._write_json(
            self.out / "manifest.json",
            {"name": self.name, "up_axis": self.up_axis, "up_transform": self.up.tolist(), "parts": parts},
        )

        def tree(node) -> dict:
            return (
                {"name": node.name, "path": node.path, "children": [tree(c) for c in node.children]}
                if node.children
                else {"name": node.name, "path": node.path}
            )

        self._write_json(self.out / "scene_tree.json", [tree(r) for r in self.roots])
        self.detected = detect.detect(self.instances, self.rules)
        (self.out / "detected.yaml").write_text(
            "# Proposals only (spec 02). Never applied automatically.\n"
            + yaml.safe_dump(self.detected, sort_keys=False),
            "utf-8",
        )

    def validate(self) -> list[dict]:
        results = []
        for check in self.cfg.get("validate", []):
            m = check["measure"]
            regex = re.compile(m.get("part_regex") or m["path_regex"])
            field_name = "name" if "part_regex" in m else "path"
            sel = [i for i in self.instances if regex.search(getattr(i, field_name))]
            axis = "xyz".index(m["axis"])
            expected = rule_value(self.rules, check["rule"])
            if isinstance(expected, list):
                expected = expected[check.get("index", 0)]
            if "add_rule" in check:
                expected += rule_value(self.rules, check["add_rule"])
            expected_m = expected * INCH
            measured = self._measure(sel, axis, m["kind"])
            tol = check.get("tolerance_in", 1.0) * INCH  # §9.1: ±1 in unless the check documents otherwise
            ok = measured is not None and (
                measured <= expected_m + tol
                if m["kind"] == "extent_le"
                else abs(measured - expected_m) <= tol
            )
            results.append(
                {
                    "what": check["what"],
                    "rule": check["rule"],
                    "expected_m": round(expected_m, 4),
                    "measured_m": None if measured is None else round(measured, 4),
                    "tolerance_m": round(tol, 4),
                    "ok": bool(ok),
                    "note": check.get("note", ""),
                }
            )
        self._write_json(self.out / "validation.json", results)
        return results

    @staticmethod
    def _measure(sel: list[Instance], axis: int, kind: str) -> float | None:
        if not sel:
            return None
        if kind in ("extent", "extent_le"):
            return max(i.world_bbox[1][axis] for i in sel) - min(i.world_bbox[0][axis] for i in sel)
        if kind == "extent_first":
            return sel[0].world_bbox[1][axis] - sel[0].world_bbox[0][axis]
        if kind == "max_first":
            first = sel[0].path.split("/")[1] if "/" in sel[0].path else sel[0].path
            same = [i for i in sel if first in i.path]
            return max(i.world_bbox[1][axis] for i in same)
        if kind == "inner_gap":
            walls = [i for i in sel if i.world_bbox[1][axis] - i.world_bbox[0][axis] < 0.05]
            pos = [i.world_bbox[0][axis] for i in walls if i.world_bbox[0][axis] > 0]
            neg = [i.world_bbox[1][axis] for i in walls if i.world_bbox[1][axis] < 0]
            return min(pos) - max(neg) if pos and neg else None
        raise ValueError(kind)

    # ---------------------------------------------------------------- report
    def report(self, validation: list[dict]) -> None:
        kept = [i for i in self.instances if not i.dropped]
        unknown = sorted({i.name for i in kept if i.material == "unknown"})
        generic = sorted(
            {
                i.name
                for i in self.instances
                if re.fullmatch(r"(?i)(body|part|component|solid)?\s*\d*", i.name)
            }
        )
        sigs = collections.Counter(i.sig for i in kept)
        lines = [
            f"# CAD pipeline report: {self.name}",
            "",
            f"- Source: `{self.cfg['source'].get('path') or self.cfg['source']['zip'] + ' → ' + self.cfg['source']['member']}`",
            f"- Up axis: {self.up_axis}; units: metres (OCP document unit)",
            f"- Timings: {self.timings}",
            f"- Part instances: {len(self.instances)} ({len(kept)} kept, {len(self.instances) - len(kept)} dropped)",
            f"- Distinct prototypes: {len(self.reader.prototypes)}; distinct geometry signatures (kept): {len(sigs)}",
            f"- Triangles (kept, full detail): {sum(self.triangle_totals.values()):,}",
            f"- Draw surfaces: {sum(len(v) for v in self.group_meshes.values())} merged + "
            f"{sum(len(v) for v in self.element_instances.values())} element instances",
            "",
            "## Groups",
            "| Group | Kind | Parts | Triangles | Colliders |",
            "|---|---|---|---|---|",
        ]
        for name in sorted(set(self.group_meshes) | set(self.element_meshes)):
            n_parts = sum(1 for i in kept if i.group == name)
            n_coll = len(self.group_colliders.get(name, [])) or len(
                self.element_meshes.get(name, (None, None, []))[2]
            )
            lines.append(
                f"| {name} | {self.group_cfg[name].get('kind', 'static')} | {n_parts} | "
                f"{self.triangle_totals.get(name, 0):,} | {n_coll} |"
            )
        coll_types = collections.Counter(s["type"] for v in self.group_colliders.values() for s in v)
        lines += [
            "",
            f"Collider shapes (static/rigid groups): {dict(coll_types)}",
            "",
            "## Validation against the manual (±1 in, §9.1)",
            "| Check | Expected (m) | Measured (m) | OK |",
            "|---|---|---|---|",
        ]
        lines += [
            f"| {v['what']} | {v['expected_m']} | {v['measured_m']} ± {v['tolerance_m']} | {'✅' if v['ok'] else '❌'} {v['note']} |"
            for v in validation
        ]
        lines += ["", "## Largest parts by triangles", "| Part | Triangles |", "|---|---|"]
        tri_by_name = collections.Counter()
        for i in kept:
            tri_by_name[i.name] += self.part_triangles.get(i.id, 0)
        lines += [f"| {n} | {t:,} |" for n, t in tri_by_name.most_common(12)]
        if self.proxied:
            lines += [
                "",
                "## Render proxies (convex hulls; colliders use the full mesh)",
                "| Part | Triangles before | after |",
                "|---|---|---|",
            ]
            lines += [
                f"| {n} | {b:,} | {a:,} |"
                for n, (b, a) in sorted(self.proxied.items(), key=lambda kv: -kv[1][0])
            ]
        lines += ["", "## Materials", "| Material | Parts |", "|---|---|"]
        lines += [f"| {m} | {c} |" for m, c in collections.Counter(i.material for i in kept).most_common()]
        lines += [
            "",
            f"Unknown material parts: {unknown or 'none'}",
            f"Generic/unnamed parts: {generic or 'none'}",
        ]
        lines += [
            "",
            "## Detected (proposals)",
            "```yaml",
            yaml.safe_dump(self.detected, sort_keys=False)[:6000],
            "```",
        ]
        (self.out / "report.md").write_text("\n".join(lines) + "\n", "utf-8")

    def publish(self) -> None:
        dest = self.out_root / "godot" / "generated" / self.name
        if dest.exists():
            shutil.rmtree(dest)
        shutil.copytree(self.godot_out, dest)

    def run(self) -> list[dict]:
        self.read()
        self.build()
        self.export()
        self.manifest()
        validation = self.validate()
        self.report(validation)
        self.publish()
        return validation


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawTextHelpFormatter)
    ap.add_argument("--config", type=Path, action="append", default=[])
    ap.add_argument("--all", action="store_true")
    args = ap.parse_args(argv)
    configs = sorted((REPO_ROOT / "config" / "cad").glob("*.yaml")) if args.all else args.config
    failed = 0
    for cfg in configs:
        t0 = time.perf_counter()
        pipeline = Pipeline(cfg)
        validation = pipeline.run()
        bad = [v for v in validation if not v["ok"]]
        failed += len(bad)
        print(
            f"{pipeline.name}: {len(pipeline.instances)} parts, {sum(pipeline.triangle_totals.values()):,} triangles, "
            f"{len(validation) - len(bad)}/{len(validation)} checks OK, {time.perf_counter() - t0:.0f} s"
        )
        for v in bad:
            print(f"  FAIL {v['what']}: expected {v['expected_m']} m, measured {v['measured_m']} m")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
