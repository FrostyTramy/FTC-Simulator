"""Extract robot facts from FTC OpMode Java sources (spec 05 "Evidence sources", example code).

Finds hardwareMap lookups, motor/servo directions, zero-power behaviours, run modes, PIDF
coefficients, servo positions, IMU hub orientation and OpMode annotations, each with file:line, so a
robot YAML can be checked against the code instead of hand-copied references.

Usage: python tools/robot/parse_opmode.py <java file or folder> [--out generated/robot_code_facts.json]
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path
from typing import Any

_PATTERNS: dict[str, re.Pattern] = {
    "device": re.compile(r"(\w+)\s*=\s*hardwareMap\.get\(\s*(\w+)\.class\s*,\s*\"([^\"]+)\"\s*\)"),
    "device_mapping": re.compile(r"(\w+)\s*=\s*hardwareMap\.(\w+)\.get\(\s*\"([^\"]+)\"\s*\)"),
    # Qualified (DcMotor.ZeroPowerBehavior.BRAKE) or statically imported (BRAKE) enum constants.
    "direction": re.compile(r"(\w+)\.setDirection\(\s*(?:(?:\w+\.)*Direction\.)?(FORWARD|REVERSE)\s*\)"),
    "zero_power": re.compile(
        r"(\w+)\.setZeroPowerBehavior\(\s*(?:(?:\w+\.)*ZeroPowerBehavior\.)?(BRAKE|FLOAT|UNKNOWN)\s*\)"
    ),
    "run_mode": re.compile(
        r"(\w+)\.setMode\(\s*(?:(?:\w+\.)*RunMode\.)?"
        r"(RUN_WITHOUT_ENCODER|RUN_USING_ENCODER|RUN_TO_POSITION|STOP_AND_RESET_ENCODER)\s*\)"
    ),
    "pidf": re.compile(
        r"(\w+)\.setPIDFCoefficients\(\s*(?:\w+\.)*RunMode\.(\w+)\s*,\s*new\s+PIDFCoefficients\(([^)]*)\)\s*\)"
    ),
    "servo_position": re.compile(r"(\w+)\.setPosition\(\s*([-+]?\d*\.?\d+)\s*\)"),
    "set_velocity": re.compile(r"(\w+)\.setVelocity\(\s*(\w+)\s*\)"),
    "hub_logo": re.compile(r"LogoFacingDirection\.(\w+)"),
    "hub_usb": re.compile(r"UsbFacingDirection\.(\w+)"),
    "opmode": re.compile(r"@(TeleOp|Autonomous)\s*\(([^)]*)\)"),
    "constant": re.compile(
        r"(?:final\s+)?(?:double|int|long|float)\s+([A-Z][A-Z0-9_]*)\s*=\s*([-+]?\d*\.?\d+)\s*;"
    ),
}


def _args(text: str) -> dict[str, str]:
    return {k: v for k, v in re.findall(r"(\w+)\s*=\s*\"([^\"]*)\"", text)}


def parse_source(path: Path, display: str | None = None) -> dict[str, Any]:
    """Facts of one Java file; every fact carries 'line' (1-based) and 'ref' (file:line)."""
    name = display or path.name
    facts: dict[str, list[dict[str, Any]]] = {key: [] for key in _PATTERNS}
    text = path.read_text("utf-8")
    for number, line in enumerate(text.splitlines(), start=1):
        code = line.split("//", 1)[0]
        for key, pattern in _PATTERNS.items():
            for m in pattern.finditer(code):
                item: dict[str, Any] = {"line": number, "ref": f"{name}:{number}"}
                if key == "device":
                    item |= {"variable": m.group(1), "class": m.group(2), "name": m.group(3)}
                elif key == "device_mapping":
                    item |= {"variable": m.group(1), "mapping": m.group(2), "name": m.group(3)}
                elif key in ("direction", "zero_power", "run_mode"):
                    item |= {"variable": m.group(1), "value": m.group(2)}
                elif key == "pidf":
                    values = [float(v) for v in m.group(3).split(",")]
                    item |= {"variable": m.group(1), "mode": m.group(2), "pidf": values}
                elif key == "servo_position":
                    item |= {"variable": m.group(1), "position": float(m.group(2))}
                elif key == "set_velocity":
                    item |= {"variable": m.group(1), "argument": m.group(2)}
                elif key in ("hub_logo", "hub_usb"):
                    item |= {"value": m.group(1)}
                elif key == "opmode":
                    item |= {"kind": m.group(1)} | _args(m.group(2))
                elif key == "constant":
                    item |= {"name": m.group(1), "value": float(m.group(2))}
                facts[key].append(item)
    return {"file": name, **facts}


def devices(facts: dict[str, Any]) -> dict[str, dict[str, Any]]:
    """Per hardwareMap name: SDK class and the settings applied to its variable."""
    by_var = {d["variable"]: d for d in facts["device"] + facts["device_mapping"]}
    out: dict[str, dict[str, Any]] = {}
    for var, d in sorted(by_var.items(), key=lambda kv: kv[1]["line"]):
        entry: dict[str, Any] = {
            "sdk_class": d.get("class") or d.get("mapping"),
            "variable": var,
            "ref": d["ref"],
        }
        for key in ("direction", "zero_power", "run_mode"):
            hits = [f for f in facts[key] if f["variable"] == var]
            if hits:
                entry[key] = hits[-1]["value"]
                entry[f"{key}_ref"] = hits[-1]["ref"]
        pidf = [f for f in facts["pidf"] if f["variable"] == var]
        if pidf:
            entry["pidf"] = pidf[-1]["pidf"]
            entry["pidf_ref"] = pidf[-1]["ref"]
        out[d["name"]] = entry
    return out


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawTextHelpFormatter)
    ap.add_argument("source", type=Path)
    ap.add_argument("--out", type=Path)
    args = ap.parse_args(argv)
    files = sorted(args.source.rglob("*.java")) if args.source.is_dir() else [args.source]
    result = []
    for f in files:
        facts = parse_source(f)
        result.append({"file": facts["file"], "devices": devices(facts), "facts": facts})
    text = json.dumps(result, indent=1)
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(text, "utf-8")
    else:
        print(text)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
