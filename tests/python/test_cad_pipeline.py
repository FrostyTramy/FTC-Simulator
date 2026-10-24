"""CAD pipeline tests on a small STEP assembly generated with cadquery (spec 12: units, axes,
instancing, collider fitting, stable IDs, re-run stability)."""

from __future__ import annotations

import json
import sys
import zipfile
from pathlib import Path

import numpy as np
import pytest
import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "tools" / "cad_pipeline"))

cq = pytest.importorskip("cadquery")
import run  # noqa: E402

BALL_RADIUS_MM = 35.56  # 2.8 in POLLEN (§9.8), so the validation check can use the real rules file


def make_assembly(step_path: Path) -> None:
    plate = cq.Workplane("XY").box(100, 20, 200)
    ball = cq.Workplane("XY").sphere(BALL_RADIUS_MM)
    post = cq.Workplane("XY").circle(10).extrude(150)
    bracket = (
        cq.Workplane("XY").box(120, 20, 20).union(cq.Workplane("XY").box(20, 120, 20).translate((-50, 50, 0)))
    )
    ring = cq.Workplane("XY").circle(60).circle(30).extrude(10)
    screw = cq.Workplane("XY").circle(2).extrude(12)
    assy = cq.Assembly(name="rig")
    assy.add(plate, name="Box Plate A", loc=cq.Location((0, 0, 0)))
    assy.add(plate, name="Box Plate B", loc=cq.Location((300, 0, 0)))
    for i in range(3):
        assy.add(ball, name=f"Ball Element {i}", loc=cq.Location((i * 100, 300, 0)))
    assy.add(post, name="Post", loc=cq.Location((0, -300, 0)))
    assy.add(bracket, name="Angle Bracket", loc=cq.Location((500, 500, 0)))
    assy.add(ring, name="Flower Layer Ring", loc=cq.Location((-500, 0, 0)))
    assy.add(screw, name="Hex screw", loc=cq.Location((0, 0, 200)))
    assy.export(str(step_path))


def write_config(tmp: Path, up_axis: str = "Y") -> Path:
    cfg = {
        "name": f"fixture_{up_axis.replace('-', 'm')}",
        "source": {"zip": str(tmp / "fixture.zip"), "member": "fixture.step"},
        "up_axis": up_axis,
        "drop": ["(?i)screw"],
        "groups": [{"name": "balls", "kind": "element", "part_regex": "Ball Element"}],
        "tessellation": {
            "default": {"linear": 0.001, "angular": 0.5},
            "element": {"linear": 0.001, "angular": 0.5},
        },
        "colliders": {
            "default": "hull",
            "rules": [
                {"regex": "(?i)angle bracket", "mode": "coacd", "threshold": 0.05, "max_hulls": 8},
                {"regex": "(?i)ring", "mode": "sectors"},
                {"regex": "(?i)post", "mode": "auto"},
                {"regex": "(?i)plate", "mode": "box"},
            ],
            "sectors": 16,
        },
        "validate": [
            {
                "what": "Ball diameter",
                "rule": "elements.pollen_diameter",
                "measure": {"part_regex": "Ball Element", "axis": "x", "kind": "extent_first"},
            },
        ],
    }
    path = tmp / f"{cfg['name']}.yaml"
    path.write_text(yaml.safe_dump(cfg), "utf-8")
    return path


@pytest.fixture(scope="module")
def fixture_dir(tmp_path_factory) -> Path:
    tmp = tmp_path_factory.mktemp("cad")
    make_assembly(tmp / "fixture.step")
    with zipfile.ZipFile(tmp / "fixture.zip", "w") as z:
        z.write(tmp / "fixture.step", "fixture.step")
    return tmp


@pytest.fixture(scope="module")
def result(fixture_dir: Path):
    pipeline = run.Pipeline(write_config(fixture_dir), out_root=fixture_dir, source_root=fixture_dir)
    validation = pipeline.run()
    return pipeline, validation


def manifest_parts(pipeline) -> dict[str, list[dict]]:
    """Manifest parts by name. Instances of one CAD part definition share its name."""
    data = json.loads((pipeline.out / "manifest.json").read_text("utf-8"))
    by_name: dict[str, list[dict]] = {}
    for p in data["parts"]:
        by_name.setdefault(p["name"], []).append(p)
    return by_name


def extents(part: dict) -> np.ndarray:
    return np.array(part["bbox_max"]) - np.array(part["bbox_min"])


def test_millimetre_step_is_read_in_metres(result) -> None:
    pipeline, _ = result
    plate = manifest_parts(pipeline)["Box Plate A"][0]
    assert sorted(extents(plate)) == pytest.approx([0.02, 0.1, 0.2], abs=1e-4)


def test_identical_parts_are_instanced(result) -> None:
    pipeline, _ = result
    plates = manifest_parts(pipeline)["Box Plate A"]
    assert len(plates) == 2 and len({p["prototype"] for p in plates}) == 1
    assert plates[0]["signature"] == plates[1]["signature"]
    assert plates[0]["id"] != plates[1]["id"]  # instance ids come from the assembly path
    layout = json.loads((pipeline.godot_out / "layout.json").read_text("utf-8"))
    balls = next(g for g in layout["groups"] if g["name"] == "balls")
    assert balls["kind"] == "element" and len(balls["instances"]) == 3
    assert (pipeline.godot_out / "balls.glb").exists()
    # Element poses are body centres: the three balls are 0.1 m apart along X.
    xs = sorted(np.array(i["transform"]).reshape(4, 4)[0, 3] for i in balls["instances"])
    assert np.diff(xs) == pytest.approx([0.1, 0.1], abs=1e-4)


def test_drop_rule_removes_part_from_output(result) -> None:
    pipeline, _ = result
    screw = manifest_parts(pipeline)["Hex screw"][0]
    assert screw["dropped"] is True and screw["triangles"] == 0


def test_collider_modes(result) -> None:
    pipeline, _ = result
    shapes = json.loads((pipeline.godot_out / "static.colliders.json").read_text("utf-8"))
    types = [s["type"] for s in shapes]
    assert types.count("box") == 2  # the two plates
    assert "cylinder" in types  # the post (auto mode fits a cylinder)
    convex = [s for s in shapes if s["type"] == "convex"]
    assert len(convex) >= 2 + 8  # L-bracket decomposed + ring sectors
    ball = json.loads((pipeline.godot_out / "balls.colliders.json").read_text("utf-8"))
    assert ball == [pytest.approx(ball[0])] and ball[0]["type"] == "sphere"
    assert ball[0]["radius"] == pytest.approx(BALL_RADIUS_MM / 1000, abs=2e-4)


def test_ring_sectors_keep_the_opening_free(result) -> None:
    pipeline, _ = result
    ring = manifest_parts(pipeline)["Flower Layer Ring"][0]
    centre = (np.array(ring["bbox_min"]) + np.array(ring["bbox_max"])) / 2
    shapes = json.loads((pipeline.godot_out / "static.colliders.json").read_text("utf-8"))
    near = [
        s
        for s in shapes
        if s["type"] == "convex" and np.linalg.norm(np.mean(s["points"], axis=0) - centre) < 0.07
    ]
    assert len(near) >= 8
    pts = np.concatenate([np.array(s["points"]) for s in near])
    radial = np.linalg.norm((pts - centre)[:, [0, 1]], axis=1)  # ring axis = CAD Z (up axis Y keeps it)
    assert radial.min() >= 0.030 - 0.0015  # 30 mm hole stays open (chord error of 16 sectors)


def test_validation_uses_manual_values(result) -> None:
    _, validation = result
    assert validation and all(v["ok"] for v in validation), validation


def test_rerun_is_stable(result, fixture_dir: Path) -> None:
    pipeline, _ = result
    first = {f.name: f.read_bytes() for f in pipeline.godot_out.glob("*.json")}
    ids_first = sorted(
        p["id"] for p in json.loads((pipeline.out / "manifest.json").read_text("utf-8"))["parts"]
    )
    again = run.Pipeline(write_config(fixture_dir), out_root=fixture_dir, source_root=fixture_dir)
    again.run()
    second = {f.name: f.read_bytes() for f in again.godot_out.glob("*.json")}
    ids_second = sorted(
        p["id"] for p in json.loads((again.out / "manifest.json").read_text("utf-8"))["parts"]
    )
    assert first == second
    assert ids_first == ids_second


def test_z_up_source_is_rotated_to_y_up(fixture_dir: Path) -> None:
    pipeline = run.Pipeline(
        write_config(fixture_dir, up_axis="Z"), out_root=fixture_dir, source_root=fixture_dir
    )
    pipeline.run()
    plate = manifest_parts(pipeline)["Box Plate A"][0]
    # The 200 mm side lies along CAD Z; with Z as the up axis it becomes Godot Y.
    assert extents(plate)[1] == pytest.approx(0.2, abs=1e-4)
