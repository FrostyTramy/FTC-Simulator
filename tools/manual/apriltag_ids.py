"""Decode the AprilTag IDs printed in the official printable tag PDFs (36h11 family).

The manual's text layer loses digits of the tag IDs (§9.9, p76), so the IDs are confirmed from
the tag images. Prints one line per page: page label, tag IDs left to right.

Usage: python tools/manual/apriltag_ids.py [PrintableAprilTag_A4.pdf]
"""

from __future__ import annotations

import sys
from pathlib import Path

import fitz  # PyMuPDF
import numpy as np
from pupil_apriltags import Detector

REPO_ROOT = Path(__file__).resolve().parents[2]


def decode(pdf: Path, dpi: int = 100) -> list[tuple[str, list[int]]]:
    detector = Detector(families="tag36h11")
    doc = fitz.open(pdf)
    result = []
    for page in doc:
        pix = page.get_pixmap(dpi=dpi, colorspace=fitz.csGRAY)
        image = np.frombuffer(pix.samples, dtype=np.uint8).reshape(pix.height, pix.width)
        tags = sorted(detector.detect(image), key=lambda t: t.center[0])
        label = page.get_text().split()[0] if page.get_text().split() else "?"
        result.append((label, [int(t.tag_id) for t in tags]))
    return result


if __name__ == "__main__":
    path = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO_ROOT / "PrintableAprilTag_A4.pdf"
    for label, ids in decode(path):
        print(f"{label:8} {ids}")
