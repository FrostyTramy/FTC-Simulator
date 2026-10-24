"""Inventory of the original resources in the project root.

Hashes every original resource, lists zip contents, compares against the committed manifest
(so a replaced manual or CAD export is noticed) and optionally summarizes STEP files.
Originals are only read, never written. Standard library only.

Usage:
    python tools/inventory/inventory.py                    # check against the manifest
    python tools/inventory/inventory.py --scan-step        # also summarize STEP files (minutes)
    python tools/inventory/inventory.py --update-manifest  # accept the current files
Exit code 1 when a resource is new, changed or missing and the manifest was not updated.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import sys
import time
import zipfile
from pathlib import Path
from typing import Any

sys.path.insert(0, str(Path(__file__).resolve().parent))
import step_scan  # noqa: E402

REPO_ROOT = Path(__file__).resolve().parents[2]
MANIFEST = Path(__file__).resolve().parent / "resources_manifest.json"
RESOURCE_SUFFIXES = {".zip", ".pdf", ".step", ".stp"}
TOOL_PREFIXES = ("Godot_",)
STEP_SUFFIXES = (".step", ".stp")


def sha256_of(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def find_resources(root: Path) -> list[Path]:
    """Original resources: top-level zip/pdf/step files, excluding downloaded tools."""
    return sorted(
        p
        for p in root.iterdir()
        if p.is_file() and p.suffix.lower() in RESOURCE_SUFFIXES and not p.name.startswith(TOOL_PREFIXES)
    )


def compare(found: dict[str, dict[str, Any]], manifest: dict[str, dict[str, Any]]) -> dict[str, str]:
    """Status per file name: ok, changed, new or missing."""
    status = {}
    for name, info in found.items():
        known = manifest.get(name)
        if known is None:
            status[name] = "new"
        elif known["sha256"] != info["sha256"]:
            status[name] = "changed"
        else:
            status[name] = "ok"
    for name in manifest:
        if name not in found:
            status[name] = "missing"
    return status


def describe(path: Path, scan_step: bool) -> dict[str, Any]:
    info: dict[str, Any] = {"size": path.stat().st_size, "sha256": sha256_of(path)}
    if path.suffix.lower() == ".zip":
        with zipfile.ZipFile(path) as z:
            info["members"] = [
                {"name": m.filename, "size": m.file_size} for m in z.infolist() if not m.is_dir()
            ]
            if scan_step:
                info["step"] = {}
                for m in info["members"]:
                    if m["name"].lower().endswith(STEP_SUFFIXES):
                        with z.open(m["name"]) as raw:
                            text = io.TextIOWrapper(raw, encoding="latin-1", newline="")
                            info["step"][m["name"]] = timed_scan(text)
    elif scan_step and path.suffix.lower() in STEP_SUFFIXES:
        with path.open(encoding="latin-1", newline="") as text:
            info["step"] = {path.name: timed_scan(text)}
    return info


def timed_scan(lines: io.TextIOBase) -> dict[str, Any]:
    t0 = time.perf_counter()
    result = step_scan.scan(lines)
    result["scan_seconds"] = round(time.perf_counter() - t0, 1)
    return result


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawTextHelpFormatter)
    ap.add_argument("--root", type=Path, default=REPO_ROOT)
    ap.add_argument("--manifest", type=Path, default=MANIFEST)
    ap.add_argument("--out", type=Path, default=REPO_ROOT / "generated" / "inventory" / "inventory.json")
    ap.add_argument("--scan-step", action="store_true")
    ap.add_argument("--update-manifest", action="store_true")
    args = ap.parse_args(argv)

    found = {p.name: describe(p, args.scan_step) for p in find_resources(args.root)}
    manifest = json.loads(args.manifest.read_text("utf-8"))["files"] if args.manifest.exists() else {}
    status = compare(found, manifest)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps({"files": found, "status": status}, indent=1, ensure_ascii=False), "utf-8")
    for name, s in sorted(status.items()):
        print(f"{s:8} {name}")

    if args.update_manifest:
        entries = {n: {"size": i["size"], "sha256": i["sha256"]} for n, i in sorted(found.items())}
        args.manifest.write_text(json.dumps({"files": entries}, indent=1) + "\n", "utf-8")
        print(f"manifest updated: {len(entries)} files")
        return 0
    return 0 if all(s == "ok" for s in status.values()) else 1


if __name__ == "__main__":
    sys.exit(main())
