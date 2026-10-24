"""config/field_mechanisms.yaml: schema, consistency with the rules and with the field CAD."""

from __future__ import annotations

import json
from pathlib import Path

import jsonschema
import numpy as np
import pytest
import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
MECH = yaml.safe_load((REPO_ROOT / "config" / "field_mechanisms.yaml").read_text("utf-8"))
RULES = yaml.safe_load((REPO_ROOT / "config" / "rules_biobuzz.yaml").read_text("utf-8"))
SCHEMA = json.loads((REPO_ROOT / "config" / "schemas" / "field_mechanisms.schema.json").read_text("utf-8"))
DERIVED_PATH = REPO_ROOT / "generated" / "field" / "mechanisms_derived.yaml"
INCH = 0.0254


def derived() -> dict:
    if not DERIVED_PATH.exists():
        pytest.skip("run tools/field/derive_mechanisms.py first (needs the field CAD)")
    return yaml.safe_load(DERIVED_PATH.read_text("utf-8"))


def test_schema_valid() -> None:
    jsonschema.validate(MECH, SCHEMA)


def test_start_layout_counts_match_rules() -> None:
    pollen = sum(q["value"] for q in MECH["start_layout"]["pollen"].values())
    nectar = sum(q["value"] for q in MECH["start_layout"]["nectar"].values())
    assert pollen == RULES["elements"]["pollen_count"]["value"]
    assert nectar == 2 * RULES["elements"]["nectar_count_per_alliance"]["value"]


def test_apriltag_ids_match_rules() -> None:
    rule_ids = RULES["apriltags"]["ids"]["value"]
    by_cell = {c["id"]: c["apriltag_ids"]["value"] for c in MECH["triggers"]["cells"]}
    assert by_cell["cell_red_rear"] == rule_ids["red_rear"]
    assert by_cell["cell_red_audience"] == rule_ids["red_audience"]
    assert by_cell["cell_blue_audience"] == rule_ids["blue_audience"]
    assert by_cell["cell_blue_rear"] == rule_ids["blue_rear"]
    sticker_ids = sorted(i for s in MECH["apriltag_stickers"] for i in s["ids"])
    assert sticker_ids == list(range(30, 46))


def test_pivot_height_matches_manual() -> None:
    expected = RULES["hive"]["pivot_height"]["value"] * INCH
    for joint in MECH["joints"]:
        assert abs(joint["anchor"]["value"][1] - expected) < 0.005, joint["id"]


def test_hive_stable_states_are_mirror_images() -> None:
    for joint in MECH["joints"]:
        lo, hi = joint["angle_range"]["value"]
        assert hi - lo == pytest.approx(60.0)
        assert sorted(joint["stable_states"]["value"].values()) == [lo, hi]


def test_zones_have_manual_sizes() -> None:
    zones = MECH["triggers"]["zones"]
    for c in ("red", "blue"):
        lz = zones[f"loading_zone_{c}"]
        depth, width = sorted([lz["x"][1] - lz["x"][0], lz["z"][1] - lz["z"][0]])
        assert abs(depth - 11 * INCH) < 1.0 * INCH and abs(width - 23 * INCH) < 1.0 * INCH
        g = zones[f"garden_{c}"]
        short, long_ = sorted([g["x"][1] - g["x"][0], g["z"][1] - g["z"][0]])
        assert abs(long_ - 23 * INCH) < 1.0 * INCH and abs(short - 2 * INCH) < 1.0 * INCH
        a = zones[f"alliance_area_{c}"]
        depth, width = sorted([a["x"][1] - a["x"][0], a["z"][1] - a["z"][0]])
        assert abs(depth - 54 * INCH) < 1.0 * INCH and abs(width - 97 * INCH) < 1.0 * INCH


def test_loading_zones_and_gardens_on_the_right_walls() -> None:
    zones = MECH["triggers"]["zones"]
    assert zones["loading_zone_red"]["x"][0] < 0 < zones["loading_zone_blue"]["x"][0]
    assert zones["loading_zone_red"]["z"][1] < 0  # red loading zone towards the rear (tile A5)
    assert zones["loading_zone_blue"]["z"][0] > 0  # blue loading zone towards the audience (tile F2)
    assert zones["garden_red"]["z"][0] > 0 and zones["garden_blue"]["z"][1] < 0


def test_flower_top_height_matches_manual() -> None:
    top = MECH["triggers"]["flower_volume"]["y_max"]["value"]
    assert abs(top - RULES["flower"]["top_opening_height"]["value"] * INCH) < INCH


def test_config_matches_cad_derivation() -> None:
    d = derived()
    for key, q in MECH["walls"].items():
        if key != "note":
            assert q["value"] == pytest.approx(d["walls"][key]["value"], abs=1e-3)
    for name, z in MECH["triggers"]["zones"].items():
        if name in d["zones"]:
            assert z["x"] == pytest.approx(d["zones"][name]["x"], abs=1e-3), name
            assert z["z"] == pytest.approx(d["zones"][name]["z"], abs=1e-3), name
    flowers = {f["id"]: f for f in d["flowers"]}
    for f in MECH["triggers"]["flowers"]:
        assert f["axis_xz"]["value"] == pytest.approx(flowers[f["id"]]["axis_xz"], abs=1e-3)
    hives = {h["id"]: h for h in d["hives"]}
    for joint in MECH["joints"]:
        hive = hives[joint["body_b"]]
        assert joint["anchor"]["value"] == pytest.approx(hive["pivot"]["point"], abs=1e-3)
        assert np.abs(np.dot(joint["axis"]["value"], hive["pivot"]["axis"])) == pytest.approx(1.0, abs=1e-3)
    cells = {c["id"]: c for h in d["hives"] for c in h["cells"]}
    for cell in MECH["triggers"]["cells"]:
        assert cell["obb_transform"]["value"] == pytest.approx(cells[cell["id"]]["obb_transform"], abs=1e-3)
        assert cell["obb_size"]["value"] == pytest.approx(cells[cell["id"]]["obb_size"], abs=1e-3)
    assert len(d["elements"]["pollen"]["positions"]) == RULES["elements"]["pollen_count"]["value"]
