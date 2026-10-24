"""Automatic detection proposals (spec 02 "Automatic detection"). Written to detected.yaml for
review; nothing here is applied to the mechanism or robot configs automatically."""

from __future__ import annotations

import collections
import re
from typing import Any

import numpy as np

INCH = 0.0254

# goBILDA part-number suffix -> gearbox ratio (goBILDA naming of 5202/5203/5204 Yellow Jacket motors).
GOBILDA_RATIOS = {
    "0001": 1.0,
    "0003": 3.7,
    "0005": 5.2,
    "0014": 13.7,
    "0019": 19.2,
    "0027": 26.9,
    "0051": 50.9,
    "0071": 71.2,
    "0099": 99.5,
    "0139": 139.0,
    "0188": 188.0,
}
# Mecanum wheel handedness from the StarterBot BOM (instructions p2-4).
MECANUM_SLANT = {"3625-0100-0104": "left", "3625-0001-0104": "right"}
WHEEL_PATTERNS = [
    ("mecanum", re.compile(r"3625-0\d{3}-\d{4}")),
    ("gecko", re.compile(r"3613-\d{4}-\d{4}|[Gg]ecko")),
    ("hogback", re.compile(r"3626-\d{4}-\d{4}")),
    ("intake_roller", re.compile(r"3618-\d{4}-\d{4}")),
    ("omni", re.compile(r"[Oo]mni")),
]
MOTOR_RE = re.compile(r"(520[1-4])-\d{4}-(\d{4})")
SERVO_RE = re.compile(r"2000-0025-(\d{4})")


def _assemblies(instances, pattern: re.Pattern) -> dict[str, list]:
    """Group part instances by the assembly path prefix whose segment matches pattern."""
    groups: dict[str, list] = collections.defaultdict(list)
    for inst in instances:
        segments = inst.path.split("/")
        for k, seg in enumerate(segments):
            if pattern.search(seg):
                groups["/".join(segments[: k + 1])].append(inst)
                break
    return groups


def _union_bbox(parts) -> tuple[np.ndarray, np.ndarray]:
    lo = np.min([p.world_bbox[0] for p in parts], axis=0)
    hi = np.max([p.world_bbox[1] for p in parts], axis=0)
    return lo, hi


def _r(v) -> list[float]:
    return [round(float(x), 4) for x in v]


def detect_wheels(instances) -> list[dict]:
    wheels = []
    for kind, pattern in WHEEL_PATTERNS:
        for path, parts in sorted(_assemblies(instances, pattern).items()):
            lo, hi = _union_bbox(parts)
            ext = hi - lo
            axis_idx = int(np.argmin(ext))
            axis = np.zeros(3)
            axis[axis_idx] = 1.0
            radius = float(np.max(np.delete(ext, axis_idx)) / 2)
            entry = {
                "type": kind,
                "path": path,
                "center": _r((lo + hi) / 2),
                "axis": _r(axis),
                "radius_m": round(radius, 4),
                "width_m": round(float(ext[axis_idx]), 4),
                "confidence": 0.9 if kind != "omni" else 0.6,
                "evidence": f"part number in assembly name; axis = thinnest bbox direction; {len(parts)} parts",
            }
            if kind == "mecanum":
                number = pattern.search(path.split("/")[-1]).group(0)
                entry["slant"] = MECANUM_SLANT.get(number, "unknown")
                entry["evidence"] += "; slant from part number (BOM p2-4)"
            wheels.append(entry)
    return wheels


def detect_motors(instances) -> list[dict]:
    motors = []
    for path, parts in sorted(_assemblies(instances, MOTOR_RE).items()):
        series, suffix = MOTOR_RE.search(path.split("/")[-1]).groups()
        lo, hi = _union_bbox(parts)
        ext = hi - lo
        motors.append(
            {
                "path": path,
                "series": series,
                "gear_ratio_label": GOBILDA_RATIOS.get(suffix),
                "center": _r((lo + hi) / 2),
                "shaft_axis": _r(np.eye(3)[int(np.argmax(ext))]),
                "confidence": 0.85,
                "evidence": "goBILDA part number; ratio from part-number suffix; axis = longest bbox side",
            }
        )
    return motors


def detect_servos(instances) -> list[dict]:
    servos = []
    for path, parts in sorted(_assemblies(instances, SERVO_RE).items()):
        variant = SERVO_RE.search(path.split("/")[-1]).group(1)
        lo, hi = _union_bbox(parts)
        servos.append(
            {
                "path": path,
                "variant": {"0002": "torque", "0003": "speed"}.get(variant, variant),
                "center": _r((lo + hi) / 2),
                "confidence": 0.85,
                "evidence": "goBILDA 2000 series part number",
            }
        )
    return servos


def detect_elements(instances, rules: dict) -> list[dict]:
    known = {
        "pollen": rules["elements"]["pollen_diameter"]["value"] * INCH,
        "nectar": rules["elements"]["nectar_diameter"]["value"] * INCH,
    }
    by_sig: dict[str, list] = collections.defaultdict(list)
    for inst in instances:
        ext = np.array(inst.world_bbox[1]) - np.array(inst.world_bbox[0])
        if ext.max() > 0 and (ext.max() - ext.min()) < 0.04 * ext.max():
            by_sig[inst.sig].append(inst)
    found = []
    for parts in by_sig.values():
        if len(parts) < 4:
            continue
        ext = np.array(parts[0].world_bbox[1]) - np.array(parts[0].world_bbox[0])
        diameter = float(ext.mean())
        name, size = min(known.items(), key=lambda kv: abs(kv[1] - diameter))
        error = abs(size - diameter)
        found.append(
            {
                "name": parts[0].name,
                "count": len(parts),
                "diameter_m": round(diameter, 4),
                "matches_manual": name if error < INCH else None,
                "error_m": round(error, 4),
                "confidence": 0.95 if error < 0.25 * INCH else 0.6,
                "evidence": "repeated near-spherical solid with identical geometry signature",
            }
        )
    return sorted(found, key=lambda e: -e["count"])


def detect_pivots(instances) -> list[dict]:
    """Moving-group pivots from coaxial bearings (F608ZZ etc.) inside rigid groups."""
    pivots = []
    groups = collections.defaultdict(list)
    for inst in instances:
        if re.search(r"(?i)bearing", inst.name) and inst.group not in ("static",):
            groups[inst.group].append(inst)
    for group, parts in sorted(groups.items()):
        centers = np.array([(np.array(p.world_bbox[0]) + np.array(p.world_bbox[1])) / 2 for p in parts])
        mean = centers.mean(axis=0)
        if len(centers) >= 2:
            _, _, vt = np.linalg.svd(centers - mean)
            axis = vt[0]
        else:
            axis = np.array([0.0, 0.0, 1.0])
        axis = axis * np.sign(axis[np.argmax(np.abs(axis))])
        pivots.append(
            {
                "group": group,
                "point": _r(mean),
                "axis": _r(axis),
                "bearings": len(parts),
                "spread_m": round(float(np.ptp(centers @ axis)), 4),
                "confidence": 0.8,
                "evidence": "principal axis through bearing centres of the group",
            }
        )
    dampers = collections.defaultdict(list)
    for inst in instances:
        if re.search(r"(?i)damper", inst.name) and not re.search(r"(?i)holder", inst.name):
            dampers[inst.group].append(_r((np.array(inst.world_bbox[0]) + np.array(inst.world_bbox[1])) / 2))
    for p in pivots:
        p["dampers"] = dampers.get(p["group"], [])
    return pivots


def detect(instances, rules: dict) -> dict[str, Any]:
    return {
        "elements": detect_elements(instances, rules),
        "pivots": detect_pivots(instances),
        "wheels": detect_wheels(instances),
        "motors": detect_motors(instances),
        "servos": detect_servos(instances),
    }


def guess_up_axis(raw) -> str:
    """Up axis of a robot CAD: normal of the plane through the mecanum wheel centres, pointing to
    the side where most of the robot is. Returns a key of run.UP_TRANSFORMS."""
    wheel_re = WHEEL_PATTERNS[0][1]
    centres = collections.defaultdict(list)
    for node, world, _ in raw:
        segs = node.path.split("/")
        for k, seg in enumerate(segs):
            if wheel_re.search(seg):
                centres["/".join(segs[: k + 1])].append(world[:3, 3])
                break
    if len(centres) < 3:
        return "Y"
    pts = np.array([np.mean(v, axis=0) for v in centres.values()])
    _, _, vt = np.linalg.svd(pts - pts.mean(axis=0))
    normal = vt[2]
    body = np.mean([w[:3, 3] for _, w, _ in raw], axis=0)
    if (body - pts.mean(axis=0)) @ normal < 0:
        normal = -normal
    idx = int(np.argmax(np.abs(normal)))
    sign = "" if normal[idx] > 0 else "-"
    return f"{sign}{'XYZ'[idx]}"
