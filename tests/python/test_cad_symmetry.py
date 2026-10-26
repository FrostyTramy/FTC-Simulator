"""CAD pipeline symmetry (tools/cad_pipeline/symmetry.py): mirrored twins keep their own geometry, one-sided parts are
reported and filled with a mirrored copy when a rule asks for it, and a part whose faces do not mesh is drawn from its
healthy mirrored twin instead of disappearing (USER 2026-10-26: intake corner ramp and wheel guard missing on one side)."""

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
import mesh as mesh_mod  # noqa: E402
import run  # noqa: E402
import symmetry  # noqa: E402

CENTRE_X_MM = 12.0   # mirror plane x = 12 mm, like the RoboDojo robot's wheel centre plane


def corner_ramp() -> cq.Workplane:
    """A chiral wedge (its mirror image is a different solid): a ramp that guides balls in from the left corner."""
    return cq.Workplane("XY").polyline([(0, 0), (60, 0), (60, 15), (15, 45), (0, 45)]).close().extrude(8)


def make_robot(step_path: Path) -> None:
    ramp = corner_ramp()
    guard = cq.Workplane("XY").box(4, 120, 60)
    assy = cq.Assembly(name="robot")
    # Ramps at both front corners: the right one is its own (mirrored) solid, as a Fusion mirror exports it.
    assy.add(ramp, name="rampa colt stanga", loc=cq.Location((CENTRE_X_MM + 100, -200, 0)))
    assy.add(ramp.mirror("YZ"), name="rampa colt dreapta", loc=cq.Location((CENTRE_X_MM - 100, -200, 0)))
    # Wheel guard plate on the left side only.
    assy.add(guard, name="aparatoare roti", loc=cq.Location((CENTRE_X_MM + 170, 0, 30)))
    # A one-sided part that no rule matches (a hub).
    assy.add(cq.Workplane("XY").box(30, 143, 103), name="Control Hub", loc=cq.Location((CENTRE_X_MM + 135, 100, 60)))
    assy.export(str(step_path))


def config(tmp: Path, name: str, rules: list[dict]) -> Path:
    cfg = {
        "name": name,
        "source": {"zip": str(tmp / "robot.zip"), "member": "robot.step"},
        "up_axis": "Z",
        "default_group": {"name": "chassis", "kind": "rigid"},
        "groups": [],
        "tessellation": {"default": {"linear": 0.0005, "angular": 0.5}, "element": {"linear": 0.0005, "angular": 0.5}},
        "colliders": {"default": "hull"},
        "symmetry": {"plane": {"normal": [1, 0, 0], "point": [CENTRE_X_MM / 1000, 0, 0]}, "mirror_unpaired": rules},
    }
    path = tmp / f"{name}.yaml"
    path.write_text(yaml.safe_dump(cfg), "utf-8")
    return path


@pytest.fixture(scope="module")
def robot_dir(tmp_path_factory) -> Path:
    tmp = tmp_path_factory.mktemp("sym")
    make_robot(tmp / "robot.step")
    with zipfile.ZipFile(tmp / "robot.zip", "w") as z:
        z.write(tmp / "robot.step", "robot.step")
    return tmp


def run_pipeline(tmp: Path, name: str, rules: list[dict]) -> run.Pipeline:
    pipeline = run.Pipeline(config(tmp, name, rules), out_root=tmp, source_root=tmp)
    pipeline.run()
    return pipeline


def parts(pipeline: run.Pipeline, prefix: str) -> list[dict]:
    data = json.loads((pipeline.out / "manifest.json").read_text("utf-8"))
    return [p for p in data["parts"] if p["name"].startswith(prefix)]


def test_plane_reflection_is_a_mirror() -> None:
    plane = symmetry.Plane.from_config({"normal": [1, 0, 0], "point": [0.012, 0, 0]}, np.eye(4))
    m = plane.matrix()
    assert np.linalg.det(m[:3, :3]) == pytest.approx(-1.0)
    assert (m @ [0.112, 0.3, 0.1, 1.0])[:3] == pytest.approx([-0.088, 0.3, 0.1])


def test_mirrored_twins_pair_and_keep_their_own_geometry(robot_dir: Path) -> None:
    pipeline = run_pipeline(robot_dir, "sym_report", [])
    ramps = parts(pipeline, "rampa colt")
    assert len(ramps) == 2
    assert ramps[0]["signature"] != ramps[1]["signature"]   # mirror images do not share one tessellation
    unpaired = {i.name for i in pipeline.unpaired}
    assert not any(n.startswith("rampa colt") for n in unpaired)
    assert {"aparatoare roti", "Control Hub"} <= unpaired
    assert "Largest one-sided parts" in (pipeline.out / "report.md").read_text("utf-8")


def test_rule_mirrors_a_one_sided_part_into_an_empty_position(robot_dir: Path) -> None:
    pipeline = run_pipeline(robot_dir, "sym_fill", [{"part_regex": "(?i)aparatoare|rampa", "min_extent": 0.02}])
    guards = parts(pipeline, "aparatoare roti")
    assert len(guards) == 2
    copy = next(g for g in guards if g.get("mirrored_from"))
    centre = (np.array(copy["bbox_min"]) + np.array(copy["bbox_max"])) / 2
    assert centre[0] == pytest.approx(0.012 - 0.170, abs=1e-3)   # mirrored about x = 12 mm
    assert copy["triangles"] > 0
    assert len(parts(pipeline, "rampa colt")) == 2      # already paired: no extra copy
    assert len(parts(pipeline, "Control Hub")) == 1     # no rule matches it


def test_part_that_does_not_mesh_is_drawn_from_its_mirrored_twin(robot_dir: Path, monkeypatch) -> None:
    real = run.tessellate_checked
    calls = {"n": 0}

    def broken_once(shape, linear, angular):
        tess = real(shape, linear, angular)
        props = mesh_mod.mass_props(shape)
        if props.inertia[3] < 0 and calls["n"] == 0:   # the right-hand (mirrored) ramp: pretend no face meshes
            calls["n"] += 1
            empty = mesh_mod.Mesh(np.zeros((0, 3), np.float32), np.zeros((0, 3), np.float32), np.zeros((0, 3), np.int32))
            return mesh_mod.Tessellation(empty, tess.faces, tess.faces)
        return tess

    monkeypatch.setattr(run, "tessellate_checked", broken_once)
    pipeline = run_pipeline(robot_dir, "sym_repair", [])
    ramps = parts(pipeline, "rampa colt")
    broken = [r for r in ramps if r["faces_not_meshed"] > 0]
    assert len(broken) == 1 and broken[0]["rendered_from_mirrored"]
    assert all(r["triangles"] > 0 for r in ramps)   # both ramps are drawn
    assert "Tessellation problems" in (pipeline.out / "report.md").read_text("utf-8")
