"""Broken STEP faces that mesh far outside their edges (USER 2026-10-26: a long bar sticking out of the RoboDojo
turret) are dropped by the mesher, real curved faces are kept, and meshes cached before the check are meshed again."""

from __future__ import annotations

import json
import sys
import zipfile
from pathlib import Path

import numpy as np
import pytest

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "tools" / "cad_pipeline"))
sys.path.insert(0, str(Path(__file__).resolve().parent))

cq = pytest.importorskip("cadquery")
import mesh as mesh_mod  # noqa: E402
import run  # noqa: E402
import test_cad_symmetry as robot  # noqa: E402


def box(lo, hi) -> tuple[np.ndarray, np.ndarray]:
    return np.array(lo, float), np.array(hi, float)


def test_points_far_outside_the_edges_run_away() -> None:
    edges = box([0, 0, 0], [0.03, 0.03, 0.05])   # an encoder-sized part
    inside = np.array([[0, 0, 0], [0.03, 0.03, 0.05]], float)
    bar = np.array([[0, 0, 0], [0.03, 0.03, 0.05], [0.03, 0.03, 0.71]], float)
    assert not mesh_mod.runs_away(inside, edges)
    assert mesh_mod.runs_away(bar, edges)
    assert not mesh_mod.runs_away(bar, None)   # nothing to compare: keep


def test_a_full_sphere_is_not_a_runaway() -> None:
    # The seam of a full sphere is one meridian: the face spreads 1.55 times its edges' box.
    r = 0.05
    seam = box([0, 0, -r], [r, 0, r])
    sphere = np.array([[-r, -r, -r], [r, r, r]], float)
    assert not mesh_mod.runs_away(sphere, seam)


@pytest.mark.parametrize(
    "solid",
    [
        lambda: cq.Workplane("XY").sphere(0.05).val().wrapped,
        lambda: cq.Solid.makeTorus(0.05, 0.01).wrapped,
        lambda: cq.Workplane("XY").cylinder(0.2, 0.02).val().wrapped,
        lambda: cq.Workplane("XY").box(0.4, 0.02, 0.02).edges("|X").fillet(0.005).val().wrapped,
    ],
    ids=["sphere", "torus", "cylinder", "filleted bar"],
)
def test_real_curved_parts_mesh_completely(solid) -> None:
    tess = mesh_mod.tessellate_checked(solid(), 0.0005, 0.5)
    assert tess.missing_faces == 0
    assert tess.mesh.triangle_count > 0


def test_cached_mesh_with_a_bar_is_meshed_again(tmp_path: Path) -> None:
    robot.make_robot(tmp_path / "robot.step")
    with zipfile.ZipFile(tmp_path / "robot.zip", "w") as z:
        z.write(tmp_path / "robot.step", "robot.step")
    config = robot.config(tmp_path, "runaway", [])
    first = run.Pipeline(config, out_root=tmp_path, source_root=tmp_path)
    first.run()
    hub = next(i for i in first.instances if i.name == "Control Hub")
    key = first._mesh_key(hub)
    cached = first.cache / "parts" / f"{key}.npz"
    good = np.load(cached)
    assert len(good["info"]) == 4

    # What a cache from before the check holds for a broken part: the mesh plus a long bar, 3 info fields, and colliders
    # fitted to the bar.
    v = np.concatenate([good["v"], np.array([[0, 0, 0], [0, 0.7, 0], [0.01, 0.7, 0]], np.float32)])
    n = np.concatenate([good["n"], np.zeros((3, 3), np.float32)])
    t = np.concatenate([good["t"], np.array([[len(good["v"]), len(good["v"]) + 1, len(good["v"]) + 2]], np.int32)])
    np.savez_compressed(cached, v=v, n=n, t=t, info=np.array([6, 0, 1]))
    colliders = next((first.cache / "parts").glob(f"{key}_c*.colliders.json"))
    stale = [{"type": "box", "size": [1, 1, 1]}]
    colliders.write_text(json.dumps({"shapes": stale, "info": {}}))

    second = run.Pipeline(config, out_root=tmp_path, source_root=tmp_path)
    second.run()
    remeshed = np.load(cached)
    assert len(remeshed["info"]) == 4
    assert np.ptp(remeshed["v"], axis=0).max() < 0.2   # the Control Hub box is 143 mm at most
    assert json.loads(colliders.read_text())["shapes"] != stale   # colliders fitted again


def test_parts_longer_than_the_limit_are_reported(tmp_path: Path) -> None:
    robot.make_robot(tmp_path / "robot.step")
    with zipfile.ZipFile(tmp_path / "robot.zip", "w") as z:
        z.write(tmp_path / "robot.step", "robot.step")
    config = robot.config(tmp_path, "oversized", [])
    import yaml

    cfg = yaml.safe_load(config.read_text("utf-8"))
    cfg["max_part_extent"] = 0.1   # the Control Hub (143 mm) and the guard plate (120 mm) are longer
    config.write_text(yaml.safe_dump(cfg), "utf-8")
    pipeline = run.Pipeline(config, out_root=tmp_path, source_root=tmp_path)
    pipeline.run()
    assert {n for n, _ in pipeline.oversized} == {"Control Hub", "aparatoare roti"}
    assert "Parts longer than max_part_extent" in (pipeline.out / "report.md").read_text("utf-8")
