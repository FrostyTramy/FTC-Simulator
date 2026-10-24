"""config/physics.yaml: schema, a source on every number, consistency with the rules and materials."""

from __future__ import annotations

import json
import math
from pathlib import Path
from typing import Any

import jsonschema
import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
PHYS = yaml.safe_load((REPO_ROOT / "config" / "physics.yaml").read_text("utf-8"))
SCHEMA = json.loads((REPO_ROOT / "config" / "schemas" / "physics.schema.json").read_text("utf-8"))
RULES = yaml.safe_load((REPO_ROOT / "config" / "rules_biobuzz.yaml").read_text("utf-8"))
INCH = 0.0254


def test_schema_valid() -> None:
    jsonschema.validate(PHYS, SCHEMA)


def test_every_number_sits_next_to_a_source() -> None:
    def walk(node: Any, path: str, has_source: bool) -> None:
        if isinstance(node, dict):
            here = has_source or "source" in node
            for key, child in node.items():
                walk(child, f"{path}.{key}", here)
        elif isinstance(node, list):
            for i, child in enumerate(node):
                walk(child, f"{path}[{i}]", has_source)
        elif isinstance(node, (int, float)) and not isinstance(node, bool):
            assert has_source, f"{path}: number {node} without a source"

    for key, section in PHYS.items():
        if key != "version":
            walk(section, key, False)


def test_contact_pairs_use_declared_materials_once() -> None:
    names = set(PHYS["materials"])
    seen = set()
    for pair in PHYS["contacts"]["pairs"]:
        assert pair["a"] in names and pair["b"] in names, pair
        key = frozenset((pair["a"], pair["b"]))
        assert key not in seen, f"duplicate pair {sorted(key)}"
        seen.add(key)
        assert pair["dynamic_friction"] <= pair["static_friction"], pair


def test_element_sizes_match_the_manual() -> None:
    for name, rule in (("pollen", "pollen_diameter"), ("nectar", "nectar_diameter")):
        e = PHYS["elements"][name]
        manual = RULES["elements"][rule]["value"] * INCH
        assert abs(e["diameter"]["value"] - manual) < INCH, name
        assert math.isclose(e["radius"]["value"] * 2, e["diameter"]["value"], rel_tol=1e-6), name
        assert 0.005 < e["mass"]["value"] < 0.2, name


def test_hive_damper_engage_angles_follow_from_stroke_and_lever_arms() -> None:
    d = PHYS["hive"]["damper"]
    stroke = d["stroke"]["value"]
    for arm, engage in zip(d["lever_arms"]["value"], d["engage_angle"]["value"], strict=True):
        assert math.isclose(math.degrees(stroke / arm), engage, abs_tol=0.1)
