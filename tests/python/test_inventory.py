"""Tests for tools/inventory/inventory.py: resource discovery and manifest checking."""

from __future__ import annotations

import json
import sys
import zipfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "tools" / "inventory"))
import inventory  # noqa: E402


def make_root(tmp_path: Path) -> Path:
    (tmp_path / "manual.pdf").write_bytes(b"%PDF-1.7 fake")
    (tmp_path / "CLAUDE.md").write_text("brief")
    (tmp_path / "Godot_v4.7.2-stable_mono_win64.zip").write_bytes(b"tool, not a resource")
    with zipfile.ZipFile(tmp_path / "cad.zip", "w") as z:
        z.writestr("part.step", "ISO-10303-21;\nDATA;\nENDSEC;\nEND-ISO-10303-21;\n")
    return tmp_path


def test_finds_only_resources(tmp_path: Path) -> None:
    names = [p.name for p in inventory.find_resources(make_root(tmp_path))]
    assert names == ["cad.zip", "manual.pdf"]


def test_manifest_round_trip_then_detects_replacement(tmp_path: Path) -> None:
    root = make_root(tmp_path)
    manifest = tmp_path / "manifest.json"
    out = tmp_path / "out" / "inventory.json"
    common = ["--root", str(root), "--manifest", str(manifest), "--out", str(out)]

    assert inventory.main([*common, "--update-manifest"]) == 0
    assert inventory.main(common) == 0

    (root / "manual.pdf").write_bytes(b"%PDF-1.7 newer team update")
    (root / "cad.zip").unlink()
    assert inventory.main(common) == 1
    status = json.loads(out.read_text("utf-8"))["status"]
    assert status == {"manual.pdf": "changed", "cad.zip": "missing"}


def test_zip_members_and_step_scan(tmp_path: Path) -> None:
    info = inventory.describe(make_root(tmp_path) / "cad.zip", scan_step=True)
    assert info["members"][0]["name"] == "part.step"
    assert info["step"]["part.step"]["statements"] == 0


def test_committed_manifest_matches_resources_on_disk() -> None:
    """The real originals must match the committed manifest (skipped on machines without them)."""
    manifest = json.loads(inventory.MANIFEST.read_text("utf-8"))["files"]
    present = {p.name for p in inventory.find_resources(REPO_ROOT)}
    if not present:
        import pytest

        pytest.skip("original resources not present (e.g. CI)")
    found = {name: {"sha256": inventory.sha256_of(REPO_ROOT / name)} for name in present}
    assert inventory.compare(found, manifest) == {name: "ok" for name in manifest}
