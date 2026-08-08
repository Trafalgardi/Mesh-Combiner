# Changelog

All notable changes to this fork are documented in this file.

## [2.1.0] - 2026-08-08

### Added

- Added **Lightmap UV Mode** with four explicit modes: None, Preserve Source UV2, Preserve & Repack Source UV2, and Regenerate UV2.
- Added **Preserve & Repack Source UV2**, intended for modular static geometry that already has valid lightmap UV charts.
- Preserve & Repack keeps each source mesh's existing UV2 charts and scales/offsets them into non-overlapping cells in one combined 0..1 atlas.
- Added configurable normalized repack padding; the default is `0.002` (about one pixel at a 512px lightmap).
- Preserve & Repack validates that every source mesh has a non-degenerate UV2 channel and fails with one aggregated error if it does not.

### Changed

- `Regenerate UV2` no longer forces a 32-bit index buffer just because regeneration is enabled.
- Index format is selected from an upper bound derived from source index counts. Small combined meshes now remain UInt16 even when UV2 regeneration splits vertices.
- Combine logs now report triangle count and the selected lightmap UV strategy; regenerated UV2 reports how many vertices were added by chart splits.
- `RestoreCombinedState` no longer enables every inactive descendant. Exact source-state restoration stays in the Editor button, which has the pre-combine snapshot.

### Why

- Real Unity 6 testing on the modular wall scene showed `GenerateSecondaryUVSet` increasing the combined wall mesh from 10,616 to 14,940 vertices (+40.7%) while triangle count stayed at 5,320.
- The regenerated UV layout also produced visible baked seams, so preserving the source charts and repacking them is now the preferred test path.

## [2.0.2] - 2026-08-08

### Fixed

- **Restore / Undo Combine** now restores the exact pre-combine active/enabled state instead of enabling every inactive descendant and every disabled child MeshRenderer.
- The Editor captures the state of child MeshFilter GameObjects and MeshRenderers before combine and keeps the restore snapshot in Unity `SessionState` for the current Editor session.
- This prevents previously inactive helper/variant geometry from becoming active after Restore and then being accidentally included in the next combine pass.

### Changed

- Combine is disabled while an exact restore snapshot is pending, forcing a clean `Combine -> Restore -> Combine` comparison loop.
- Existing combined meshes created before 2.0.2 show a warning because they do not have an exact restore snapshot.

## [2.0.1] - 2026-08-08

### Added

- Added **Restore / Undo Combine** for fast iteration in the Editor.
- Restore clears the destination combined Mesh and materials, clears the generated MeshCollider reference, reactivates child GameObjects, and re-enables child MeshRenderers.
- Unsaved transient combined Mesh objects are removed through the Unity Undo system; saved `.asset` meshes are kept.

### Changed

- Read/Write validation is now aggregated into one message instead of logging one warning for every source mesh.
- Edit Mode non-readable meshes are reported as informational because Editor combining can still use them; runtime combining still fails with one aggregated error.
- Missing Mesh and MeshRenderer validation messages are aggregated as well.

## [2.0.0] - 2026-08-08

### Added

- Unity Package Manager support through a root `package.json`.
- Unity `.meta` files for all package assets and folders so Git-installed immutable packages import correctly.
- Runtime and Editor assembly definitions for package-based installation.
- Optional Update/Create MeshCollider output.
- Unity 6 lightmap UV2 generation validation.
- Automatic Contribute GI / Receive GI = Lightmaps setup when generating UV2.
- Editor Undo support.

### Changed

- Reworked combining to use destination-local matrices without temporarily changing hierarchy transforms.
- Improved multi-material and submesh preservation.
- Improved 32-bit index handling for large meshes and lightmap UV generation.
- Improved generated mesh asset saving and validation.
- Source objects are deactivated by default instead of destroyed.

### Fixed

- Fixed Git-UPM installation being ignored because package files had no committed `.meta` files.
- Fixed the upstream missing MeshCollider case when collider output is enabled.
- Fixed fragile transform handling during combine.
- Fixed ignored secondary UV generation failures.

### Known limitations

- No topology welding or Boolean union.
- Internal coincident faces are not removed yet.
- Runtime combining requires CPU-readable source meshes.
