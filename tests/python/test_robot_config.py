"""config/robots/gobilda_starter.yaml: schema, sources, rule limits and agreement with the example code
(parsed automatically, spec 05 "Evidence sources")."""

from __future__ import annotations

import json
import sys
from pathlib import Path
from typing import Any

import jsonschema
import pytest
import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "tools" / "robot"))
import parse_opmode  # noqa: E402

ROBOT = yaml.safe_load((REPO_ROOT / "config" / "robots" / "gobilda_starter.yaml").read_text("utf-8"))
SCHEMA = json.loads((REPO_ROOT / "config" / "schemas" / "robot.schema.json").read_text("utf-8"))
RULES = yaml.safe_load((REPO_ROOT / "config" / "rules_biobuzz.yaml").read_text("utf-8"))
MOTORS = yaml.safe_load((REPO_ROOT / "config" / "motors.yaml").read_text("utf-8"))
SERVOS = yaml.safe_load((REPO_ROOT / "config" / "servos.yaml").read_text("utf-8"))
BATTERY = yaml.safe_load((REPO_ROOT / "config" / "battery.yaml").read_text("utf-8"))
EXAMPLE = (
    REPO_ROOT
    / "tests"
    / "compat"
    / "gobilda_starter"
    / "TeamCode"
    / "src"
    / "main"
    / "java"
    / "org"
    / "firstinspires"
    / "ftc"
    / "teamcode"
    / "BioBuzzStarterbotTeleopMecanum.java"
)
CODE = parse_opmode.devices(parse_opmode.parse_source(EXAMPLE))
INCH = 0.0254


def device(name: str) -> dict[str, Any]:
    return next(d for d in ROBOT["devices"] if d["name"] == name)


def test_schema_valid() -> None:
    jsonschema.validate(ROBOT, SCHEMA)


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

    for key, section in ROBOT.items():
        if key != "version":
            walk(section, key, False)


def test_device_names_and_classes_match_the_example_code() -> None:
    sdk_devices = {d["name"]: d for d in ROBOT["devices"] if d["type"] != "imu"}
    assert set(sdk_devices) == set(CODE), "hardwareMap names differ from the example code"
    for name, found in CODE.items():
        assert sdk_devices[name]["sdk_class"] == found["sdk_class"], name


@pytest.mark.parametrize("name", sorted(CODE))
def test_device_settings_match_the_example_code(name: str) -> None:
    found, mine = CODE[name], device(name)
    assert mine.get("direction", "FORWARD") == found.get("direction", "FORWARD"), (
        f"{name} direction ({found.get('direction_ref')})"
    )
    if "zero_power" in found:
        assert mine.get("zero_power") == found["zero_power"], f"{name} zero power ({found['zero_power_ref']})"
    elif mine["type"] == "motor":
        assert mine.get("zero_power") == "FLOAT", f"{name}: SDK default zero power behaviour is FLOAT"
    if "run_mode" in found:
        assert mine.get("run_mode") == found["run_mode"], f"{name} run mode ({found['run_mode_ref']})"
    if "pidf" in found:
        assert mine.get("pidf") == found["pidf"], f"{name} PIDF ({found['pidf_ref']})"


def test_device_models_exist_in_the_hardware_configs() -> None:
    for d in ROBOT["devices"]:
        if d["type"] == "motor":
            assert d["model"] in MOTORS["models"], d["name"]
        elif d["type"] in ("servo", "crservo"):
            assert d["model"] in SERVOS["models"], d["name"]
    assert ROBOT["battery"]["model"]["value"] in BATTERY["models"]


def test_actuator_counts_respect_r503() -> None:
    motors = [d for d in ROBOT["devices"] if d["type"] == "motor"]
    servos = [d for d in ROBOT["devices"] if d["type"] in ("servo", "crservo")]
    assert len(motors) <= RULES["robot_limits"]["max_motors"]["value"]
    assert len(servos) <= RULES["robot_limits"]["max_servos"]["value"]
    assert ROBOT["checks"]["motor_count"]["value"] == len(motors)
    assert ROBOT["checks"]["servo_count"]["value"] == len(servos)


def test_hub_ports_are_unique_and_within_limits() -> None:
    limits = {"motor": 4, "servo": 6, "crservo": 6}  # per hub (spec 05 "Hubs and ports")
    hubs = {h["id"] for h in ROBOT["hubs"]}
    used: dict[tuple[str, str, int], str] = {}
    for d in ROBOT["devices"]:
        if d["type"] not in limits:
            continue
        assert d["hub"] in hubs, d["name"]
        kind = "servo" if d["type"] == "crservo" else d["type"]
        assert 0 <= d["port"] < limits[d["type"]], d["name"]
        key = (d["hub"], kind, d["port"])
        assert key not in used, f"{d['name']} and {used.get(key)} share {key}"
        used[key] = d["name"]


def test_starting_size_fits_r102() -> None:
    cube = [v * INCH for v in RULES["robot_limits"]["start_cube"]["value"]]
    size = ROBOT["dimensions"]["bbox_size"]["value"]
    assert all(s <= c + 1e-6 for s, c in zip(sorted(size), sorted(cube), strict=True))


def test_mecanum_wheels_form_an_x_pattern() -> None:
    wheels = {w["id"]: w for w in ROBOT["drivetrain"]["wheels"]}
    assert wheels["fl"]["slant"] == wheels["br"]["slant"] != wheels["fr"]["slant"] == wheels["bl"]["slant"]
    for w in wheels.values():
        assert w["motor"] in CODE
    track = ROBOT["drivetrain"]["track_width"]["value"]
    assert abs(wheels["fr"]["position"][0] - wheels["fl"]["position"][0] - track) < 1e-3


def test_drive_signs_follow_the_motor_directions() -> None:
    # Left motors are REVERSE in the code (CODE:121, 123), so positive power drives the left side forward
    # only if the left wheels are mounted mirrored: drive_sign −1 on the left, +1 on the right.
    for w in ROBOT["drivetrain"]["wheels"]:
        direction = CODE[w["motor"]].get("direction", "FORWARD")
        assert w["drive_sign"] == (-1 if direction == "REVERSE" else 1), w["id"]
