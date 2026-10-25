"""Phase 9 vision acceptance (docs/open_questions.md Q-011): runs the app with the sensor robot at given poses, the
unchanged SDK sample ConceptAprilTag and a capture, then compares every reported cluster XYZ with the ground truth the
app prints (VisionCheck). Pass: error <= 1 in + 2 % of the range (proposed threshold, Q-011).

Usage: python tools/checks/vision_acceptance.py [--pose X,Z,YAW_DEG ...]
Writes reports/vision/<n>.png, <n>_cam0.png, <n>.log and prints one line per detected cluster.
"""

from __future__ import annotations

import argparse
import math
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GODOT = ROOT / "Godot_v4.7.2-stable_mono_win64" / "Godot_v4.7.2-stable_mono_win64_console.exe"
JAR = ROOT / "generated" / "build" / "sdk_samples" / "libs" / "teamcode.jar"
DEFAULT_POSES = ["-0.3236,1.40,0", "-0.45,1.55,-12", "0.3241,-1.40,180", "0.40,-1.25,170"]
XYZ = re.compile(r"XYZ\s+(-?[\d.]+)\s+(-?[\d.]+)\s+(-?[\d.]+)")


def run(pose: str, index: int) -> list[tuple[str, float, float]]:
    out = ROOT / "reports" / "vision"
    out.mkdir(parents=True, exist_ok=True)
    png = out / f"pose{index}.png"
    cmd = [str(GODOT), "--path", str(ROOT / "godot"), "--", "--robot", "gobilda_starter_sensors", "--robot-pose", pose,
           "--teamcode", str(JAR), "--opmode", "Concept: AprilTag", "--capture", str(png), "--capture-seconds", "8"]
    log = subprocess.run(cmd, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=1800).stdout
    (out / f"pose{index}.log").write_text(log, encoding="utf-8")
    truth = {}
    for line in log.splitlines():
        if line.startswith("groundtruth "):
            name = line[len("groundtruth "):line.index(" XYZ")]
            truth[name] = tuple(float(v) for v in XYZ.search(line).groups())
    results = []
    cluster = None
    for line in log.splitlines():
        if line.startswith("telemetry:") and "Tag Cluster (" in line:
            cluster = line[line.index("(") + 1:line.rindex(")")]
        elif line.startswith("telemetry: XYZ") and cluster:
            seen = tuple(float(v) for v in XYZ.search(line).groups())
            t = truth.get(cluster)
            if t:
                err = math.dist(seen, t)
                rng = math.hypot(t[0], t[1])
                results.append((cluster, err, 1.0 + 0.02 * rng))
            cluster = None
    return results


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--pose", action="append")
    args = parser.parse_args()
    failed = 0
    detected = 0
    for i, pose in enumerate(args.pose or DEFAULT_POSES):
        for cluster, err, limit in run(pose, i):
            detected += 1
            ok = err <= limit
            failed += 0 if ok else 1
            print(f"pose {pose}: {cluster} error {err:.2f} in (limit {limit:.2f}) {'PASS' if ok else 'FAIL'}")
    if detected == 0:
        print("no cluster detected")
        sys.exit(1)
    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
