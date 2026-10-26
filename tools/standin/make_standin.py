"""CAD-free stand-in assets: the field and every robot built from boxes and cylinders (spec 12 "CI uses small sample
fixtures"). The original STEP files are never committed (D-001), so CI machines and fresh checkouts have no pipeline
output; with these stand-ins the Godot app, the scenarios and full matches still run.

Every dimension comes from the reviewed configs (config/field_mechanisms.yaml walls, zones, hive pivots, CELL boxes,
FLOWER axes and volume, trays; config/rules_biobuzz.yaml; config/physics.yaml element sizes; config/robots/*.yaml
frame, size, wheels, mechanisms and ball path). Shapes the configs do not describe (wall height, hive frame tubes,
FLOWER pipes, robot plates, arm outlines) are simple ASSUMED stand-ins: good enough for match flow, rules, code and
determinism tests, never for calibration. Layouts carry "standin": true; the app shows a banner and prefers the real
pipeline output (godot/generated/<name>/) whenever it exists.

Same file formats as tools/cad_pipeline/run.py (layout.json, <group>.glb, <group>.colliders.json, manifest.json).

Usage: python tools/standin/make_standin.py        (writes godot/generated/standin/<name>/)
"""

from __future__ import annotations

import json
import math
import shutil
from pathlib import Path
from typing import Any

import numpy as np
import trimesh
import yaml

REPO = Path(__file__).resolve().parents[2]
OUT = REPO / "godot" / "generated" / "standin"
INCH = 0.0254

# ASSUMED stand-in shapes (not in the configs).
WALL_HEIGHT = 12 * INCH       # AndyMark am-0481 perimeter rail + polycarbonate, about 12 in
WALL_THICKNESS = 0.05
TILE_THICKNESS = 0.59 * INCH  # §9.2 p64 (also rules field.tile_thickness)
CELL_WALL = 0.006
PIPE_RADIUS = 0.011
FRAME_TUBE = 0.04

# Rotations that bring a source up axis to +Y (tools/cad_pipeline/run.py UP_TRANSFORMS).
UP = {
    "Y": np.eye(4),
    "Z": np.array([[1, 0, 0, 0], [0, 0, 1, 0], [0, -1, 0, 0], [0, 0, 0, 1]], dtype=float),
}

COLOURS = {
    "tiles": [0.33, 0.34, 0.36, 1],
    "perimeter": [0.75, 0.77, 0.8, 1],
    "frame": [0.6, 0.6, 0.62, 1],
    "red": [0.85, 0.15, 0.12, 1],
    "blue": [0.12, 0.3, 0.85, 1],
    "flower": [0.95, 0.95, 0.92, 1],
    "pollen": [1.0, 0.9, 0.15, 1],
    "chassis": [0.55, 0.57, 0.6, 1],
    "wheel": [0.12, 0.12, 0.13, 1],
    "mechanism": [0.95, 0.55, 0.1, 1],
    "arm": [0.95, 0.85, 0.1, 1],
}


def load(path: Path) -> Any:
    return yaml.safe_load(path.read_text("utf-8"))


def val(node: Any) -> Any:
    """The value of a {value, unit, source} entry (or the node itself)."""
    return node["value"] if isinstance(node, dict) and "value" in node else node


def row_major(m: np.ndarray) -> list[float]:
    return [round(float(x), 7) for x in np.asarray(m).flatten()]


def translation(p) -> np.ndarray:
    return trimesh.transformations.translation_matrix(np.asarray(p, dtype=float))


def look_along(axis, up_hint=(0.0, 1.0, 0.0)) -> np.ndarray:
    """Rotation whose local +Y is `axis` (cylinders in Godot and trimesh-after-fix are along their local Y)."""
    y = np.asarray(axis, dtype=float)
    y /= np.linalg.norm(y)
    hint = np.asarray(up_hint, dtype=float)
    if abs(float(hint @ y)) > 0.9:
        hint = np.array([1.0, 0.0, 0.0]) if abs(y[0]) < 0.9 else np.array([0.0, 0.0, 1.0])
    x = np.cross(y, hint)
    x /= np.linalg.norm(x)
    z = np.cross(x, y)
    m = np.eye(4)
    m[:3, 0], m[:3, 1], m[:3, 2] = x, y, z
    return m


class Group:
    """Meshes (by colour) and colliders of one layout group, in the layout frame."""

    def __init__(self, name: str, kind: str) -> None:
        self.name = name
        self.kind = kind
        self.meshes: dict[str, list[trimesh.Trimesh]] = {}
        self.colliders: list[dict] = []

    def box(self, size, transform: np.ndarray, colour: str, collide: bool = True) -> None:
        size = [float(s) for s in size]
        self.meshes.setdefault(colour, []).append(trimesh.creation.box(extents=size, transform=transform))
        if collide:
            self.colliders.append({"type": "box", "transform": row_major(transform), "size": [round(s, 6) for s in size]})

    def cylinder(self, radius: float, height: float, transform: np.ndarray, colour: str, collide: bool = True) -> None:
        """Cylinder along the local Y axis of `transform` (Godot CylinderShape3D convention)."""
        to_y = trimesh.transformations.rotation_matrix(-math.pi / 2, [1, 0, 0])   # trimesh cylinders run along Z
        mesh = trimesh.creation.cylinder(radius=radius, height=height, sections=24, transform=transform @ to_y)
        self.meshes.setdefault(colour, []).append(mesh)
        if collide:
            self.colliders.append({"type": "cylinder", "transform": row_major(transform), "radius": round(radius, 6), "height": round(height, 6)})

    def write(self, out: Path) -> dict:
        scene = trimesh.Scene()
        for colour, meshes in sorted(self.meshes.items()):
            merged = trimesh.util.concatenate(meshes)
            merged.visual = trimesh.visual.TextureVisuals(
                material=trimesh.visual.material.PBRMaterial(name=colour, baseColorFactor=COLOURS[colour], metallicFactor=0.0, roughnessFactor=0.6)
            )
            scene.add_geometry(merged, geom_name=f"{self.name}_{colour}", node_name=f"{self.name}_{colour}")
        (out / f"{self.name}.glb").write_bytes(trimesh.exchange.gltf.export_glb(scene, include_normals=True))
        (out / f"{self.name}.colliders.json").write_text(json.dumps(self.colliders, separators=(",", ":")), "utf-8")
        return {"name": self.name, "kind": self.kind, "mesh": f"{self.name}.glb", "colliders": f"{self.name}.colliders.json",
                "surfaces": len(self.meshes)}


def element_group(out: Path, name: str, radius: float, colour: str, positions: list) -> dict:
    scene = trimesh.Scene()
    ball = trimesh.creation.icosphere(subdivisions=3, radius=radius)
    ball.visual = trimesh.visual.TextureVisuals(
        material=trimesh.visual.material.PBRMaterial(name=colour, baseColorFactor=COLOURS[colour], metallicFactor=0.0, roughnessFactor=0.5)
    )
    scene.add_geometry(ball, geom_name=name, node_name=name)
    (out / f"{name}.glb").write_bytes(trimesh.exchange.gltf.export_glb(scene, include_normals=True))
    (out / f"{name}.colliders.json").write_text(json.dumps([{"type": "sphere", "center": [0, 0, 0], "radius": radius}]), "utf-8")
    instances = [{"id": f"standin_{name}_{i:02d}", "transform": row_major(translation(p))} for i, p in enumerate(positions)]
    return {"name": name, "kind": "element", "mesh": f"{name}.glb", "colliders": f"{name}.colliders.json", "instances": instances}


def fresh_dir(name: str) -> Path:
    out = OUT / name
    if out.exists():
        shutil.rmtree(out)
    out.mkdir(parents=True)
    return out


# ------------------------------------------------------------------------------------------------------------- field
def field() -> None:
    mech = load(REPO / "config" / "field_mechanisms.yaml")
    phys = load(REPO / "config" / "physics.yaml")
    out = fresh_dir("field")
    walls = {k: float(val(v)) for k, v in mech["walls"].items() if k != "note"}
    half_x, half_z = walls["x_max"], walls["z_max"]
    groups: list[dict] = []

    tiles = Group("tiles", "static")
    tiles.box([2 * half_x, TILE_THICKNESS, 2 * half_z], translation([0, -TILE_THICKNESS / 2, 0]), "tiles")
    groups.append(tiles.write(out))

    perimeter = Group("perimeter", "static")
    for sx, sz, cx, cz in (
        (WALL_THICKNESS, 2 * half_z + 2 * WALL_THICKNESS, half_x + WALL_THICKNESS / 2, 0.0),
        (WALL_THICKNESS, 2 * half_z + 2 * WALL_THICKNESS, -half_x - WALL_THICKNESS / 2, 0.0),
        (2 * half_x, WALL_THICKNESS, 0.0, half_z + WALL_THICKNESS / 2),
        (2 * half_x, WALL_THICKNESS, 0.0, -half_z - WALL_THICKNESS / 2),
    ):
        perimeter.box([sx, WALL_HEIGHT, sz], translation([cx, WALL_HEIGHT / 2 - TILE_THICKNESS, cz]), "perimeter")
    groups.append(perimeter.write(out))

    # HIVE frame: two A-frames (legs from the base corners to the pivot apex) and a crossbar (§9.6.1 p69 sizes).
    rules = load(REPO / "config" / "rules_biobuzz.yaml")
    hive_rules = rules["hive"]
    frame_w = float(val(hive_rules["frame_width"])) * INCH
    frame_d = float(val(hive_rules["frame_depth"])) * INCH
    pivots = {j["body_b"]: np.array(val(j["anchor"]), dtype=float) for j in mech["joints"]}
    pivot_y = float(pivots["hive_red"][1])
    frame = Group("hive_frame", "static")
    for x in (-frame_w / 2, frame_w / 2):
        for z in (-frame_d / 2, frame_d / 2):
            base, apex = np.array([x, 0.0, z]), np.array([x, pivot_y, 0.0])
            frame.box([FRAME_TUBE, float(np.linalg.norm(apex - base)), FRAME_TUBE],
                      translation((base + apex) / 2) @ look_along(apex - base), "frame")
    frame.box([frame_w, FRAME_TUBE, FRAME_TUBE], translation([0, pivot_y + FRAME_TUBE, 0]), "frame")
    groups.append(frame.write(out))

    # HIVES: each CELL is an open box (CAD skins' oriented box) whose opening is the local ±X face pointing away from the
    # pivot (the oriented boxes' axis signs differ between CELLS), plus an arm to the pivot.
    manifest_parts = []
    for hive in ("hive_red", "hive_blue"):
        colour = "red" if hive.endswith("red") else "blue"
        g = Group(hive, "rigid")
        for cell in (c for c in mech["triggers"]["cells"] if c["body"] == hive):
            t = cell_transform(cell)
            sx, sy, sz = (float(v) for v in val(cell["obb_size"]))
            w = CELL_WALL
            back = -cell_opening_side(t, pivots[hive])
            for size, centre in (
                ([w, sy, sz], [back * (sx / 2 - w / 2), 0, 0]),    # back wall (closed end)
                ([sx, w, sz], [0, -sy / 2 + w / 2, 0]),            # floor (AprilTag side)
                ([sx, w, sz], [0, sy / 2 - w / 2, 0]),             # roof
                ([sx, sy, w], [0, 0, -sz / 2 + w / 2]),            # sides
                ([sx, sy, w], [0, 0, sz / 2 - w / 2]),
            ):
                g.box(size, t @ translation(centre), colour)
                manifest_parts.append(box_part(hive, t @ translation(centre), size, density=1200.0))
            arm_from = pivots[hive]
            arm_to = (t @ np.array([back * sx / 2, 0, 0, 1.0]))[:3]
            g.box([FRAME_TUBE, float(np.linalg.norm(arm_to - arm_from)), FRAME_TUBE],
                  translation((arm_from + arm_to) / 2) @ look_along(arm_to - arm_from), "frame")
        groups.append(g.write(out))

    # FLOWERS: 4 pipes around the scoring volume, a solid middle ring at its bottom (ASSUMED: elements stay in the
    # scoring volume), a lower ring on the tiles and a bracket to the wall.
    fv = mech["triggers"]["flower_volume"]
    r_in, y_min, y_max = float(val(fv["radius"])), float(val(fv["y_min"])), float(val(fv["y_max"]))
    flowers = Group("flowers", "static")
    for f in mech["triggers"]["flowers"]:
        ax, az = (float(v) for v in val(f["axis_xz"]))
        ring_r = r_in + PIPE_RADIUS
        for k in range(4):
            a = math.pi / 4 + k * math.pi / 2
            flowers.cylinder(PIPE_RADIUS, y_max - y_min, translation([ax + ring_r * math.cos(a), (y_min + y_max) / 2, az + ring_r * math.sin(a)]), "flower")
        flowers.cylinder(ring_r + PIPE_RADIUS, 0.01, translation([ax, y_min - 0.005, az]), "flower")
        flowers.cylinder(ring_r + PIPE_RADIUS, 0.01, translation([ax, 0.005, az]), "flower")
        to_wall = np.array([np.sign(ax) if abs(ax) > abs(az) else 0.0, 0.0, np.sign(az) if abs(az) >= abs(ax) else 0.0])
        bracket_c = np.array([ax, y_min / 2, az]) + to_wall * (ring_r + 0.03)
        flowers.box([0.03 if to_wall[0] else 0.04, y_min, 0.04 if to_wall[0] else 0.03], translation(bracket_c), "flower")
    groups.append(flowers.write(out))

    # Tape (zones): visual only.
    tape = Group("tape", "static")
    for name, zone in mech["triggers"]["zones"].items():
        if name.startswith("side_"):
            continue
        colour = "red" if name.endswith("red") else "blue"
        x0, x1 = zone["x"]
        z0, z1 = zone["z"]
        y = 0.0005 if not name.startswith("alliance_area") else -TILE_THICKNESS + 0.0005
        for (a, b, c, d) in ((x0, x1, z0, z0), (x0, x1, z1, z1), (x0, x0, z0, z1), (x1, x1, z0, z1)):
            length = max(abs(b - a), abs(d - c))
            size = [max(abs(b - a), 0.025), 0.001, max(abs(d - c), 0.025)] if length > 0 else [0.025, 0.001, 0.025]
            tape.box(size, translation([(a + b) / 2, y, (c + d) / 2]), colour, collide=False)
    groups.append(tape.write(out))

    trays = Group("trays", "static")
    for tray in mech["trays"]:
        lo, hi = (np.array(p, dtype=float) for p in val(tray["aabb"]))
        size, centre = hi - lo, (hi + lo) / 2
        trays.box([size[0], 0.005, size[2]], translation([centre[0], lo[1] + 0.0025, centre[2]]), "frame")
        for dx in (-1, 1):
            trays.box([0.005, size[1], size[2]], translation([centre[0] + dx * (size[0] / 2 - 0.0025), centre[1], centre[2]]), "frame")
        for dz in (-1, 1):
            trays.box([size[0], size[1], 0.005], translation([centre[0], centre[1], centre[2] + dz * (size[2] / 2 - 0.0025)]), "frame")
    groups.append(trays.write(out))

    # SCORING ELEMENTS at the start layout (§10.3.1 p83-84, field_mechanisms.yaml start_layout counts).
    rp = float(val(phys["elements"]["pollen"]["radius"]))
    rn = float(val(phys["elements"]["nectar"]["radius"]))
    pollen: list[list[float]] = []
    for f in mech["triggers"]["flowers"]:                                   # 4 in each FLOWER
        ax, az = (float(v) for v in val(f["axis_xz"]))
        pollen += [[ax, y_min + rp + 0.002 + k * 2 * rp, az] for k in range(4)]
    zones = mech["triggers"]["zones"]
    gr, gb = zones["garden_red"], zones["garden_blue"]                      # 4 per GARDEN from the alliance corner
    pollen += [[gr["x"][0] + rp + 0.002 + k * 2 * rp, rp, gr["z"][1] - rp - 0.002] for k in range(4)]
    pollen += [[gb["x"][1] - rp - 0.002 - k * 2 * rp, rp, gb["z"][0] + rp + 0.002] for k in range(4)]
    for side, lz in ((-1, zones["loading_zone_red"]), (1, zones["loading_zone_blue"])):   # pre-loads off-field
        zc = (lz["z"][0] + lz["z"][1]) / 2
        for k in range(8):
            pollen.append([side * (half_x + WALL_THICKNESS + 0.06 + (k // 4) * 2.2 * rp), -TILE_THICKNESS + rp,
                           zc + (k % 4 - 1.5) * 2.2 * rp])
    nectar: dict[str, list[list[float]]] = {"red": [], "blue": []}
    for alliance, hive, up_cell in (("red", "hive_red", "cell_red_audience"), ("blue", "hive_blue", "cell_blue_rear")):
        cell = next(c for c in mech["triggers"]["cells"] if c["id"] == up_cell)
        t = cell_transform(cell)
        sx, sy, sz = (float(v) for v in val(cell["obb_size"]))
        own_side = -1.0 if alliance == "red" else 1.0                       # side of the CELL nearest the own ALLIANCE AREA
        z_dir = 1.0 if float(t[0, 2]) * own_side > 0 else -1.0
        back = -cell_opening_side(t, np.array(next(val(j["anchor"]) for j in mech["joints"] if j["body_b"] == hive), dtype=float))
        floor = -1.0 if float(t[1, 1]) > 0 else 1.0    # the ±Y face that faces down (the AprilTag side)
        for k in range(3):   # touching the back wall, on the floor, in a line from the own side
            local = [back * (sx / 2 - CELL_WALL - rn - 0.002), floor * (sy / 2 - CELL_WALL - rn - 0.002),
                     z_dir * (sz / 2 - CELL_WALL - rn - 0.002 - k * (2 * rn + 0.002))]
            nectar[alliance].append((t @ np.array([*local, 1.0]))[:3].round(5).tolist())
        tray = next(tr for tr in mech["trays"] if tr["alliance"] == alliance)
        lo, hi = (np.array(p, dtype=float) for p in val(tray["aabb"]))
        nectar[alliance] += [[(lo[0] + hi[0]) / 2, lo[1] + 0.005 + rn, lo[2] + rn + 0.01 + k * 2.05 * rn] for k in range(5)]
    groups.append(element_group(out, "pollen", rp, "pollen", pollen))
    groups.append(element_group(out, "nectar_red", rn, "red", nectar["red"]))
    groups.append(element_group(out, "nectar_blue", rn, "blue", nectar["blue"]))

    (out / "layout.json").write_text(json.dumps({"name": "field", "up_axis": "Y", "standin": True, "groups": groups}, indent=1), "utf-8")
    (out / "manifest.json").write_text(json.dumps({"name": "field", "standin": True, "parts": manifest_parts}), "utf-8")
    print(f"stand-in field: {len(groups)} groups, {len(pollen)} POLLEN, {len(nectar['red'])}+{len(nectar['blue'])} NECTAR -> {out}")


def cell_transform(cell: dict) -> np.ndarray:
    """A CELL's oriented box, squared to the field axes: the CAD box of the blue rear CELL is rolled by 0.3° (fit noise of
    the skins), which on the stand-in's plain floor rolls the start NECTAR along the CELL."""
    t = np.array(val(cell["obb_transform"]), dtype=float).reshape(4, 4)
    r = np.where(np.abs(t[:3, :3]) < 0.01, 0.0, t[:3, :3])
    u, _, vt = np.linalg.svd(r)
    t[:3, :3] = u @ vt
    return t


def cell_opening_side(t: np.ndarray, pivot: np.ndarray) -> float:
    """+1 if the CELL opens on its local +X face, -1 for -X: the face pointing away from the hive pivot."""
    return 1.0 if float(t[:3, 0] @ (t[:3, 3] - pivot)) > 0 else -1.0


def box_part(group: str, transform: np.ndarray, size, density: float) -> dict:
    """A manifest part for FieldMechanisms' mass properties (volume × density, solid box)."""
    corners = np.array([[sx, sy, sz, 1.0] for sx in (-0.5, 0.5) for sy in (-0.5, 0.5) for sz in (-0.5, 0.5)]) * [*size, 1.0]
    pts = (corners @ transform.T)[:, :3]
    volume = float(np.prod(size))
    return {"group": group, "mass_kg_estimate": round(volume * density, 5), "center_of_mass": transform[:3, 3].round(6).tolist(),
            "bbox_min": pts.min(0).round(6).tolist(), "bbox_max": pts.max(0).round(6).tolist()}


# ------------------------------------------------------------------------------------------------------------- robots
def robot(path: Path) -> None:
    cfg = load(path)
    name = cfg["name"]
    if "base" in cfg:
        return   # a variant uses its base robot's CAD
    up = str(val(cfg["frame"]["cad_up_axis"])).lstrip("+")
    cad_to_robot = np.array(val(cfg["frame"]["cad_to_robot"]), dtype=float)
    to_mesh = UP[up] @ np.linalg.inv(cad_to_robot)        # robot frame → layout (pipeline mesh) frame
    out = fresh_dir(name)
    w, h, d = (float(v) for v in val(cfg["dimensions"]["bbox_size"]))
    dt = cfg["drivetrain"]
    wheel_r = float(val(dt["wheel_radius"]))
    wheel_w = float(val(dt["wheel_width"]))
    track = float(val(dt["track_width"]))
    ball = cfg.get("ball_path", {})
    capture = val(ball["capture_zone"]) if "capture_zone" in ball else None
    front_free_z = float(capture["max"][2]) if capture else -d / 2   # keep the capture zone open in front
    front_free_y = float(capture["max"][1]) if capture else 0.0
    groups: list[dict] = []

    def g_box(g: Group, size, centre, colour, collide=True, rot: np.ndarray | None = None):
        g.box(size, to_mesh @ translation(centre) @ (rot if rot is not None else np.eye(4)), colour, collide)

    chassis = Group("chassis", "rigid")
    inner = track / 2 - wheel_w / 2 - 0.004
    g_box(chassis, [2 * inner, 0.012, d / 2 - front_free_z], [0, wheel_r * 0.55, (front_free_z + d / 2) / 2], "chassis")
    tower_h = max(h * 0.55, front_free_y + 0.03)
    g_box(chassis, [2 * inner * 0.8, tower_h - wheel_r * 0.6, d / 2 - front_free_z - 0.02],
          [0, (tower_h + wheel_r * 0.6) / 2, (front_free_z + 0.02 + d / 2) / 2], "chassis")
    for sx in (-1, 1):   # side plates frame the intake opening
        g_box(chassis, [0.006, 0.1, d], [sx * (inner - 0.003), 0.07, 0], "chassis")
    if front_free_y > 0:  # cross plate above the capture zone
        g_box(chassis, [2 * inner, 0.01, d / 2 + front_free_z], [0, front_free_y + 0.02, (front_free_z - d / 2) / 2], "chassis")
    groups.append(chassis.write(out))

    for wheel in dt["wheels"]:
        g = Group(f"wheel_{wheel['id']}", "rigid")
        g.cylinder(wheel_r, wheel_w, to_mesh @ translation(wheel["position"]) @ look_along([1, 0, 0]), "wheel", collide=False)
        groups.append(g.write(out))

    for m in cfg.get("mechanisms", []):
        if "axis_point" not in m or "axis" not in m:
            continue
        g = Group(m["id"], "rigid")
        point = np.array(val(m["axis_point"]), dtype=float)
        axis = np.array(val(m["axis"]), dtype=float)
        kind = m["type"]
        radius = float(val(m["radius"])) if "radius" in m else None
        width = float(val(m.get("width", m.get("length", {"value": 0.1}))))
        if radius is not None:                                   # rollers, wheels, flywheels: compliant contact
            g.cylinder(radius, width, to_mesh @ translation(point) @ look_along(axis), "mechanism", collide=False)
        elif kind == "paddle_wheel":
            reach = float(val(m.get("paddle_radius", {"value": 0.05})))
            for k in range(4):
                a = k * math.pi / 2
                rot = look_along(axis) @ trimesh.transformations.rotation_matrix(a, [0, 1, 0])
                centre = point + (rot[:3, :3] @ np.array([reach / 2, 0.0, 0.0]))
                g.box([reach, 0.03, 0.004], to_mesh @ translation(centre) @ rot, "mechanism")
        elif kind == "arm" and "floor_stop" in m:
            # An arm that rests on the floor: drawn like the RoboDojo flower bar (docs/notes/robodojo_23205.md), a
            # 254 mm bar with its ramp 145 mm from the pivot, 39° above the back at the CAD pose, side arms to the pivot.
            reach, rise = 0.145, math.radians(39)
            centre = point + np.array([0.0, reach * math.sin(rise), reach * math.cos(rise)])
            tilt = trimesh.transformations.rotation_matrix(-rise, [1, 0, 0])
            g_box(g, [0.254, 0.008, 0.07], centre, "arm", rot=tilt)
            for sx in (-0.12, 0.12):
                arm_to = centre + np.array([sx, 0, 0])
                arm_from = point + np.array([sx, 0, 0])
                g.box([0.008, float(np.linalg.norm(arm_to - arm_from)), 0.012],
                      to_mesh @ translation((arm_to + arm_from) / 2) @ look_along(arm_to - arm_from), "arm")
        elif kind == "arm":
            g_box(g, [0.01, 0.01, 0.08], point + np.array([0, 0, 0.04]), "arm")
        elif kind == "turret":
            g.cylinder(0.08, 0.01, to_mesh @ translation(point) @ look_along(axis), "mechanism", collide=False)
            g_box(g, [0.1, 0.04, 0.1], point + np.array([0, 0.025, 0]), "mechanism")
        elif kind == "hood":
            g_box(g, [0.06, 0.06, 0.006], point + np.array([0, 0.03, 0.06]), "mechanism")
        else:
            g.cylinder(0.01, width, to_mesh @ translation(point) @ look_along(axis), "mechanism", collide=False)
        groups.append(g.write(out))

    layout = {"name": name, "up_axis": up, "standin": True, "groups": groups}
    (out / "layout.json").write_text(json.dumps(layout, indent=1), "utf-8")
    print(f"stand-in robot {name}: {len(groups)} groups -> {out}")


def main() -> int:
    field()
    for path in sorted((REPO / "config" / "robots").glob("*.yaml")):
        robot(path)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
