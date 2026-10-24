# Architecture (as built)

The target architecture is in `specs/01-architecture.md`. This file describes what exists.

## Repository
```
config/            YAML configs + schemas/ + robots/      (empty, Phase 1+)
docs/              project documents (this file, STATUS, RESOURCES, ...)
generated/         pipeline output, git-ignored (inventory/ so far)
godot/             Godot C# project                       (empty, Phase 2)
java/              ftc-sdk-shim/, runner/, build-adapter/  (empty, Phase 6)
scripts/           PowerShell entry points; lib/ holds tool discovery helpers
tests/python/      pytest tests for the Python tools
tools/inventory/   resource inventory (stdlib only)
tools/cad_pipeline/ requirements.txt (pipeline code, Phase 2)
workspace/         user code projects, git-ignored
```

## Components
- **Resource inventory** (`tools/inventory/`)
  - `step_scan.py`: streams a STEP Part 21 file and resolves PRODUCT → formation → definition,
    flattens NEXT_ASSEMBLY_USAGE_OCCURRENCE links (DAG, memoized) into leaf occurrence counts,
    and reads the length unit of each geometric context.
  - `inventory.py`: SHA-256 + zip members for every root resource, compares against
    `resources_manifest.json`, writes `generated/inventory/inventory.json`.
- **Tool discovery** (`scripts/lib/tools.ps1`): finds Godot .NET, JDK 17 and Python without
  trusting PATH `java`; native calls that may hang go through `Invoke-WithTimeout`.
