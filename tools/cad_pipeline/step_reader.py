"""Read a STEP file with its assembly structure (OCP XCAF) into plain Python objects.

Result: a list of prototype shapes (one per distinct CAD part definition) and a tree of nodes
(assemblies and part instances) with local transforms in metres. Lengths are converted by OCP
itself (document length unit = metre), whatever unit the file declares.
"""

from __future__ import annotations

import hashlib
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np
from OCP.gp import gp_Trsf
from OCP.IFSelect import IFSelect_RetDone
from OCP.Quantity import Quantity_Color, Quantity_TOC_sRGB
from OCP.STEPCAFControl import STEPCAFControl_Reader
from OCP.TCollection import TCollection_ExtendedString
from OCP.TDataStd import TDataStd_Name
from OCP.TDF import TDF_Label, TDF_LabelSequence
from OCP.TDocStd import TDocStd_Document
from OCP.TopoDS import TopoDS_Shape
from OCP.XCAFDoc import XCAFDoc_ColorType, XCAFDoc_DocumentTool


@dataclass
class Prototype:
    """A CAD part definition; instanced by one or more nodes."""

    key: str  # stable id: hash of name + entry
    name: str
    shape: TopoDS_Shape
    color: tuple[float, float, float] | None


@dataclass
class Node:
    name: str
    path: str  # "/"-joined names from the root, made unique with #n suffixes
    local: np.ndarray  # 4x4 transform relative to the parent, metres
    prototype: str | None = None  # Prototype.key for parts, None for assemblies
    children: list[Node] = field(default_factory=list)
    color: tuple[float, float, float] | None = None  # instance colour override

    def walk(self, parent_world: np.ndarray | None = None):
        """Yield (node, world transform) depth first."""
        world = self.local if parent_world is None else parent_world @ self.local
        yield self, world
        for child in self.children:
            yield from child.walk(world)


def trsf_to_matrix(trsf: gp_Trsf) -> np.ndarray:
    m = np.eye(4)
    for r in range(3):
        for c in range(4):
            m[r, c] = trsf.Value(r + 1, c + 1)
    return m


def _label_name(label: TDF_Label) -> str:
    attr = TDataStd_Name()
    if label.FindAttribute(TDataStd_Name.GetID_s(), attr):
        return attr.Get().ToExtString()
    return ""


class StepReader:
    def __init__(self, path: Path):
        self.doc = TDocStd_Document(TCollection_ExtendedString("XmlOcaf"))
        # OCCT >= 7.6 converts STEP lengths into the document's unit; 1.0 = metres.
        XCAFDoc_DocumentTool.SetLengthUnit_s(self.doc, 1.0)
        reader = STEPCAFControl_Reader()
        reader.SetNameMode(True)
        reader.SetColorMode(True)
        reader.SetLayerMode(False)
        if reader.ReadFile(str(path)) != IFSelect_RetDone:
            raise RuntimeError(f"cannot read STEP file {path}")
        if not reader.Transfer(self.doc):
            raise RuntimeError(f"STEP transfer failed for {path}")
        self.shape_tool = XCAFDoc_DocumentTool.ShapeTool_s(self.doc.Main())
        self.color_tool = XCAFDoc_DocumentTool.ColorTool_s(self.doc.Main())
        self.prototypes: dict[str, Prototype] = {}

    def _color(self, label: TDF_Label) -> tuple[float, float, float] | None:
        color = Quantity_Color()
        for kind in (XCAFDoc_ColorType.XCAFDoc_ColorSurf, XCAFDoc_ColorType.XCAFDoc_ColorGen):
            if self.color_tool.GetColor_s(label, kind, color):
                r, g, b = color.Values(Quantity_TOC_sRGB)
                return (round(r, 4), round(g, 4), round(b, 4))
        return None

    def _prototype(self, label: TDF_Label) -> str:
        entry = label.EntryDumpToString() if hasattr(label, "EntryDumpToString") else str(label.Tag())
        name = _label_name(label)
        key = hashlib.sha1(f"{name}|{entry}".encode()).hexdigest()[:12]
        if key not in self.prototypes:
            shape = self.shape_tool.GetShape_s(label)
            self.prototypes[key] = Prototype(key, name, shape, self._color(label))
        return key

    def _build(self, label: TDF_Label, name: str, path: str, local: np.ndarray, color) -> Node:
        node = Node(name=name, path=path, local=local, color=color)
        if self.shape_tool.IsAssembly_s(label):
            comps = TDF_LabelSequence()
            self.shape_tool.GetComponents_s(label, comps, False)
            used: dict[str, int] = {}
            for i in range(1, comps.Length() + 1):
                comp = comps.Value(i)
                ref = TDF_Label()
                self.shape_tool.GetReferredShape_s(comp, ref)
                child_name = _label_name(ref) or _label_name(comp) or "unnamed"
                n = used.get(child_name, 0)
                used[child_name] = n + 1
                child_path = f"{path}/{child_name}#{n}"
                loc = trsf_to_matrix(self.shape_tool.GetLocation_s(comp).Transformation())
                node.children.append(self._build(ref, child_name, child_path, loc, self._color(comp)))
        else:
            node.prototype = self._prototype(label)
        return node

    def roots(self) -> list[Node]:
        labels = TDF_LabelSequence()
        self.shape_tool.GetFreeShapes(labels)
        result = []
        for i in range(1, labels.Length() + 1):
            label = labels.Value(i)
            name = _label_name(label) or f"root{i}"
            result.append(self._build(label, name, name, np.eye(4), self._color(label)))
        return result
