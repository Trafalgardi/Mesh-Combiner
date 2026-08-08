# Changelog

All notable changes to this fork are documented in this file.

## [2.1.1] - 2026-08-08

### Fixed

- Replaced the first whole-source-mesh UV2 repacker with a **chart-level repacker**.
- All existing source UV2 charts now use one global scale, preserving relative texel density across adjacent modular pieces instead of scaling every source mesh independently.
- Existing chart topology is preserved; the repack path does not run `GenerateSecondaryUVSet` and does not intentionally create new vertices.
- Chart padding is now explicit in texels instead of being an arbitrary normalized per-mesh border.
- Preserve & Repack no longer uses `CombineInstance.lightmapScaleOffset` / `hasLightmapData`, avoiding the unexpected extra lightmap UV channel seen in the Unity 6 real-scene test.

### Added

- `Chart Padding (texels)` setting, default `2`.
- `Padding Reference Size` setting, default `512`.
- Combine log now reports chart count and the global UV packing scale.

### Notes

- Real-scene 2.1.0 testing showed a visible lightmap seam exactly at a modular wall boundary and a change in lightmap texel visualization density across that boundary. The independent per-source scaling in 2.1.0 was the cause addressed by this revision.

## [2.1.0] - 2026-08-08

### Added

- Added **Lightmap UV Mode** with `None`, `Preserve Source UV2`, `Preserve And Repack Source UV2`, and `Regenerate UV2` modes.
- Added the first source-UV2 preservation/repack experiment for modular static geometry.

### Changed

- Regenerate UV2 no longer forces UInt32 unconditionally. Index format selection uses the source index-count upper bound, so small meshes can stay UInt16.

### Known issue

- The first repack implementation scaled each source mesh independently into an equal atlas cell. This could change texel density between adjacent modules and shrink internal chart margins. Fixed in 2.1.1.

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
