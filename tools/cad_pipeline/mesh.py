"""Tessellation, mass properties and geometry signatures for OCP shapes."""

from __future__ import annotations

import hashlib
from dataclasses import dataclass

import numpy as np
from OCP.Bnd import Bnd_Box
from OCP.BRep import BRep_Tool
from OCP.BRepAdaptor import BRepAdaptor_Curve
from OCP.BRepBndLib import BRepBndLib
from OCP.BRepGProp import BRepGProp
from OCP.BRepLib import BRepLib_ToolTriangulatedShape
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.BRepTools import BRepTools
from OCP.GProp import GProp_GProps
from OCP.ShapeFix import ShapeFix_Shape
from OCP.TopAbs import TopAbs_EDGE, TopAbs_FACE, TopAbs_REVERSED
from OCP.TopExp import TopExp_Explorer
from OCP.TopLoc import TopLoc_Location
from OCP.TopoDS import TopoDS, TopoDS_Shape


@dataclass
class Mesh:
    """Triangle mesh in the shape's own frame. Vertices are not shared between CAD faces, so
    normals stay crisp across CAD edges and smooth inside curved faces."""

    vertices: np.ndarray  # (n, 3) float32, metres
    normals: np.ndarray  # (n, 3) float32
    triangles: np.ndarray  # (m, 3) int32

    @property
    def triangle_count(self) -> int:
        return len(self.triangles)

    def transformed(self, matrix: np.ndarray) -> Mesh:
        rot = matrix[:3, :3]
        v = self.vertices @ rot.T + matrix[:3, 3]
        n = self.normals @ np.linalg.inv(rot).T  # normal matrix for non-orthonormal transforms
        n /= np.maximum(np.linalg.norm(n, axis=1, keepdims=True), 1e-12)
        tris = self.triangles if np.linalg.det(rot) > 0 else self.triangles[:, ::-1]
        return Mesh(v.astype(np.float32), n.astype(np.float32), tris)

    @staticmethod
    def concat(meshes: list[Mesh]) -> Mesh:
        offsets = np.cumsum([0] + [len(m.vertices) for m in meshes[:-1]])
        return Mesh(
            np.concatenate([m.vertices for m in meshes]),
            np.concatenate([m.normals for m in meshes]),
            np.concatenate([m.triangles + o for m, o in zip(meshes, offsets, strict=True)]).astype(np.int32),
        )


@dataclass
class Tessellation:
    """A part's render mesh and how well the B-rep meshed: faces OCP could not triangulate leave holes (or, when no
    face meshes, an invisible part), so they are counted, repaired where possible and reported."""

    mesh: Mesh
    faces: int
    missing_faces: int
    repaired: bool = False


def tessellate_checked(shape: TopoDS_Shape, linear: float, angular: float) -> Tessellation:
    """Tessellate; when faces stay untriangulated (broken STEP surfaces), heal the shape (ShapeFix) and mesh it again,
    keeping whichever result covers more faces."""
    mesh, faces, missing = _tessellate(shape, linear, angular)
    if missing == 0:
        return Tessellation(mesh, faces, 0)
    fixer = ShapeFix_Shape(shape)
    fixer.Perform()
    healed = fixer.Shape()
    BRepTools.Clean_s(healed)
    mesh2, faces2, missing2 = _tessellate(healed, linear, angular)
    if mesh2.triangle_count > mesh.triangle_count and missing2 <= missing:
        return Tessellation(mesh2, faces2, missing2, repaired=True)
    return Tessellation(mesh, faces, missing)


def tessellate(shape: TopoDS_Shape, linear: float, angular: float) -> Mesh:
    return _tessellate(shape, linear, angular)[0]


def edge_points(shape: TopoDS_Shape, per_edge: int = 16) -> np.ndarray:
    """Points sampled along every B-rep edge (shape frame): a fallback outline for parts whose faces do not mesh."""
    pts: list[tuple[float, float, float]] = []
    exp = TopExp_Explorer(shape, TopAbs_EDGE)
    while exp.More():
        edge = TopoDS.Edge_s(exp.Current())
        exp.Next()
        if BRep_Tool.Degenerated_s(edge):
            continue
        curve = BRepAdaptor_Curve(edge)
        a, b = curve.FirstParameter(), curve.LastParameter()
        for k in range(per_edge + 1):
            p = curve.Value(a + (b - a) * k / per_edge)
            pts.append((p.X(), p.Y(), p.Z()))
    return np.array(pts, dtype=np.float64).reshape(-1, 3)


def _tessellate(shape: TopoDS_Shape, linear: float, angular: float) -> tuple[Mesh, int, int]:
    """(mesh, faces, faces without triangulation)."""
    BRepMesh_IncrementalMesh(shape, linear, False, angular, True)
    faces = 0
    missing = 0
    verts: list[np.ndarray] = []
    norms: list[np.ndarray] = []
    tris: list[np.ndarray] = []
    offset = 0
    exp = TopExp_Explorer(shape, TopAbs_FACE)
    while exp.More():
        face = TopoDS.Face_s(exp.Current())
        exp.Next()
        faces += 1
        loc = TopLoc_Location()
        tri = BRep_Tool.Triangulation_s(face, loc)
        if tri is None or tri.NbTriangles() == 0:
            missing += 1
            continue
        if not tri.HasNormals():
            BRepLib_ToolTriangulatedShape.ComputeNormals_s(face, tri)
        trsf = loc.Transformation()
        n_nodes = tri.NbNodes()
        v = np.empty((n_nodes, 3))
        n = np.empty((n_nodes, 3))
        for i in range(1, n_nodes + 1):
            p = tri.Node(i).Transformed(trsf)
            v[i - 1] = (p.X(), p.Y(), p.Z())
            try:
                d = tri.Normal(i).Transformed(trsf)
                n[i - 1] = (d.X(), d.Y(), d.Z())
            except Exception:  # degenerate surface point (zero-length normal): rebuilt from the triangles below
                n[i - 1] = np.nan
        t = np.empty((tri.NbTriangles(), 3), dtype=np.int32)
        for i in range(1, tri.NbTriangles() + 1):
            a, b, c = tri.Triangle(i).Get()
            t[i - 1] = (a - 1, b - 1, c - 1)
        bad = np.isnan(n).any(axis=1)
        if bad.any():
            face_n = np.cross(v[t[:, 1]] - v[t[:, 0]], v[t[:, 2]] - v[t[:, 0]])
            acc = np.zeros_like(v)
            for k in range(3):
                np.add.at(acc, t[:, k], face_n)
            length = np.linalg.norm(acc, axis=1, keepdims=True)
            fixed = np.where(length > 0, acc / np.maximum(length, 1e-30), [0.0, 0.0, 1.0])
            n[bad] = fixed[bad]
        if face.Orientation() == TopAbs_REVERSED:
            t = t[:, ::-1]
            n = -n
        verts.append(v)
        norms.append(n)
        tris.append(t + offset)
        offset += n_nodes
    if not verts:
        return Mesh(np.zeros((0, 3), np.float32), np.zeros((0, 3), np.float32), np.zeros((0, 3), np.int32)), faces, missing
    mesh = Mesh(
        np.concatenate(verts).astype(np.float32),
        np.concatenate(norms).astype(np.float32),
        np.concatenate(tris).astype(np.int32),
    )
    return mesh, faces, missing


@dataclass
class MassProps:
    volume: float  # m^3
    area: float  # m^2
    center: tuple[float, float, float]  # volume centroid, shape frame
    bbox_min: tuple[float, float, float]
    bbox_max: tuple[float, float, float]
    # Volume inertia about the centroid, shape frame (m^5): Ixx, Iyy, Izz, Ixy, Ixz, Iyz. The products change sign
    # under a mirror, so they tell a mirrored copy from the original (signature()).
    inertia: tuple[float, float, float, float, float, float] = (0.0, 0.0, 0.0, 0.0, 0.0, 0.0)


def mass_props(shape: TopoDS_Shape) -> MassProps:
    vol = GProp_GProps()
    BRepGProp.VolumeProperties_s(shape, vol)
    surf = GProp_GProps()
    BRepGProp.SurfaceProperties_s(shape, surf)
    box = Bnd_Box()
    BRepBndLib.AddOptimal_s(shape, box, False, False)
    c = vol.CentreOfMass() if vol.Mass() > 1e-15 else surf.CentreOfMass()
    xmin, ymin, zmin, xmax, ymax, zmax = box.Get()
    inertia = (0.0,) * 6
    if vol.Mass() > 1e-15:
        m = vol.MatrixOfInertia()
        inertia = tuple(float(m.Value(r, c)) for r, c in ((1, 1), (2, 2), (3, 3), (1, 2), (1, 3), (2, 3)))
    return MassProps(
        volume=abs(vol.Mass()),
        area=surf.Mass(),
        center=(c.X(), c.Y(), c.Z()),
        bbox_min=(xmin, ymin, zmin),
        bbox_max=(xmax, ymax, zmax),
        inertia=inertia,
    )


def signature(props: MassProps) -> str:
    """Geometry hash for instancing identical solids defined separately in the CAD: volume, area, the bounding box and
    centroid in the solid's own frame (rounded) and the inertia tensor about the centroid. The tessellation is shared
    in that frame, so solids only match when their local geometry coincides. A mirrored copy has the same volume, area
    and extents, and when it is centred in its frame also the same box and centroid; its products of inertia have the
    opposite sign, so it keeps its own mesh instead of being drawn as its twin."""
    box = [round(v, 5) + 0.0 for v in (*props.bbox_min, *props.bbox_max, *props.center)]
    # Relative to the largest moment, so the rounding does not depend on the part's size.
    scale = max((abs(v) for v in props.inertia[:3]), default=0.0) or 1.0
    inertia = [round(v / scale, 3) + 0.0 for v in props.inertia]
    key = f"{props.volume:.4e}|{props.area:.4e}|{box}|{inertia}"
    return hashlib.sha1(key.encode()).hexdigest()[:12]
