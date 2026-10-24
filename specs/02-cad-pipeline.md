# 02 — CAD Pipeline (STEP → Godot)

## Goal
One command turns every STEP file into Godot-ready assets plus machine-readable
manifests, so mechanisms can be defined on named parts. Re-running it after a
CAD change must never destroy the reviewed configs.

```
python tools/cad_pipeline/run.py --input <step file> --out generated/<name>
python tools/cad_pipeline/run.py --all
```

## Steps
1. **Read STEP with assembly structure** (cadquery/OCP XCAF). Keep component
   names, colors and the full hierarchy. If names are missing or generic
   ("Body1", "Component23"), report it and ask me for a better export.
2. **Units and axes**: STEP is usually millimeters; Godot uses meters and
   Y-up. Detect source units from the file, convert, and record the transform
   in the manifest. Verify with a known dimension from the manual (e.g. the
   field is 12 ft square; tile size) and fail loudly if it doesn't match.
3. **Tessellation**: configurable linear/angular deflection per category
   (fine for visible curved parts, coarse for hidden internals). Normals
   smoothed by feature angle so CAD edges look crisp.
4. **Instancing**: hash each solid's geometry (volume, area, bounding box,
   vertex signature). Identical solids (pollen balls, tiles, screws, wheels)
   become one mesh + many instances. Report counts per unique part.
5. **Cleanup**: drop invisible/internal fasteners if they don't matter
   (configurable list), merge tiny static parts into combined meshes per
   static assembly to reduce draw calls.
6. **Export**: one glTF (.glb) per component (or per merged static group) +
   `scene_tree.json` to rebuild the hierarchy in Godot.
7. **Manifest** (`manifest.json`): for every part: id, name, path in
   hierarchy, parent, local + world transform, bounding box, volume, surface
   area, center of mass, material guess, estimated mass (volume × density
   from `materials.yaml`), instance group, candidate role (see detection).
8. **Colliders** (`colliders/`):
   - Primitive fitting first: sphere (pollen), box (walls, tiles, plates),
     cylinder/capsule (wheels, shafts, posts). Fit error threshold
     configurable.
   - Otherwise convex decomposition (CoACD preferred, V-HACD fallback) with a
     max hull count per part.
   - Field floor = one flat box; perimeter = boxes. Never the render mesh.
   - Output a debug mesh to compare collider vs visual.
9. **LODs**: decimate dense meshes (meshoptimizer or similar) to 2–3 LOD
   levels; keep silhouettes.
10. **Validation report** (`report.html` or `.md` with images): part counts,
    triangle counts, largest meshes, unknown materials, unnamed parts,
    instance groups, detected joints/wheels, collider stats, rendered preview
    images from several angles.

## Automatic detection (proposals only, written to `detected.yaml`)
These are suggestions for the mechanism/robot YAMLs, never applied blindly.
- **Wheels**: cylindrical solids/assemblies with roller patterns (mecanum: N
  rollers at ~45°; omni: rollers at 90°) or rubber tread names; record
  center, axis, radius, width, type, handedness of mecanum rollers
  (needed for correct strafing: check X pattern from the top).
- **Motors**: parts named like goBILDA/REV motors (e.g. "5203", "Yellow
  Jacket", "HD Hex", "Core Hex") or matching their shape; shaft axis.
- **Servos**: goBILDA/Axon/REV servo shapes or names; output spline axis.
- **Joints**: coaxial cylindrical faces between two components (shaft +
  bearing, hinge pins, pivot bolts), linear rails/slides (parallel prismatic
  features, "slide", "rail", "viper" names), gears/pulleys (toothed
  cylinders) and their meshing pairs → gear ratio candidates.
- **Field elements**: repeated spheres of ~3 in diameter → pollen; other
  repeated loose solids → nectar or other elements (match with the manual);
  parts not attached to the floor → candidate moving parts (hives etc.).
- Each detection has a confidence score and the evidence used.

## Materials
`config/materials.yaml`: rules matching part names/colors → material:
```yaml
- match: { name_regex: "(?i)(u-channel|channel|plate|bracket|gobilda)" }
  material: aluminum_6061_anodized_silver
- match: { name_regex: "(?i)tile" }
  material: foam_tile_grey
- match: { name_regex: "(?i)pollen" }
  material: pollen_plastic_yellow    # check color in manual/CAD
```
Each material: density (kg/m³), friction/restitution reference (physics
materials in `physics.yaml`), and render params (albedo, metallic,
roughness, normal map, clearcoat, transmission). Unmatched parts get a
"unknown" magenta material and are listed in the report.

## Godot import
- An editor tool / import script builds `.tscn` scenes from the generated
  output: node hierarchy, mesh instances (MultiMesh for big instance groups),
  colliders on separate layers, LOD setup, materials.
- Generated scenes are never hand-edited; anything manual lives in wrapper
  scenes or YAML so regeneration is safe.
- Stable IDs: parts keep the same ID across re-exports when name + path match
  (and geometry hash is similar). The pipeline reports renamed/removed parts
  that are referenced by YAML configs.

## Performance targets
- Full field + 4 robots: under ~2M triangles on High, draw calls under ~1500
  (use instancing and merging). Report numbers.
- Pipeline for the full field: under 10 minutes, cached per part (skip
  unchanged solids by hash).
