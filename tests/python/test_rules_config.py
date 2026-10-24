"""config/rules_biobuzz.yaml: schema, sources on every number, and consistency checks."""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any

import jsonschema
import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
RULES = yaml.safe_load((REPO_ROOT / "config" / "rules_biobuzz.yaml").read_text("utf-8"))
SCHEMA = json.loads((REPO_ROOT / "config" / "schemas" / "rules_biobuzz.schema.json").read_text("utf-8"))
GAME_RULES_MD = (REPO_ROOT / "docs" / "game_rules.md").read_text("utf-8")
SOURCE_RE = re.compile(r"(§\d|Table \d|Fig \d|[GR]\d{3}|SIM|ASSUMED)")


def v(section: str, key: str) -> Any:
    return RULES[section][key]["value"]


def quantities(node: Any, path: str = ""):
    if isinstance(node, dict):
        if {"value", "unit", "source"} <= node.keys():
            yield path, node
            return
        for key, child in node.items():
            yield from quantities(child, f"{path}.{key}")
    elif isinstance(node, list):
        for i, child in enumerate(node):
            yield from quantities(child, f"{path}[{i}]")


def test_schema_valid() -> None:
    jsonschema.validate(RULES, SCHEMA)


def test_every_quantity_has_a_traceable_source() -> None:
    found = list(quantities(RULES))
    assert len(found) > 80
    for path, q in found:
        assert SOURCE_RE.search(q["source"]), f"{path}: source '{q['source']}' has no § / Table / Fig / rule"


def test_no_bare_numbers_outside_quantities() -> None:
    def walk(node: Any, path: str, inside: bool) -> None:
        if isinstance(node, dict):
            is_q = {"value", "unit", "source"} <= node.keys()
            for key, child in node.items():
                walk(child, f"{path}.{key}", inside or is_q)
        elif isinstance(node, list):
            for i, child in enumerate(node):
                walk(child, f"{path}[{i}]", inside)
        elif isinstance(node, (int, float)) and not isinstance(node, bool):
            assert inside, f"{path}: number {node} without a source"

    walk(RULES, "", False)


def test_element_counts_add_up() -> None:
    pollen = (
        v("setup", "pollen_per_flower") * v("flower", "count")
        + 2 * v("setup", "pollen_per_garden")
        + 2 * v("setup", "robots_per_alliance") * v("setup", "pollen_preload_per_robot")
    )
    assert pollen == v("elements", "pollen_count") == 40
    nectar = v("setup", "nectar_in_up_cell") + v("setup", "nectar_in_alliance_area")
    assert nectar == v("elements", "nectar_count_per_alliance") == 8


def test_match_length_matches_field_timer() -> None:
    assert v("timing", "auto") + v("timing", "teleop") == 150  # 2:30 on the field timer, Table 9-1


def test_apriltag_ids_unique_and_complete() -> None:
    ids = [i for cell in v("apriltags", "ids").values() for i in cell]
    assert sorted(ids) == list(range(30, 46))


def test_point_values_appear_in_game_rules_md() -> None:
    table = re.search(r"### 8\.1 Point values.*?\n\n", GAME_RULES_MD, re.S).group(0)
    rows = {
        "LEAVE": (v("scoring", "leave_auto"),),
        "PARK": (v("scoring", "park_auto"), v("scoring", "park_teleop")),
        "HIVE TIP": (v("scoring", "hive_tip_auto"), v("scoring", "hive_tip_teleop")),
        "remaining in CELL": (v("scoring", "cell_element_end"),),
        "Bottom NECTAR Bonus": (v("scoring", "bottom_nectar_bonus"),),
        "owned FLOWER": (v("scoring", "owned_flower_element"),),
        "in GARDEN": (v("scoring", "garden_element"),),
    }
    for label, values in rows.items():
        line = next(line for line in table.splitlines() if label in line)
        cells = [c.strip() for c in line.split("|")]
        for value in values:
            assert str(value) in cells, f"{label}: {value} not in '{line}'"


def test_robot_limits_match_game_rules_md() -> None:
    assert "**18 × 18 × 18 in" in GAME_RULES_MD
    assert "**18 × 24 in (45.70 × 61.0 cm) footprint × 29 in" in GAME_RULES_MD
    assert v("robot_limits", "start_cube") == [18, 18, 18]
    assert v("robot_limits", "expansion") == [18, 24, 29]
    assert v("robot_limits", "max_motors") == v("robot_limits", "max_servos") == 8


def test_foul_rules_are_documented() -> None:
    for foul in RULES["fouls"]:
        assert f"| {foul['rule']} |" in GAME_RULES_MD, foul["rule"]
