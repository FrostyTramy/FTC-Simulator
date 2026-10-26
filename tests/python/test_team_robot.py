"""config/robots/robodojo_23205.yaml (team robot): schema, a source next to every number, known motor/servo models,
unique ports, rule limits (R102 start cube, R503 motor/servo counts) and agreement with the TeleOp written for it."""

from __future__ import annotations

import json
import re
from pathlib import Path
from typing import Any

import jsonschema
import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
ROBOT = yaml.safe_load((REPO_ROOT / "config" / "robots" / "robodojo_23205.yaml").read_text("utf-8"))
SCHEMA = json.loads((REPO_ROOT / "config" / "schemas" / "robot.schema.json").read_text("utf-8"))
MOTORS = yaml.safe_load((REPO_ROOT / "config" / "motors.yaml").read_text("utf-8"))
SERVOS = yaml.safe_load((REPO_ROOT / "config" / "servos.yaml").read_text("utf-8"))
TELEOP = (REPO_ROOT / "tests" / "compat" / "robodojo_23205" / "TeamCode" / "src" / "main" / "java" / "org" / "firstinspires"
          / "ftc" / "teamcode" / "RoboDojoTeleOp.java").read_text("utf-8")


def test_schema_valid() -> None:
    jsonschema.validate(ROBOT, SCHEMA)


def test_every_number_sits_next_to_a_source() -> None:
    def walk(node: Any, path: str, has_source: bool) -> None:
        if isinstance(node, dict):
            here = has_source or "source" in node
            for key, child in node.items():
                if key not in ("i2c_bus", "port"):   # wiring labels (ASSUMED ports are noted on the device)
                    walk(child, f"{path}.{key}", here)
        elif isinstance(node, list):
            for i, child in enumerate(node):
                walk(child, f"{path}[{i}]", has_source)
        elif isinstance(node, (int, float)) and not isinstance(node, bool):
            assert has_source, f"{path} = {node} has no source"

    for key, value in ROBOT.items():
        if key not in ("version",):
            walk(value, key, False)


def test_models_exist_and_ports_are_unique() -> None:
    seen = set()
    for d in ROBOT["devices"]:
        if d["type"] == "motor":
            assert d["model"] in MOTORS["models"], d["name"]
        if d["type"] in ("servo", "crservo"):
            assert d["model"] in SERVOS["models"], d["name"]
        if "port" in d:
            key = (d["hub"], "servo" if d["type"] in ("servo", "crservo") else d["type"], d["port"])
            assert key not in seen, f"two devices on {key}"
            seen.add(key)


def test_rule_limits() -> None:
    motors = [d for d in ROBOT["devices"] if d["type"] == "motor"]
    servos = [d for d in ROBOT["devices"] if d["type"] in ("servo", "crservo")]
    assert len(motors) == ROBOT["checks"]["motor_count"]["value"] <= 8
    assert len(servos) == ROBOT["checks"]["servo_count"]["value"]
    assert all(v <= 0.4572 for v in ROBOT["dimensions"]["bbox_size"]["value"])


def test_teleop_uses_exactly_the_configured_device_names() -> None:
    used = set(re.findall(r'hardwareMap\.get\(\w+\.class, "([^"]+)"\)', TELEOP))
    configured = {d["name"] for d in ROBOT["devices"] if d["type"] != "imu"}
    assert used == configured


def test_turret_gearing_matches_the_teleop() -> None:
    turret = next(d for d in ROBOT["devices"] if d["name"] == "turret")
    teeth = float(re.search(r"TURRET_RING_TEETH = (\d+)", TELEOP).group(1))
    assert abs(turret["speed_ratio"] - 24 / teeth) < 1e-6
    sign = float(re.search(r"TURRET_ENCODER_SIGN = (-?\d+)", TELEOP).group(1))
    assert sign * turret["encoder_counts_per_output_rev"]["value"] > 0   # the code undoes the counter-rotating encoder


def test_gate_positions_in_the_teleop_open_and_close_the_gate() -> None:
    # goBILDA 25-3 servo, SDK default pulse 600..2400 us, 0.15 deg/us → angle from centre = (1800·p − 900)·0.15.
    lo, hi = ROBOT["ball_path"]["gate"]["value"]["open_servo_angles"]

    def angle(pos: float) -> float:
        return (1800 * pos - 900) * 0.15

    closed = float(re.search(r"GATE_CLOSED = ([\d.]+)", TELEOP).group(1))
    opened = float(re.search(r"GATE_OPEN = ([\d.]+)", TELEOP).group(1))
    assert not lo <= angle(closed) <= hi
    assert lo <= angle(opened) <= hi
