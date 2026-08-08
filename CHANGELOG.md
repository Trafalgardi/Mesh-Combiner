# Changelog

All notable changes to this fork are documented in this file.

## [2.4.2] - 2026-08-09

### Fixed

- Fixed T-junction candidate collection for **closed / manifold modular meshes**.
- Version 2.4.1 incorrectly treated only single-owner edges as boundary candidates. Closed wall modules normally have two owners at a visible perimeter edge (for example front face + side/end face), so the partial-edge pass could inspect zero candidates even when T-junction seams existed.
- The stitcher now skips only ordinary two-owner coplanar triangulation edges and keeps open, hard, non-manifold, and multi-owner edge records as generic seam candidates.
- Partial/T-junction matching can therefore inspect differently segmented closed-module seams around windows, doors, arches, cutouts, and similar geometry.
- Diagnostics now describe the count as **seam-candidate edge records inspected** rather than implying that all candidates are open mesh boundaries.

### Safety / behavior

- No project-specific names or asset assumptions are used.
- Partial matching still requires coplanar owning triangles, collinear edge lines within tolerance, non-zero segment overlap, and a maximum 1-degree edge-direction difference.
- Geometry, UV0, normals, tangents, materials, and triangle indices remain unchanged; only UV2 is rewritten by the experimental stitcher.

## [2.4.1] - 2026-08-08

### Added

- Added generic T-junction support for collinear partially-overlapping edges.
- A long seam edge can stitch to one or more shorter collinear edges when they overlap and the owning triangles pass the coplanar-normal test.
- Diagnostics report exact connections, partial/T-junction connections, total connections, candidate count, chart count, and packing scale.

## [2.4.0] - 2026-08-08

### Added

- Added experimental **Coplanar Stitched UV2** generator under `Tools -> Mesh Combiner -> Coplanar Stitched UV2...`.
- Exact matching geometric edges can join disconnected modular pieces into one coplanar lightmap chart without welding render topology.
- Connected coplanar surfaces are planar-projected and packed with one world-space scale.
- Geometry, UV0, normals, tangents, materials, and triangle topology are not modified.

## [2.3.0] - 2026-08-08

### Added

- Added optional **Remove Exact Opposing Faces** geometry cleanup.
- Exact coincident opposite-wound triangle pairs can be removed from combined submesh index buffers.
- Cleanup is opt-in and is not a Boolean union.

## [2.2.2] - 2026-08-08

### Fixed / Added

- Restore snapshots now survive Editor restarts/crashes through stable `GlobalObjectId` references stored persistently.
- Added **Force Recover Sources (No Snapshot)** for emergency recovery.

## [2.2.1] - 2026-08-08

- Added automatic filtering for nested exact-duplicate source geometry.
- Duplicate filtering is based on shared Mesh + world transform + hierarchy, not object names.

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

- Added Unity 6 modernization, UPM packaging, committed meta files, asmdefs, MeshCollider output, GI setup, transform-safe combining and Editor Undo.

## Known limitations

- No general Boolean union.
- No arbitrary partial coplanar face removal.
- No general polygon matching across different triangulations.
- No destructive weld-by-position without topology/attribute checks.
- Runtime combining requires CPU-readable source meshes.
