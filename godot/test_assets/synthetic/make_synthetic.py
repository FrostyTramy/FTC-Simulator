"""Generate the synthetic layout used by the Godot loader tests (tests/scenarios/loader_*.yaml).

Same file formats as tools/cad_pipeline/run.py. The ramp is a box rotated -30° about Z, so its
surface falls towards +X: a ball dropped on it must end up on the +X side. If the loader read the
row-major matrices transposed, the ramp would fall towards -X and the ball would start at the
origin, and the scenario fails.

Usage: .venv/Scripts/python.exe godot/test_assets/synthetic/make_synthetic.py
"""

from __future__ import annotations

import json
import math
from pathlib import Path

import numpy as np
import trimesh

OUT = Path(__file__).resolve().parent
BALL_RADIUS = 0.0356  # pollen size, 2.8 in (§9.8)


def row_major(m: np.ndarray) -> list[float]:
    return [round(float(x), 7) for x in m.flatten()]


def material(name: str, rgba: list[float]) -> trimesh.visual.material.PBRMaterial:
    return trimesh.visual.material.PBRMaterial(name=name, baseColorFactor=rgba, metallicFactor=0.0, roughnessFactor=0.7)


def write_glb(meshes: dict[str, tuple[trimesh.Trimesh, list[float]]], path: Path) -> None:
    scene = trimesh.Scene()
    for name, (mesh, rgba) in meshes.items():
        mesh.visual = trimesh.visual.TextureVisuals(material=material(name, rgba))
        scene.add_geometry(mesh, geom_name=name, node_name=name)
    path.write_bytes(trimesh.exchange.gltf.export_glb(scene, include_normals=True))


def main() -> None:
    floor_t = trimesh.transformations.translation_matrix([0, -0.05, 0])
    floor_size = [8.0, 0.1, 8.0]
    ramp_t = trimesh.transformations.translation_matrix([0, 0.3, 0]) @ trimesh.transformations.rotation_matrix(
        math.radians(-30), [0, 0, 1]
    )
    ramp_size = [1.0, 0.05, 0.6]
    post_t = trimesh.transformations.translation_matrix([-1.5, 0.25, -1.5])
    tetra = np.array([[1.4, 0.0, 1.4], [1.8, 0.0, 1.4], [1.6, 0.0, 1.8], [1.6, 0.4, 1.6]])

    floor = trimesh.creation.box(extents=floor_size, transform=floor_t)
    ramp = trimesh.creation.box(extents=ramp_size, transform=ramp_t)
    post = trimesh.creation.cylinder(radius=0.1, height=0.5, transform=post_t @ trimesh.transformations.rotation_matrix(math.pi / 2, [1, 0, 0]))
    convex = trimesh.convex.convex_hull(tetra)
    write_glb(
        {"floor": (floor, [0.35, 0.35, 0.37, 1]), "ramp": (ramp, [0.8, 0.5, 0.2, 1]), "post": (post, [0.2, 0.6, 0.3, 1]),
         "tetra": (convex, [0.6, 0.2, 0.6, 1])},
        OUT / "static.glb",
    )
    static_colliders = [
        {"type": "box", "transform": row_major(floor_t), "size": floor_size},
        {"type": "box", "transform": row_major(ramp_t), "size": ramp_size},
        {"type": "cylinder", "transform": row_major(post_t), "radius": 0.1, "height": 0.5},
        {"type": "convex", "points": tetra.tolist()},
    ]
    (OUT / "static.colliders.json").write_text(json.dumps(static_colliders), "utf-8")

    block_t = trimesh.transformations.translation_matrix([1.5, 0.15, -1.5])
    block = trimesh.creation.box(extents=[0.3, 0.3, 0.3], transform=block_t)
    write_glb({"block": (block, [0.2, 0.3, 0.8, 1])}, OUT / "block.glb")
    (OUT / "block.colliders.json").write_text(
        json.dumps([{"type": "box", "transform": row_major(block_t), "size": [0.3, 0.3, 0.3]}]), "utf-8"
    )

    ball = trimesh.creation.icosphere(subdivisions=3, radius=BALL_RADIUS)
    write_glb({"ball": (ball, [1.0, 0.94, 0.25, 1])}, OUT / "ball.glb")
    (OUT / "ball.colliders.json").write_text(
        json.dumps([{"type": "sphere", "center": [0, 0, 0], "radius": BALL_RADIUS}]), "utf-8"
    )

    layout = {
        "name": "synthetic",
        "up_axis": "Y",
        "groups": [
            {"name": "static", "kind": "static", "mesh": "static.glb", "colliders": "static.colliders.json", "surfaces": 4},
            {"name": "block", "kind": "rigid", "mesh": "block.glb", "colliders": "block.colliders.json", "surfaces": 1},
            {"name": "pollen", "kind": "element", "mesh": "ball.glb", "colliders": "ball.colliders.json",
             "instances": [{"id": "ball0", "transform": row_major(trimesh.transformations.translation_matrix([-0.2, 0.8, 0.0]))}]},
        ],
    }
    (OUT / "layout.json").write_text(json.dumps(layout, indent=1), "utf-8")
    print(f"synthetic layout written to {OUT}")


if __name__ == "__main__":
    main()
