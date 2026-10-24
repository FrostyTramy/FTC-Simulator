"""Derive field mechanism geometry (zones, flower scoring volumes, hive cells, pivots, AprilTag
stickers, element start poses) from the field CAD.

Writes generated/field/mechanisms_derived.yaml. The reviewed config/field_mechanisms.yaml is
written by hand from this output; tests/python/test_field_mechanisms.py checks that both agree,
so a CAD change is noticed instead of silently overwriting reviewed values (spec 02, spec 03).

Frame: CAD = Godot = sim frame (docs/game_rules.md §1): X towards blue, Y up, Z towards the
audience, metres, origin at the field centre on the tile top surface.

Usage: python tools/field/derive_mechanisms.py [--step generated/cache/field/field-cad-step.step]
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import numpy as np
import trimesh
import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "tools" / "cad_pipeline"))
from mesh import mass_props, tessellate  # noqa: E402
from step_reader import StepReader  # noqa: E402

INCH = 0.0254


def r(v, nd: int = 4):
    if isinstance(v, (list, tuple, np.ndarray)):
        return [r(x, nd) for x in v]
    return round(float(v), nd)


class Field:
    def __init__(self, step: Path):
        self.reader = StepReader(step)
        self.parts = []  # (name, path, world 4x4, prototype key)
        for root in self.reader.roots():
            for node, world in root.walk():
                if node.prototype:
                    name = self.reader.prototypes[node.prototype].name or node.name
                    self.parts.append((name, node.path, world, node.prototype))
        self._props = {}

    def props(self, proto):
        if proto not in self._props:
            self._props[proto] = mass_props(self.reader.prototypes[proto].shape)
        return self._props[proto]

    def select(self, name_regex: str = ".", path_regex: str = "."):
        n, p = re.compile(name_regex), re.compile(path_regex)
        return [x for x in self.parts if n.search(x[0]) and p.search(x[1])]

    def aabb(self, parts):
        pts = []
        for _, _, world, proto in parts:
            pr = self.props(proto)
            lo, hi = np.array(pr.bbox_min), np.array(pr.bbox_max)
            corners = np.array([[x, y, z, 1] for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])])
            pts.append((corners @ world.T)[:, :3])
        pts = np.concatenate(pts)
        return pts.min(0), pts.max(0)

    def centre(self, part):
        _, _, world, proto = part
        return (world @ np.array([*self.props(proto).center, 1.0]))[:3]

    def vertices(self, parts, linear: float = 0.004):
        out = []
        for _, _, world, proto in parts:
            m = tessellate(self.reader.prototypes[proto].shape, linear, 0.6)
            out.append(m.vertices @ world[:3, :3].T + world[:3, 3])
        return np.concatenate(out)


def walls(f: Field) -> dict:
    """Inner faces of the perimeter from the polycarbonate side panels and rails."""
    per = [p for p in f.select(path_regex="am-0481b") if not re.search(r"(?i)pin|fastener|rivet|screw|strap", p[0])]
    faces = {}
    for axis, (neg, pos) in {"x": ("x_min", "x_max"), "z": ("z_min", "z_max")}.items():
        i = "xyz".index(axis)
        lo_inner, hi_inner = [], []
        for p in per:
            lo, hi = f.aabb([p])
            if hi[i] - lo[i] > 0.1:  # only parts that are thin along this axis are walls on it
                continue
            other = 2 - i if i != 1 else 0
            if hi[other] - lo[other] < 0.2:
                continue
            (hi_inner if lo[i] > 0 else lo_inner).append(hi[i] if lo[i] < 0 else lo[i])
        faces[neg] = max(x for x in lo_inner)
        faces[pos] = min(x for x in hi_inner)
    return {k: {"value": r(v), "unit": "m", "source": "CAD am-0481b perimeter (innermost wall part faces)"} for k, v in faces.items()}


def tape_box(f: Field, color: str, length_in: str):
    parts = f.select(name_regex=rf"Gaffer Tape, {color}, 1in Wide, {re.escape(length_in)}in Long")
    return [f.aabb([p]) for p in parts]


def zones(f: Field, wall: dict) -> dict:
    out = {}
    for color, sign in (("Red", -1), ("Blue", 1)):
        c = color.lower()
        # LOADING ZONE: two 11 in tapes + one 20.69 in tape; zone includes the tape, bounded by the wall.
        boxes = tape_box(f, color, "11") + tape_box(f, color, "20.69")
        lo = np.min([b[0] for b in boxes], axis=0)
        hi = np.max([b[1] for b in boxes], axis=0)
        x0, x1 = (wall["x_min"]["value"], hi[0]) if sign < 0 else (lo[0], wall["x_max"]["value"])
        out[f"loading_zone_{c}"] = {
            "x": r([x0, x1]), "z": r([lo[2], hi[2]]), "unit": "m",
            "source": f"CAD {color} gaffer tape 11 in ×2 + 20.69 in (outer tape edges) and wall; §9.3 p65",
        }
        # GARDEN: the 22.69 in tapes along the audience (red) / rear (blue) wall, from the outer tape edge to the wall.
        boxes = tape_box(f, color, "22.69")
        lo = np.min([b[0] for b in boxes], axis=0)
        hi = np.max([b[1] for b in boxes], axis=0)
        z0, z1 = (lo[2], wall["z_max"]["value"]) if sign < 0 else (wall["z_min"]["value"], hi[2])
        x0, x1 = (wall["x_min"]["value"], hi[0]) if sign < 0 else (lo[0], wall["x_max"]["value"])
        out[f"garden_{c}"] = {
            "x": r([x0, x1]), "z": r([z0, z1]), "unit": "m",
            "source": f"CAD {color} gaffer tape 22.69 in ×2 (outside edge) and walls; §9.3 p66, Fig 10-6",
        }
        # ALLIANCE AREA: 94.82 in tape + 2 × 54 in tapes outside the field.
        boxes = tape_box(f, color, "94.82") + tape_box(f, color, "54")
        lo = np.min([b[0] for b in boxes], axis=0)
        hi = np.max([b[1] for b in boxes], axis=0)
        out[f"alliance_area_{c}"] = {
            "x": r([lo[0], hi[0]]), "z": r([lo[2], hi[2]]), "unit": "m",
            "source": f"CAD {color} gaffer tape 94.82 in + 54 in ×2; §9.3 p65",
        }
    return out


def flowers(f: Field) -> list[dict]:
    out = []
    assemblies = sorted({re.search(r"am-5855: Flower Assembly <\d+>#\d+", p[1]).group(0)
                         for p in f.select(path_regex="am-5855: Flower Assembly")})
    for asm in assemblies:
        parts = f.select(path_regex=re.escape(asm))
        pipes = [p for p in parts if "HIPS Pipe" in p[0]]
        pipe_c = np.array([f.centre(p) for p in pipes])
        axis_xz = pipe_c[:, [0, 2]].mean(axis=0)
        pipe_lo, pipe_hi = f.aabb(pipes)
        pipe_radius = min((f.aabb([p])[1] - f.aabb([p])[0])[[0, 2]].max() / 2 for p in pipes)
        cage_r = float(np.mean(np.linalg.norm(pipe_c[:, [0, 2]] - axis_xz, axis=1)) - pipe_radius)
        rings = sorted(((f.aabb([p]), p[0]) for p in parts if "Flower Layer" in p[0]), key=lambda t: t[0][1][1])
        top, middle = rings[-1], rings[-2]
        lo_all, hi_all = f.aabb(parts)
        side = "x_min" if axis_xz[0] < -1.5 else "x_max" if axis_xz[0] > 1.5 else "z_min" if axis_xz[1] < -1.5 else "z_max"
        out.append({
            "id": f"flower_{side.replace('_', '')}",
            "wall": side,
            "axis_xz": r(axis_xz),
            "scoring_volume": {
                "radius": r(cage_r), "y_min": r(middle[0][1][1]), "y_max": r(top[0][1][1]), "unit": "m",
                "source": f"CAD {asm}: inside of the 4 HIPS pipes, from top of '{middle[1]}' to top of '{top[1]}'; §10.5.2 p87, CAD Ref 10-4",
            },
            "top_height": {"value": r(hi_all[1]), "unit": "m", "source": f"CAD {asm} highest point (backstop)"},
            "pipes_y": r([pipe_lo[1], pipe_hi[1]]),
        })
    return sorted(out, key=lambda x: x["id"])


def hives(f: Field) -> list[dict]:
    out = []
    for color in ("Red", "Blue"):
        hive_path = f"am-5853-{color} Hive"
        parts = f.select(path_regex=hive_path)
        bearings = [p for p in parts if "Bearing" in p[0]]
        centres = np.array([f.centre(p) for p in bearings])
        mean = centres.mean(axis=0)
        _, _, vt = np.linalg.svd(centres - mean)
        axis = vt[0] * np.sign(vt[0][np.argmax(np.abs(vt[0]))])
        cells = []
        for cell_name in ("Audience", "Scoring"):
            cell_parts = [p for p in parts if re.search(rf"{color} Cell \({cell_name}\)", p[1]) and "Skin" in p[0]]
            verts = f.vertices(cell_parts)
            to_obb, extents = trimesh.bounds.oriented_bounds(verts)
            obb_world = np.linalg.inv(to_obb)
            cells.append({
                "id": f"cell_{color.lower()}_{'audience' if cell_name == 'Audience' else 'rear'}",
                "obb_transform": r(obb_world.flatten(), 5), "obb_size": r(extents), "unit": "m",
                "centre": r(obb_world[:3, 3]),
                "source": f"CAD {color} Cell ({cell_name}) skins (top/back/bottom), oriented bounding box; §9.6.2 p70",
            })
        up = max(cells, key=lambda c: c["centre"][1])
        dampers = [r(f.centre(p)) for p in parts if re.search(r"(?i)damper", p[0]) and "Holder" not in p[0]]
        lo, hi = f.aabb(parts)
        vol = sum(f.props(p[3]).volume for p in parts)
        out.append({
            "id": f"hive_{color.lower()}",
            "cad_path": hive_path,
            "pivot": {"point": r(mean), "axis": r(axis), "unit": "m",
                      "source": f"CAD {len(bearings)} F608ZZ bearings in {hive_path}: principal axis through their centres"},
            "cells": cells,
            "up_cell_in_cad": up["id"],
            "dampers": dampers,
            "aabb": [r(lo), r(hi)],
            "solid_volume_m3": float(f"{vol:.6e}"),
        })
    return out


def stickers(f: Field) -> list[dict]:
    out = []
    for p in f.select(name_regex=r"am-5888-(red|blue)\d"):
        verts = f.vertices([p], 0.002)
        to_obb, extents = trimesh.bounds.oriented_bounds(verts)
        obb = np.linalg.inv(to_obb)
        thin = int(np.argmin(extents))
        normal = obb[:3, thin] * (1 if obb[1, thin] < 0 else -1)  # stickers face down (§9.9 p75)
        out.append({"name": p[0], "centre": r(obb[:3, 3]), "size": r(sorted(extents, reverse=True)),
                    "normal_down": r(normal), "unit": "m", "source": f"CAD {p[0]} oriented bounding box"})
    return sorted(out, key=lambda x: x["name"])


def elements(f: Field) -> dict:
    out = {}
    for key, regex in (("pollen", r"am-5851"), ("nectar_red", r"Red Nectar"), ("nectar_blue", r"Blue Nectar")):
        poses = sorted((r(f.centre(p)) for p in f.select(name_regex=regex)), key=lambda c: (c[0], c[2], c[1]))
        out[key] = {"positions": poses, "unit": "m", "source": f"CAD {regex} instances (volume centroid)"}
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawTextHelpFormatter)
    ap.add_argument("--step", type=Path, default=REPO_ROOT / "generated" / "cache" / "field" / "field-cad-step.step")
    ap.add_argument("--out", type=Path, default=REPO_ROOT / "generated" / "field" / "mechanisms_derived.yaml")
    args = ap.parse_args(argv)
    f = Field(args.step)
    wall = walls(f)
    data = {
        "frame": "X towards blue, Y up, Z towards audience, metres (docs/game_rules.md §1)",
        "walls": wall,
        "zones": zones(f, wall),
        "flowers": flowers(f),
        "hives": hives(f),
        "apriltag_stickers": stickers(f),
        "trays": [{"aabb": [r(a) for a in f.aabb([p])], "source": "CAD am-5706 Artifact Tray"} for p in f.select(name_regex="Artifact Tray")],
        "elements": elements(f),
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(yaml.safe_dump(data, sort_keys=False, width=120), "utf-8")
    print(f"wrote {args.out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
