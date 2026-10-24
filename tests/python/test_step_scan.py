"""Tests for tools/inventory/step_scan.py on tiny hand-written STEP files."""

from __future__ import annotations

import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "tools" / "inventory"))
import step_scan  # noqa: E402

# Root assembly "ROBOT" uses sub-assembly "WHEEL" twice; "WHEEL" holds 3 "ROLLER" parts.
# One geometric context in millimetres with two shape representations.
ASSEMBLY_MM = """ISO-10303-21;
HEADER;
FILE_NAME('tiny.step','2026-10-24',(''),(''),'test','test','');
ENDSEC;
DATA;
#1=PRODUCT('ROBOT','ROBOT','',(#90));
#2=PRODUCT('WHEEL','WHEEL','',(#90));
#3=PRODUCT('ROLLER','ROL
LER','',(#90));
#11=PRODUCT_DEFINITION_FORMATION('','',#1);
#12=PRODUCT_DEFINITION_FORMATION('','',#2);
#13=PRODUCT_DEFINITION_FORMATION_WITH_SPECIFIED_SOURCE('','',#3,.NOT_KNOWN.);
#21=PRODUCT_DEFINITION('design','',#11,#91);
#22=PRODUCT_DEFINITION('design','',#12,#91);
#23=PRODUCT_DEFINITION('design','',#13,#91);
#31=NEXT_ASSEMBLY_USAGE_OCCURRENCE('NAUO1','left #9','',#21,#22,$);
#32=NEXT_ASSEMBLY_USAGE_OCCURRENCE('NAUO2','right','',#21,#22,$);
#33=NEXT_ASSEMBLY_USAGE_OCCURRENCE('NAUO3','r1','',#22,#23,$);
#34=NEXT_ASSEMBLY_USAGE_OCCURRENCE('NAUO4','r2','',#22,#23,$);
#35=NEXT_ASSEMBLY_USAGE_OCCURRENCE('NAUO5','r3','',#22,#23,$);
#40=( LENGTH_UNIT() NAMED_UNIT(*) SI_UNIT(.MILLI.,.METRE.) );
#41=( NAMED_UNIT(*) PLANE_ANGLE_UNIT() SI_UNIT($,.RADIAN.) );
#42=( GEOMETRIC_REPRESENTATION_CONTEXT(3) GLOBAL_UNIT_ASSIGNED_CONTEXT((#40,#41))
 REPRESENTATION_CONTEXT('',''));
#50=ADVANCED_BREP_SHAPE_REPRESENTATION('',(#60),#42);
#51=SHAPE_REPRESENTATION('',(#61),#42);
#70=COLOUR_RGB('',1.,0.937,0.247);
ENDSEC;
END-ISO-10303-21;
"""


def scan_text(text: str) -> dict:
    return step_scan.scan(text.splitlines(keepends=True))


def test_assembly_is_flattened_through_shared_subassemblies() -> None:
    r = scan_text(ASSEMBLY_MM)
    assert r["roots"] == ["ROBOT"]
    assert r["nauo"] == 5
    assert r["max_depth"] == 2
    assert r["leaf_occurrences"] == 6
    assert r["leaf_occurrences_by_name"] == [("ROLLER", 6)]


def test_wrapped_string_is_joined_without_space() -> None:
    assert scan_text(ASSEMBLY_MM)["unique_products"] == 3  # 'ROL\nLER' -> 'ROLLER', not 'ROL LER'


def test_millimetre_context_detected() -> None:
    r = scan_text(ASSEMBLY_MM)
    assert r["length_unit_metres"] == 1e-3
    assert r["contexts"][0]["shape_representations"] == 2


def test_metre_context_detected() -> None:
    r = scan_text(ASSEMBLY_MM.replace("SI_UNIT(.MILLI.,.METRE.)", "SI_UNIT($,.METRE.)"))
    assert r["length_unit_metres"] == 1.0


def test_inch_conversion_unit() -> None:
    assert step_scan.unit_to_metres("CONVERSION_BASED_UNIT('INCH',#5) LENGTH_UNIT() NAMED_UNIT(#6)") == 0.0254


def test_header_and_colours() -> None:
    r = scan_text(ASSEMBLY_MM)
    assert r["header"][0].startswith("FILE_NAME('tiny.step'")
    assert r["colours_rgb"] == [[1.0, 0.937, 0.247]]


def test_x2_unicode_decoding() -> None:
    assert step_scan.decode_string(r"Caf\X2\00E9\X0\ It''s") == "Café It's"
