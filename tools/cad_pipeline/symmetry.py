"""Left/right symmetry of a CAD assembly (spec 02 step 10 "validation report").

Robots are mostly mirror-symmetric about their centre plane. A part whose mirror position is empty is either one-sided
by design (a hub, a motor) or missing: not modelled on that side, dropped by a rule, or lost because its STEP surfaces
do not mesh. This module pairs parts across the plane by world bounding box, so the pipeline can report the unpaired
ones, render a broken part from its healthy twin, and fill configured one-sided parts with a mirrored copy.
"""

from __future__ import annotations

from dataclasses import dataclass

import numpy as np

Box = tuple[np.ndarray, np.ndarray]  # (min, max) world corners, metres


@dataclass(frozen=True)
class Plane:
    """Mirror plane n·x = d in the pipeline's world frame (unit normal)."""

    normal: np.ndarray
    offset: float

    @staticmethod
    def from_config(cfg: dict, up: np.ndarray) -> Plane:
        """`{normal: [x, y, z], point: [x, y, z]}` in the source CAD frame (metres), turned by the up transform."""
        n = up[:3, :3] @ np.asarray(cfg["normal"], dtype=float)
        n /= np.linalg.norm(n)
        p = up[:3, :3] @ np.asarray(cfg.get("point", [0.0, 0.0, 0.0]), dtype=float)
        return Plane(n, float(n @ p))

    def matrix(self) -> np.ndarray:
        """Reflection as a 4x4 matrix (determinant −1): x' = x − 2(n·x − d)n."""
        m = np.eye(4)
        m[:3, :3] -= 2.0 * np.outer(self.normal, self.normal)
        m[:3, 3] = 2.0 * self.offset * self.normal
        return m

    def mirror_box(self, box: Box) -> Box:
        corners = np.array([[x, y, z] for x in (box[0][0], box[1][0]) for y in (box[0][1], box[1][1]) for z in (box[0][2], box[1][2])])
        m = self.matrix()
        pts = corners @ m[:3, :3].T + m[:3, 3]
        return pts.min(0), pts.max(0)


def as_box(world_bbox: tuple[list[float], list[float]]) -> Box:
    return np.asarray(world_bbox[0], dtype=float), np.asarray(world_bbox[1], dtype=float)


def counterparts(boxes: list[Box], plane: Plane, tolerance: float) -> list[int | None]:
    """For every box, the index of the box at its mirror position (itself for a part on the plane), or None.

    Two boxes pair when the mirrored corners of one lie within `tolerance` of the other's corners. Each box pairs at
    most once; candidates are matched nearest first, so two identical parts stacked on one side do not both claim the
    single twin on the other side."""
    n = len(boxes)
    result: list[int | None] = [None] * n
    if n == 0:
        return result
    lo = np.array([b[0] for b in boxes])
    hi = np.array([b[1] for b in boxes])
    candidates: list[tuple[float, int, int]] = []
    for i in range(n):
        mlo, mhi = plane.mirror_box(boxes[i])
        err = np.maximum(np.abs(lo - mlo).max(axis=1), np.abs(hi - mhi).max(axis=1))
        for j in np.nonzero(err <= tolerance)[0]:
            if j >= i:
                candidates.append((float(err[j]), i, int(j)))
    for _, i, j in sorted(candidates):
        if result[i] is None and result[j] is None:
            result[i] = j
            result[j] = i
    return result


def overlap_fraction(box: Box, others: list[Box]) -> float:
    """Share of `box`'s volume covered by the largest single overlap with any of `others` (0 = empty space)."""
    size = np.maximum(box[1] - box[0], 1e-6)
    volume = float(np.prod(size))
    best = 0.0
    for lo, hi in others:
        d = np.minimum(box[1], hi) - np.maximum(box[0], lo)
        if np.all(d > 0):
            best = max(best, float(np.prod(d)) / volume)
    return min(best, 1.0)
