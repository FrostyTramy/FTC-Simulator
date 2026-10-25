"""Robot variants (config/robots/*.yaml with `base:`, spec 05 robot library): the merged robot is a valid robot,
every sensor number has a source, hardwareMap names are unique and every sensor model exists in config/sensors.yaml."""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import jsonschema
import pytest
import yaml

REPO_ROOT = Path(__file__).resolve().parents[2]
ROBOTS = REPO_ROOT / "config" / "robots"
SCHEMA = json.loads((REPO_ROOT / "config" / "schemas" / "robot.schema.json").read_text("utf-8"))
SENSORS = yaml.safe_load((REPO_ROOT / "config" / "sensors.yaml").read_text("utf-8"))
VARIANTS = sorted(p.stem for p in ROBOTS.glob("*.yaml") if "base" in yaml.safe_load(p.read_text("utf-8")))
SDK_CLASSES = {
    "pinpoint": {"GoBildaPinpointDriver"},
    "otos": {"SparkFunOTOS"},
    "color": {"RevColorSensorV3"},
    "distance": {"Rev2mDistanceSensor"},
    "touch": {"TouchSensor", "DigitalChannel"},
    "webcam": {"WebcamName"},
}


def merged(name: str) -> dict[str, Any]:
    """Same rule as RobotDefinition.Load: the variant replaces whole top-level sections of its base."""
    raw = yaml.safe_load((ROBOTS / f"{name}.yaml").read_text("utf-8"))
    if "base" not in raw:
        return raw
    robot = merged(raw["base"])
    robot.update({k: v for k, v in raw.items() if k != "base"})
    return robot


def test_there_is_a_variant() -> None:
    assert "gobilda_starter_sensors" in VARIANTS


@pytest.mark.parametrize("name", VARIANTS)
def test_merged_robot_is_schema_valid(name: str) -> None:
    robot = merged(name)
    assert robot["name"] == name
    jsonschema.validate(robot, SCHEMA)


@pytest.mark.parametrize("name", VARIANTS)
def test_sensor_numbers_have_sources_and_models_exist(name: str) -> None:
    robot = merged(name)
    for s in robot.get("sensors", []):
        assert s["sdk_class"] in SDK_CLASSES[s["type"]], s["name"]
        section = {"touch": "touch"}.get(s["type"], s["type"])
        assert s["model"] in SENSORS[section], f"{s['name']}: unknown model {s['model']}"
        for key, q in s.items():
            if isinstance(q, dict):
                assert "ASSUMED" in q["source"] or len(q["source"]) > 10, f"{s['name']}.{key}"
        pose = s["pose"]["value"]
        f, u = pose.get("forward", [0, 0, -1]), pose.get("up", [0, 1, 0])
        assert abs(sum(a * b for a, b in zip(f, u))) < 1e-3, f"{s['name']}: forward and up not perpendicular"


@pytest.mark.parametrize("name", VARIANTS)
def test_hardware_names_are_unique(name: str) -> None:
    robot = merged(name)
    names = [d["name"] for d in robot["devices"]] + [s["name"] for s in robot.get("sensors", [])]
    assert len(names) == len(set(names))
