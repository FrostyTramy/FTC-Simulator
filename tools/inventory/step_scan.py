"""Streaming summary of a STEP (ISO 10303-21) file using only the standard library.

Reads the assembly structure (PRODUCT -> PRODUCT_DEFINITION_FORMATION -> PRODUCT_DEFINITION,
linked by NEXT_ASSEMBLY_USAGE_OCCURRENCE), the length unit of every geometric context, colours
and the header. It never loads geometry, so a 400 MB file costs only time, not memory.
"""

from __future__ import annotations

import collections
import re
from collections.abc import Iterable
from typing import Any

_ENTITY_RE = re.compile(r"^#(\d+)\s*=\s*([A-Z0-9_]+)\s*\((.*)\)\s*;$", re.S)
_COMPLEX_RE = re.compile(r"^#(\d+)\s*=\s*\((.*)\)\s*;$", re.S)
_STR_RE = re.compile(r"'((?:[^']|'')*)'")
_REF_RE = re.compile(r"#(\d+)")
_X2_RE = re.compile(r"\\X2\\([0-9A-F]+)\\X0\\")
_NUM_RE = re.compile(r"[-+]?(?:\d+\.\d*|\.\d+|\d+)(?:[Ee][-+]?\d+)?")
_PREFIX_RE = re.compile(r"SI_UNIT\s*\(\s*(\.[A-Z]+\.|\$)\s*,\s*\.([A-Z]+)\.\s*\)")

_SHAPE_REPS = {
    "SHAPE_REPRESENTATION",
    "ADVANCED_BREP_SHAPE_REPRESENTATION",
    "MANIFOLD_SURFACE_SHAPE_REPRESENTATION",
    "GEOMETRICALLY_BOUNDED_WIREFRAME_SHAPE_REPRESENTATION",
    "FACETED_BREP_SHAPE_REPRESENTATION",
}
_PRODUCT_DEFS = {"PRODUCT_DEFINITION", "PRODUCT_DEFINITION_WITH_ASSOCIATED_DOCUMENTS"}
_SI_PREFIX_TO_METRES = {"$": 1.0, ".MILLI.": 1e-3, ".CENTI.": 1e-2, ".KILO.": 1e3}


def decode_string(s: str) -> str:
    """Decode a Part 21 string literal body ('' escapes and \\X2\\ UTF-16 hex runs)."""
    s = s.replace("''", "'")
    return _X2_RE.sub(
        lambda m: "".join(chr(int(m.group(1)[i : i + 4], 16)) for i in range(0, len(m.group(1)), 4)),
        s,
    )


def _statements(lines: Iterable[str]) -> Iterable[tuple[str, str]]:
    """Yield (section, statement) pairs. Part 21 line breaks are insignificant, even in strings."""
    section = ""
    buf: list[str] = []
    for line in lines:
        s = line.strip()
        if not s:
            continue
        if not buf and s in ("HEADER;", "DATA;"):
            section = s[:-1]
            continue
        buf.append(s)
        if not s.endswith(";"):
            continue
        yield section, "".join(buf)
        buf = []


def unit_to_metres(unit_body: str) -> float | None:
    """Metres per unit for an SI or conversion-based length unit body, None if unknown."""
    m = _PREFIX_RE.search(unit_body)
    if m and m.group(2) == "METRE":
        return _SI_PREFIX_TO_METRES.get(m.group(1))
    if "CONVERSION_BASED_UNIT" in unit_body:
        name = _STR_RE.search(unit_body)
        if name and decode_string(name.group(1)).upper() in ("INCH", "IN"):
            return 0.0254
    return None


def scan(lines: Iterable[str]) -> dict[str, Any]:
    """Summarize a STEP file given an iterable of its text lines."""
    header: list[str] = []
    n_statements = 0
    entity_counts: collections.Counter[str] = collections.Counter()
    product: dict[int, str] = {}
    pdf_to_product: dict[int, int] = {}
    pd_to_pdf: dict[int, int] = {}
    nauo: list[tuple[int, int]] = []
    length_units: dict[int, str] = {}
    context_units: dict[int, list[int]] = {}
    reps_per_context: collections.Counter[int] = collections.Counter()
    colours: collections.Counter[tuple[float, float, float]] = collections.Counter()

    for section, stmt in _statements(lines):
        if section == "HEADER":
            if stmt != "ENDSEC;":
                header.append(stmt)
            continue
        if section != "DATA" or not stmt.startswith("#"):
            continue
        n_statements += 1
        m = _ENTITY_RE.match(stmt)
        if m:
            eid, etype, args = int(m.group(1)), m.group(2), m.group(3)
            entity_counts[etype] += 1
            if etype == "PRODUCT":
                strs = [decode_string(x) for x in _STR_RE.findall(args)]
                product[eid] = strs[1] if len(strs) > 1 and strs[1] else (strs[0] if strs else "")
            elif etype.startswith("PRODUCT_DEFINITION_FORMATION"):
                refs = _REF_RE.findall(args)
                if refs:
                    pdf_to_product[eid] = int(refs[-1])
            elif etype in _PRODUCT_DEFS:
                refs = _REF_RE.findall(args)
                if refs:
                    pd_to_pdf[eid] = int(refs[0])
            elif etype == "NEXT_ASSEMBLY_USAGE_OCCURRENCE":
                refs = _REF_RE.findall(_STR_RE.sub("", args))
                nauo.append((int(refs[0]), int(refs[1])))
            elif etype in _SHAPE_REPS:
                refs = _REF_RE.findall(args)
                if refs:
                    reps_per_context[int(refs[-1])] += 1
            elif etype == "COLOUR_RGB":
                nums = _NUM_RE.findall(_STR_RE.sub("", args))
                colours[tuple(round(float(x), 3) for x in nums[-3:])] += 1  # type: ignore[misc]
            continue
        m = _COMPLEX_RE.match(stmt)
        if m:
            eid, body = int(m.group(1)), m.group(2)
            entity_counts["<complex>"] += 1
            if "LENGTH_UNIT" in body:
                length_units[eid] = re.sub(r"\s+", " ", body)
            g = re.search(r"GLOBAL_UNIT_ASSIGNED_CONTEXT\s*\(\s*\(([^)]*)\)", body)
            if g:
                context_units[eid] = [int(x) for x in _REF_RE.findall(g.group(1))]

    def name_of(pd: int) -> str:
        return product.get(pdf_to_product.get(pd_to_pdf.get(pd, -1), -1), f"?pd{pd}")

    children: dict[int, list[int]] = collections.defaultdict(list)
    for parent, child in nauo:
        children[parent].append(child)
    child_set = {c for _, c in nauo}
    roots = [pd for pd in pd_to_pdf if pd not in child_set]

    memo: dict[int, tuple[collections.Counter[str], int]] = {}

    def walk(pd: int) -> tuple[collections.Counter[str], int]:
        # (leaf occurrences by name, depth below pd); assemblies are DAGs, so memoize.
        if pd in memo:
            return memo[pd]
        kids = children.get(pd, [])
        leaves: collections.Counter[str] = collections.Counter()
        depth = 0
        if not kids:
            leaves[name_of(pd)] = 1
        for k in kids:
            kl, kd = walk(k)
            leaves.update(kl)
            depth = max(depth, kd + 1)
        memo[pd] = (leaves, depth)
        return memo[pd]

    leaf_total: collections.Counter[str] = collections.Counter()
    max_depth = 0
    root_names = []
    for r in roots:
        leaves, depth = walk(r)
        leaf_total.update(leaves)
        max_depth = max(max_depth, depth)
        root_names.append(name_of(r))

    contexts = []
    for cid, unit_refs in context_units.items():
        lengths = [length_units[u] for u in unit_refs if u in length_units]
        contexts.append(
            {
                "context": cid,
                "length_unit_metres": unit_to_metres(lengths[0]) if lengths else None,
                "shape_representations": reps_per_context.get(cid, 0),
            }
        )
    weight: collections.Counter[float | None] = collections.Counter()
    for c in contexts:
        weight[c["length_unit_metres"]] += c["shape_representations"]
    dominant = weight.most_common(1)[0][0] if weight else None

    return {
        "header": header,
        "statements": n_statements,
        "entity_counts_top": entity_counts.most_common(25),
        "unique_products": len(set(product.values())),
        "nauo": len(nauo),
        "roots": sorted(root_names),
        "max_depth": max_depth,
        "leaf_occurrences": sum(leaf_total.values()),
        "unique_leaf_names": len(leaf_total),
        "leaf_occurrences_by_name": sorted(leaf_total.items(), key=lambda kv: (-kv[1], kv[0])),
        "length_unit_metres": dominant,
        "contexts": contexts,
        "colours_rgb": [list(c) for c, _ in colours.most_common()],
    }
