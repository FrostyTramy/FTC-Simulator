"""Print the pinned requirements that are missing or at another version, separated by '; '.

Usage: python check_requirements.py <requirements.txt>   (prints nothing when all match)
"""

import importlib.metadata as metadata
import sys

missing = []
for raw in open(sys.argv[1], encoding="utf-8"):
    line = raw.split("#")[0].strip()
    if not line:
        continue
    name, want = line.split("==")
    try:
        have = metadata.version(name)
    except metadata.PackageNotFoundError:
        have = None
    if have != want:
        missing.append(f"{name} (have {have}, want {want})")
print("; ".join(missing))
