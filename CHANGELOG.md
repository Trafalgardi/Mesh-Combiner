# Changelog

All notable changes to this fork are documented in this file.

## [2.4.1] - 2026-08-08

### Added

- Coplanar Stitched UV2 now detects **collinear partially-overlapping boundary edges** in addition to exact endpoint-to-endpoint edge matches.
- Added generic T-junction support for modular surfaces whose boundary segmentation differs between neighboring pieces, such as walls containing windows, doors, arches, cutouts, or other openings.
- A long boundary edge can now stitch to one or more shorter collinear edges when the segments physically overlap and the owning triangles pass the coplanar-normal test.
- Result diagnostics now report exact shared-edge connections, partial/T-junction connections, total stitched connections, and the number of boundary edges inspected.

### Safety / behavior

- Partial-edge matching uses the configured world-space position tolerance for line coincidence and non-zero segment overlap.
- Partial-edge direction matching is capped at 1 degree even when the configured coplanar triangle angle is higher.
- Geometry, UV0, normals, tangents, materials, triangle indices, and render topology remain unchanged; only UV2 is rewritten.
- This does not perform a Boolean union or remove partially overlapping coplanar faces. It only expands lightmap-chart connectivity across compatible geometric boundary segments.

### Validation target

- Real-scene testing of 2.4.0 removed baked seams on ordinary flush wall-module boundaries but left seams next to window/door modules where openings change boundary segmentation.
- 2.4.1 specifically targets those T-junction/partial-boundary cases without adding project-specific names or assumptions.

## [2.4.0] - 2026-08-08

### Added

- Added experimental **Coplanar Stitched UV2** generator under `Tools -> Mesh Combiner -> Coplanar Stitched UV2...`.
- The generator groups triangles through matching world-space geometric edges and only joins neighbors whose normals are within a configurable coplanar-angle threshold.
- Connected coplanar surfaces are planar-projected into one continuous UV2 chart so modular boundaries can share the same lightmap chart without welding render topology.
- All generated charts use one global world-space scale before packing, preserving consistent texel density.
- Added configurable edge-position tolerance, coplanar angle, chart padding, and padding reference size.
- The tool clones the current combined mesh before replacing UV2, so saved/imported mesh assets are not modified in place.
- If the combined MeshCollider references the previous mesh, it is updated to the UV2 clone.

### Safety / validation

- Geometry, UV0, normals, tangents, materials and triangle topology are not modified.
- Generation aborts rather than corrupting UV2 when one vertex index is shared by multiple generated hard-angle charts and would require safe vertex splitting.
- Current experimental implementation requires triangle topology.
- This mode intentionally targets lightmap seams after `UV Overlap` and `Texel Validity` are already clean; it is not a Boolean union or geometry weld.

## [2.3.0] - 2026-08-08

### Added

- Added optional **Remove Exact Opposing Faces** geometry cleanup.
- Exact cleanup matches triangle pairs by the same three destination-local positions within a configurable tolerance and requires opposite face direction.
- Matching pairs are removed from the combined submesh index buffers before optional `Regenerate UV2` runs.
- Added **Position Tolerance** with a default of `0.0001` Unity units.
- Combine logs report removed face-pair and triangle counts.

### Safety / behavior

- Exact opposing-face cleanup is off by default because intentionally double-sided geometry can also contain coincident opposite-wound triangles.
- Cleanup removes triangle indices only. It does not weld or compact vertices and does not rewrite normals, tangents, UVs, colors, skinning data, or other vertex attributes.
- Multi-material output is supported; matching opposing triangles can be removed even when they belong to different submeshes/materials.
- Non-triangle submeshes are left untouched.
- This is not a Boolean union and does not remove partial coplanar overlaps or coincident surfaces that use different triangulation.

## [2.2.2] - 2026-08-08

### Fixed

- Replaced the Editor-session-only restore snapshot with a crash-persistent snapshot stored through `EditorPrefs`.
- Snapshot entries now keep both the current InstanceID and a stable `GlobalObjectId`, so exact source state can be resolved again after domain reloads and Editor restarts.
- The restore snapshot is persisted **before** the hierarchy is mutated by Combine, so an Editor crash during/after Combine does not automatically lose the recovery data.

### Added

- Added **Force Recover Sources (No Snapshot)**. It clears the destination `MeshFilter`, output materials, and matching combined `MeshCollider`, then activates every descendant MeshFilter GameObject and enables its MeshRenderer.
- Force recovery is intentionally aggressive and is available for stale/crashed scenes where no exact snapshot can be resolved. It can re-enable helpers or variants that were intentionally disabled before Combine.
- Inspector help now clarifies that different checker phase/offset between independent lightmap charts is expected; matching checker size/texel density is the relevant diagnostic.

## [2.2.1] - 2026-08-08

### Fixed

- Added automatic filtering for nested exact-duplicate source geometry.
- A deeper child is skipped when it references the same shared Mesh and has the same world transform as an ancestor below the combiner root.
- This addresses a validation case where each visible modular object also contained a nested bake/helper proxy.
- Duplicate filtering does not depend on project-specific names.
- Combine logs report the skipped nested duplicate count.

## [2.2.0] - 2026-08-08

- Repack authored UV2 charts using world-space surface density.
- Detect chart connectivity through shared UV edges.
- Skip disabled source MeshRenderers.
- Include source Scale In Lightmap in Edit Mode density calculations.

## [2.1.2] - 2026-08-08

- Fixed Unity 6 `EditorGUILayout.IntPopup` compilation.

## [2.1.1] - 2026-08-08

- Added chart-level source UV2 repacking with texel padding.

## [2.1.0] - 2026-08-08

- Added lightmap UV modes and improved UInt16/UInt32 selection.

## [2.0.2] - 2026-08-08

- Restore now restores exact pre-combine active/enabled state.

## [2.0.1] - 2026-08-08

- Added Restore / Undo Combine and aggregated validation logs.

## [2.0.0] - 2026-08-08

- Added Unity 6 modernization, UPM packaging, meta files, asmdefs, MeshCollider output, GI setup, transform-safe combining and Editor Undo.

### Known limitations

- No topology welding or Boolean union.
- Partial coplanar overlaps and differently triangulated coincident surfaces are not removed.
- Runtime combining requires CPU-readable source meshes.
