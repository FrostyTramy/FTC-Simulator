"""Tessellation, mass properties and geometry signatures for OCP shapes."""

from __future__ import annotations

import hashlib
from dataclasses import dataclass

import numpy as np
from OCP.Bnd import Bnd_Box
from OCP.BRep import BRep_Tool
from OCP.BRepBndLib import BRepBndLib
from OCP.BRepGProp import BRepGProp
from OCP.BRepLib import BRepLib_ToolTriangulatedShape
from OCP.BRepMesh import BRepMesh_IncrementalMesh
from OCP.GProp import GProp_GProps
from OCP.TopAbs import TopAbs_FACE, TopAbs_REVERSED
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


def tessellate(shape: TopoDS_Shape, linear: float, angular: float) -> Mesh:
    BRepMesh_IncrementalMesh(shape, linear, False, angular, True)
    verts: list[np.ndarray] = []
    norms: list[np.ndarray] = []
    tris: list[np.ndarray] = []
    offset = 0
    exp = TopExp_Explorer(shape, TopAbs_FACE)
    while exp.More():
        face = TopoDS.Face_s(exp.Current())
        exp.Next()
        loc = TopLoc_Location()
        tri = BRep_Tool.Triangulation_s(face, loc)
        if tri is None or tri.NbTriangles() == 0:
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
        return Mesh(np.zeros((0, 3), np.float32), np.zeros((0, 3), np.float32), np.zeros((0, 3), np.int32))
    return Mesh(
        np.concatenate(verts).astype(np.float32),
        np.concatenate(norms).astype(np.float32),
        np.concatenate(tris).astype(np.int32),
    )


@dataclass
class MassProps:
    volume: float  # m^3
    area: float  # m^2
    center: tuple[float, float, float]  # volume centroid, shape frame
    bbox_min: tuple[float, float, float]
    bbox_max: tuple[float, float, float]


def mass_props(shape: TopoDS_Shape) -> MassProps:
    vol = GProp_GProps()
    BRepGProp.VolumeProperties_s(shape, vol)
    surf = GProp_GProps()
    BRepGProp.SurfaceProperties_s(shape, surf)
    box = Bnd_Box()
    BRepBndLib.AddOptimal_s(shape, box, False, False)
    c = vol.CentreOfMass() if vol.Mass() > 1e-15 else surf.CentreOfMass()
    xmin, ymin, zmin, xmax, ymax, zmax = box.Get()
    return MassProps(
        volume=abs(vol.Mass()),
        area=surf.Mass(),
        center=(c.X(), c.Y(), c.Z()),
        bbox_min=(xmin, ymin, zmin),
        bbox_max=(xmax, ymax, zmax),
    )


def signature(props: MassProps) -> str:
    """Geometry hash for instancing identical solids defined separately in the CAD
    (volume, area and sorted bounding-box extents, rounded)."""
    ext = sorted(round(b - a, 5) for a, b in zip(props.bbox_min, props.bbox_max, strict=True))
    key = f"{props.volume:.4e}|{props.area:.4e}|{ext}"
    return hashlib.sha1(key.encode()).hexdigest()[:12]
