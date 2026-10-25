"""Hardware configs (motors, servos, battery, latency, gamepads): schema, a source on every number, and
consistency checks against the datasheet relations the models rely on."""

from __future__ import annotations

import json
import math
import re
from pathlib import Path
from typing import Any

import jsonschema
import pytest
import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
CONFIGS = ["motors", "servos", "battery", "hardware_latency", "gamepads"]
SOURCE_RE = re.compile(r"(https?://|SDK|goBILDA|REV|gm0|ASSUMED|R\d{3}|§\d|Table \d|specs/\d)")
KGF_CM = 0.0980665  # N·m per kgf·cm


def load(name: str) -> dict:
    return yaml.safe_load((REPO_ROOT / "config" / f"{name}.yaml").read_text("utf-8"))


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


@pytest.mark.parametrize("name", CONFIGS)
def test_schema_valid(name: str) -> None:
    schema = json.loads((REPO_ROOT / "config" / "schemas" / f"{name}.schema.json").read_text("utf-8"))
    jsonschema.validate(load(name), schema)


@pytest.mark.parametrize("name", CONFIGS)
def test_every_quantity_has_a_traceable_source(name: str) -> None:
    found = list(quantities(load(name)))
    assert found
    for path, q in found:
        assert SOURCE_RE.search(q["source"]), f"{name}{path}: source '{q['source']}' is not traceable"


@pytest.mark.parametrize("name", CONFIGS)
def test_no_bare_numbers_outside_quantities(name: str) -> None:
    def walk(node: Any, path: str, inside: bool) -> None:
        if isinstance(node, dict):
            is_q = {"value", "unit", "source"} <= node.keys()
            for key, child in node.items():
                walk(child, f"{path}.{key}", inside or is_q)
        elif isinstance(node, list):
            for i, child in enumerate(node):
                walk(child, f"{path}[{i}]", inside)
        elif isinstance(node, (int, float)) and not isinstance(node, bool):
            assert inside, f"{name}{path}: number {node} without a source"

    walk(load(name), "", False)


def test_motor_encoder_counts_match_ratio() -> None:
    """counts per output rev = 28 counts at the motor x gear ratio (goBILDA formula)."""
    for key, m in load("motors")["models"].items():
        expected = 28 * m["gear_ratio"]["value"]
        assert abs(m["counts_per_output_rev"]["value"] - expected) / expected < 0.002, key


def test_motor_free_speed_matches_ratio() -> None:
    """All 5203 motors share the RS-555 motor: free speed x ratio ~ 6000 RPM. goBILDA rounds the
    slow ratios (188:1 is listed as 30 RPM = 5658 RPM at the motor), hence 6 %."""
    for key, m in load("motors")["models"].items():
        motor_rpm = m["free_speed"]["value"] * m["gear_ratio"]["value"]
        assert abs(motor_rpm - 6000) / 6000 < 0.06, f"{key}: {motor_rpm:.0f} RPM at the motor"


def test_motor_gearbox_efficiency_plausible() -> None:
    """Datasheet stall torque / (bare-motor stall torque x ratio) is the implied efficiency."""
    models = load("motors")["models"]
    bare = models["gobilda_5203_1_1"]["stall_torque"]["value"]
    for key, m in models.items():
        eff = m["stall_torque"]["value"] / (bare * m["gear_ratio"]["value"])
        assert 0.8 < eff < 1.06, f"{key}: implied efficiency {eff:.3f}"


def test_motor_constants_derivable() -> None:
    """R, Kt, Ke derived at the output shaft are positive and the free current < stall current."""
    for key, m in load("motors")["models"].items():
        v = m["nominal_voltage"]["value"]
        r = v / m["stall_current"]["value"]
        w_free = m["free_speed"]["value"] * 2 * math.pi / 60
        ke = (v - m["free_current"]["value"] * r) / w_free
        kt = m["stall_torque"]["value"] * KGF_CM / m["stall_current"]["value"]
        assert r > 0 and ke > 0 and kt > 0, key


def test_sdk_achievable_max_ticks_quirk() -> None:
    """The SDK's single goBILDA type reports 2368 achievable ticks/s for every ratio."""
    t = load("motors")["sdk_motor_types"]["gobilda_5202_series"]
    ticks = (
        t["ticks_per_rev"]["value"] * t["max_rpm"]["value"] * t["achievable_max_rpm_fraction"]["value"] / 60
    )
    assert round(ticks) == 2368


def test_servo_speed_from_seconds_per_60deg() -> None:
    """RPM listed by goBILDA agrees with its seconds-per-60-degree rating within 15 %."""
    secs = {"gobilda_2000_0025_0002": [0.25, 0.20, 0.17], "gobilda_2000_0025_0003": [0.11, 0.09, 0.07]}
    for key, s in load("servos")["models"].items():
        for rpm, t60 in zip(s["no_load_speed"]["value"], secs[key], strict=True):
            assert abs(rpm - (60 / t60) / 6) / rpm < 0.15, key


def test_servo_travel_matches_pwm_span() -> None:
    for key, s in load("servos")["models"].items():
        lo, hi = s["standard_pwm_range"]["value"]
        assert abs((hi - lo) * s["degrees_per_us"]["value"] - s["travel"]["value"]) < 1e-9, key


def test_battery_ocv_curve_monotonic() -> None:
    for key, b in load("battery")["models"].items():
        curve = b["ocv_curve"]["value"]
        socs = [p[0] for p in curve]
        volts = [p[1] for p in curve]
        assert socs[0] == 0.0 and socs[-1] == 1.0, key
        assert all(a < b for a, b in zip(socs, socs[1:])), key
        assert all(a <= b for a, b in zip(volts, volts[1:])), key
        assert volts[0] >= 9.0 or b["minimum_resting_voltage"]["value"] > volts[0], key
