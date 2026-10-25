"""Copies FTC SDK sample OpModes into a TeamCode tree the way a team does (spec 12 compatibility tests).

The FtcRobotController samples say: "Use Android Studio to Copy this Class, and Paste it into your team's code folder
with a new name. Remove or comment out the @Disabled line to add this OpMode to the Driver Station OpMode list." This
tool does exactly that and nothing else: the package line becomes org.firstinspires.ftc.teamcode and every line that
is only the @Disabled annotation is commented out. Everything else stays byte for byte (BSD-3 headers included).

Usage: python tools/compat/import_sdk_samples.py [--samples DIR] [--out DIR] NAME...
Default samples folder: generated/sdk/12.0.0/samples (FtcRobotController v12.0, fetched by scripts/setup_tools.ps1).
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_SAMPLES = REPO_ROOT / "generated" / "sdk" / "12.0.0" / "samples"
DEFAULT_OUT = REPO_ROOT / "tests" / "compat" / "sdk_samples" / "TeamCode" / "src" / "main" / "java"
PACKAGE_RE = re.compile(r"^package[ \t]+org\.firstinspires\.ftc\.robotcontroller\.external\.samples;", re.M)
DISABLED_RE = re.compile(r"^([ \t]*)@Disabled(?=[ \t]*\r?$)", re.M)


def convert(source: str) -> str:
    if not PACKAGE_RE.search(source):
        raise ValueError("not an SDK sample (package line not found)")
    text = PACKAGE_RE.sub("package org.firstinspires.ftc.teamcode;", source, count=1)
    return DISABLED_RE.sub(lambda m: m.group(1) + "//@Disabled", text)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--samples", type=Path, default=DEFAULT_SAMPLES)
    parser.add_argument("--out", type=Path, default=DEFAULT_OUT)
    parser.add_argument("names", nargs="+")
    args = parser.parse_args()
    target = args.out / "org" / "firstinspires" / "ftc" / "teamcode"
    target.mkdir(parents=True, exist_ok=True)
    for name in args.names:
        src = args.samples / f"{name}.java"
        text = src.read_bytes().decode("utf-8")
        (target / f"{name}.java").write_bytes(convert(text).encode("utf-8"))
        print(f"{name}: imported")


if __name__ == "__main__":
    main()
