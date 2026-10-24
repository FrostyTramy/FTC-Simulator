"""Extract the game manual text page by page and flag places where glyphs were lost.

The manual's text layer maps some digits to spaces (e.g. "AprilTag ID's  0,   ,   ,"), so every
number taken from the text must be checked against the rendered page. This tool writes:
  docs/manual_text/p001.txt ...           page text, one file per page (git-ignored)
  generated/manual_pages/p001.png ...      rendered pages for the requested range (--render)
  generated/manual_text_suspects.txt       lines that probably lost characters

Usage: python tools/manual/extract.py [--manual PDF] [--render 62-144] [--dpi 110]
"""

from __future__ import annotations

import argparse
import re
from pathlib import Path

import fitz  # PyMuPDF

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_MANUAL = REPO_ROOT / "BIOBUZZ_Competition_Manual_TU06.pdf"

# A run of 2+ spaces between two non-space characters, or before punctuation, inside a line.
_GAP_RE = re.compile(r"\S {2,}[\S]|[(≤≥~x] {2,}|\b(?:Red|Blue) {2,}$")


def suspect_lines(text: str) -> list[str]:
    """Lines whose internal spacing suggests characters were dropped from the text layer."""
    return [line.strip() for line in text.splitlines() if _GAP_RE.search(line.rstrip())]


def page_range(spec: str) -> range:
    first, _, last = spec.partition("-")
    return range(int(first), int(last or first) + 1)


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawTextHelpFormatter)
    ap.add_argument("--manual", type=Path, default=DEFAULT_MANUAL)
    ap.add_argument("--text-out", type=Path, default=REPO_ROOT / "docs" / "manual_text")
    ap.add_argument("--gen-out", type=Path, default=REPO_ROOT / "generated")
    ap.add_argument("--render", default="", help="page range to render as PNG, e.g. 62-144")
    ap.add_argument("--dpi", type=int, default=110)
    args = ap.parse_args(argv)

    doc = fitz.open(args.manual)
    args.text_out.mkdir(parents=True, exist_ok=True)
    report = []
    for index, page in enumerate(doc):
        number = index + 1
        text = page.get_text()
        (args.text_out / f"p{number:03d}.txt").write_text(text, "utf-8")
        report += [f"p{number}: {line}" for line in suspect_lines(text)]

    args.gen_out.mkdir(parents=True, exist_ok=True)
    (args.gen_out / "manual_text_suspects.txt").write_text("\n".join(report) + "\n", "utf-8")

    if args.render:
        pages_dir = args.gen_out / "manual_pages"
        pages_dir.mkdir(parents=True, exist_ok=True)
        for number in page_range(args.render):
            doc[number - 1].get_pixmap(dpi=args.dpi).save(pages_dir / f"p{number:03d}.png")

    print(f"{doc.page_count} pages -> {args.text_out}; {len(report)} suspect lines")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
