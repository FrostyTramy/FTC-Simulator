"""Collision shapes for CAD parts (spec 02 §8).

Modes per part (chosen in run.py from the config, see config/cad/*.yaml `colliders`):
  sphere   one sphere from the bounding box (game elements)
  box      one oriented box (perimeter rails, glass, flat panels)
  hull     one convex hull (default for structure: tubes, brackets, legs; concavity irrelevant)
  auto     sphere/box/cylinder fit, else hull if nearly convex, else CoACD
  coacd    CoACD convex decomposition (parts whose concave side touches game elements)
  sectors  radial sector hulls around the part's thin axis, centred on its largest hole
           (rings with an exact opening, e.g. FLOWER layers: opening error < 0.5 mm)
Shapes are computed once per distinct part in its own frame (in worker processes) and then
placed per instance with a rigid transform. Output format (row-major 4x4 transforms):
  {"type": "box", "transform": [16], "size": [x, y, z]}
  {"type": "sphere", "center": [3], "radius": r}
  {"type": "cylinder", "transform": [16], "radius": r, "height": h}   # axis = local Y
  {"type": "convex", "points": [[x, y, z], ...]}
"""

from __future__ import annotations

import math
from dataclasses import dataclass, field

import numpy as np
import trimesh

MAX_HULL_POINTS = 96


@dataclass
class ColliderSettings:
    max_fit_error: float = 0.12  # relative volume error allowed for a primitive fit (auto mode)
    convex_ratio: float = 0.92  # part volume / hull volume above this -> single hull (auto mode)
    coacd_threshold: float = 0.08
    coacd_max_hulls: int = 12
    coacd_params: dict = field(
        default_factory=lambda: {
            "mcts_iterations": 60,
            "mcts_nodes": 10,
            "resolution": 800,
            "preprocess_resolution": 30,
        }
    )
    sectors: int = 32


@dataclass
class Job:
    """One collider computation, picklable for worker processes."""

    key: str
    vertices: np.ndarray
    triangles: np.ndarray
    volume: float
    bbox_min: tuple[float, float, float]
    bbox_max: tuple[float, float, float]
    mode: str
    settings: ColliderSettings
    threshold: float | None = None
    max_hulls: int | None = None


@dataclass
class Result:
    key: str
    shapes: list[dict]
    info: dict


def _to_trimesh(vertices: np.ndarray, triangles: np.ndarray) -> trimesh.Trimesh:
    tm = trimesh.Trimesh(np.asarray(vertices, dtype=np.float64), np.asarray(triangles), process=False)
    tm.merge_vertices()
    return tm


def _hull_points(points: np.ndarray) -> list[list[float]]:
    hull = trimesh.convex.convex_hull(np.asarray(points, dtype=np.float64))
    pts = hull.vertices
    if len(pts) > MAX_HULL_POINTS:
        # Keep the most extreme points along a fixed set of directions (deterministic).
        dirs = trimesh.sample.sample_surface_sphere(MAX_HULL_POINTS * 4, seed=1)
        idx = np.unique(np.argmax(pts @ dirs.T, axis=0))[:MAX_HULL_POINTS]
        pts = pts[np.sort(idx)]
    return np.round(pts, 6).tolist()


def _matrix_list(m: np.ndarray) -> list[float]:
    return np.round(m, 7).flatten().tolist()


def _sphere(lo: np.ndarray, hi: np.ndarray) -> dict:
    return {
        "type": "sphere",
        "center": np.round((lo + hi) / 2, 6).tolist(),
        "radius": round(float((hi - lo).mean() / 2), 6),
    }


def _box(tm: trimesh.Trimesh) -> dict:
    obb = tm.bounding_box_oriented
    return {
        "type": "box",
        "transform": _matrix_list(obb.primitive.transform),
        "size": np.round(obb.primitive.extents, 6).tolist(),
    }


def _auto(tm: trimesh.Trimesh, job: Job) -> list[dict] | None:
    """Primitive or single-hull fit; None when the part is too concave for one shape."""
    lo, hi = np.array(job.bbox_min), np.array(job.bbox_max)
    ext = hi - lo
    s = job.settings
    if ext.max() - ext.min() < 0.03 * ext.max():
        sphere_vol = 4 / 3 * math.pi * (ext.mean() / 2) ** 3
        if abs(job.volume - sphere_vol) / sphere_vol < s.max_fit_error:
            return [_sphere(lo, hi)]
    obb_vol = float(np.prod(tm.bounding_box_oriented.primitive.extents))
    if obb_vol > 0 and job.volume / obb_vol > 1 - s.max_fit_error:
        return [_box(tm)]
    try:
        cyl = trimesh.bounds.minimum_cylinder(tm)
        cyl_vol = math.pi * cyl["radius"] ** 2 * cyl["height"]
        if cyl_vol > 0 and job.volume / cyl_vol > 1 - s.max_fit_error:
            # trimesh cylinders run along local Z, Godot cylinders along local Y.
            z_to_y = trimesh.transformations.rotation_matrix(math.pi / 2, [1, 0, 0])
            return [
                {
                    "type": "cylinder",
                    "transform": _matrix_list(cyl["transform"] @ z_to_y),
                    "radius": round(float(cyl["radius"]), 6),
                    "height": round(float(cyl["height"]), 6),
                }
            ]
    except (ValueError, IndexError, np.linalg.LinAlgError):
        pass
    hull_vol = float(tm.convex_hull.volume)
    if hull_vol > 0 and job.volume / hull_vol > s.convex_ratio:
        return [{"type": "convex", "points": _hull_points(tm.vertices)}]
    return None


def _coacd(tm: trimesh.Trimesh, job: Job) -> list[dict]:
    import coacd

    coacd.set_log_level("error")
    parts = coacd.run_coacd(
        coacd.Mesh(tm.vertices, tm.faces),
        threshold=job.threshold or job.settings.coacd_threshold,
        max_convex_hull=job.max_hulls or job.settings.coacd_max_hulls,
        seed=1,
        **job.settings.coacd_params,
    )
    return [{"type": "convex", "points": _hull_points(np.asarray(v))} for v, _ in parts if len(v) >= 4]


def largest_empty_circle(xy: np.ndarray, step: float) -> tuple[np.ndarray, float]:
    """Centre and radius of the largest circle inside the bounding box of xy that contains no
    point of xy (grid search, then local refinement). Used to find the opening of a ring."""
    lo, hi = xy.min(axis=0), xy.max(axis=0)

    def best(gx: np.ndarray, gy: np.ndarray) -> tuple[np.ndarray, float]:
        grid = np.stack(np.meshgrid(gx, gy, indexing="ij"), axis=-1).reshape(-1, 2)
        dmin = np.full(len(grid), np.inf)
        for chunk in np.array_split(xy, max(1, len(xy) // 4000)):
            d = np.linalg.norm(grid[:, None, :] - chunk[None, :, :], axis=2).min(axis=1)
            dmin = np.minimum(dmin, d)
        # A circle must not leave the part's footprint, otherwise "outside" wins.
        margin = np.minimum(
            np.minimum(grid[:, 0] - lo[0], hi[0] - grid[:, 0]),
            np.minimum(grid[:, 1] - lo[1], hi[1] - grid[:, 1]),
        )
        score = np.where(margin >= dmin, dmin, -1.0)
        i = int(np.argmax(score))
        return grid[i], float(score[i])

    c, r = best(np.arange(lo[0], hi[0], step), np.arange(lo[1], hi[1], step))
    fine = step / 8
    c, r = best(np.arange(c[0] - step, c[0] + step, fine), np.arange(c[1] - step, c[1] + step, fine))
    return c, r


def _sectors(tm: trimesh.Trimesh, job: Job) -> tuple[list[dict], dict]:
    obb = tm.bounding_box_oriented
    frame = obb.primitive.transform
    k = int(np.argmin(obb.primitive.extents))
    axis = frame[:3, k]
    u, v = frame[:3, (k + 1) % 3], frame[:3, (k + 2) % 3]
    origin = frame[:3, 3]
    rel = tm.vertices - origin
    xy = np.c_[rel @ u, rel @ v]
    step = max(float(np.ptp(xy, axis=0).max()) / 120, 0.0005)
    c2, radius = largest_empty_circle(xy, step)
    if radius <= 0:
        return [{"type": "convex", "points": _hull_points(tm.vertices)}], {
            "sectors": "no opening found, hull used"
        }
    centre = origin + c2[0] * u + c2[1] * v
    n = job.settings.sectors
    shapes = []
    for i in range(n):
        a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
        n0 = -math.sin(a0) * u + math.cos(a0) * v  # keep the side counter-clockwise of a0
        n1 = math.sin(a1) * u - math.cos(a1) * v  # keep the side clockwise of a1
        piece = tm.slice_plane(centre, n0)
        if piece is None or len(piece.vertices) == 0:
            continue
        piece = piece.slice_plane(centre, n1)
        if piece is None or len(piece.vertices) < 4:
            continue
        pts = piece.vertices
        if np.linalg.matrix_rank(pts - pts.mean(axis=0), tol=1e-6) < 3:
            continue
        shapes.append({"type": "convex", "points": _hull_points(pts)})
    chord_error = radius * (1 - math.cos(math.pi / n))
    info = {
        "opening_diameter_m": round(2 * radius, 5),
        "opening_centre": np.round(centre, 6).tolist(),
        "axis": np.round(axis, 6).tolist(),
        "max_chord_error_m": round(chord_error, 6),
        "pieces": len(shapes),
    }
    return shapes, info


def intrusion(tm: trimesh.Trimesh, shapes: list[dict], samples: int = 4000) -> float:
    """99th percentile distance (m) from collider hull surfaces to the part surface: how far the
    collider bridges concave regions (0 = collider follows the surface)."""
    from scipy.spatial import cKDTree

    hulls = [trimesh.convex.convex_hull(np.array(s["points"])) for s in shapes if s["type"] == "convex"]
    if not hulls or tm.area <= 0:
        return 0.0
    ref, _ = trimesh.sample.sample_surface(tm, 30000, seed=1)
    tree = cKDTree(ref)
    pts = np.concatenate(
        [trimesh.sample.sample_surface(h, max(50, samples // len(hulls)), seed=1)[0] for h in hulls]
    )
    d, _ = tree.query(pts)
    return float(np.percentile(d, 99))


def fit_job(job: Job) -> Result:
    """Worker entry point: collider shapes for one part in its own frame."""
    if len(job.triangles) == 0:
        return Result(job.key, [], {"mode": job.mode, "empty": True})
    lo, hi = np.array(job.bbox_min), np.array(job.bbox_max)
    if job.mode == "sphere":
        return Result(job.key, [_sphere(lo, hi)], {"mode": "sphere"})
    tm = _to_trimesh(job.vertices, job.triangles)
    info: dict = {"mode": job.mode}
    if job.mode == "box":
        shapes = [_box(tm)]
    elif job.mode == "hull":
        shapes = [{"type": "convex", "points": _hull_points(tm.vertices)}]
    elif job.mode == "sectors":
        shapes, extra = _sectors(tm, job)
        info.update(extra)
    elif job.mode == "coacd":
        shapes = _coacd(tm, job)
    elif job.mode == "auto":
        shapes = _auto(tm, job)
        if shapes is None:
            shapes = _coacd(tm, job)
            info["mode"] = "auto->coacd"
    else:
        raise ValueError(f"unknown collider mode {job.mode}")
    if job.mode in ("coacd", "sectors") or info["mode"] == "auto->coacd":
        info["hulls"] = len(shapes)
        info["intrusion_p99_m"] = round(intrusion(tm, shapes), 5)
    return Result(job.key, shapes, info)


def place(shapes: list[dict], matrix: np.ndarray) -> list[dict]:
    """Apply a rigid instance transform to collider shapes."""
    out = []
    rot, trans = matrix[:3, :3], matrix[:3, 3]
    for s in shapes:
        s = dict(s)
        if s["type"] == "sphere":
            s["center"] = np.round(rot @ np.array(s["center"]) + trans, 6).tolist()
        elif s["type"] in ("box", "cylinder"):
            s["transform"] = _matrix_list(matrix @ np.array(s["transform"]).reshape(4, 4))
        elif s["type"] == "convex":
            s["points"] = np.round(np.array(s["points"]) @ rot.T + trans, 6).tolist()
        out.append(s)
    return out
