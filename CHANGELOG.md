# Changelog

All notable changes to this fork are documented in this file.

## [2.2.1] - 2026-08-08

### Fixed

- Added automatic filtering for nested exact-duplicate source geometry.
- If a child MeshFilter references the same shared Mesh as an ancestor and both objects have effectively the same world transform, the deeper child is omitted from the combine.
- This specifically addresses helper/bake-proxy hierarchies where one visible wall and one nested proxy were both being included. The real-scene test selected 119 wall objects but the previous combiner consumed 238 source meshes.
- Duplicate filtering is based on mesh identity + hierarchy + world transform, not on project-specific names such as `HF_WallBakeProxy`.
- Combine logs now report how many nested exact duplicates were skipped.

### Validation note

- In the 2.2.0 test, **UV Overlap** was essentially clean while **Texel Validity** showed invalid texels exactly along modular boundaries. Visible baked seams aligned with those boundaries. Duplicate geometry must be eliminated before topology welding or a topology-aware lightmap unwrap is evaluated.

## [2.2.0] - 2026-08-08

### Fixed

- **Preserve And Repack Source UV2** now sizes UV charts from their world-space surface area instead of trusting the normalized size of each source asset's authored UV2 layout.
- This fixes inconsistent lightmap texel density between differently sized modular pieces whose authored lightmap UVs each independently fill most of the 0..1 range.
- UV chart connectivity is now detected through shared UV edges, so hard-normal vertex splits do not unnecessarily turn one authored UV island into several packing charts.
- Child MeshFilters whose `MeshRenderer.enabled` is false are skipped automatically.

### Changed

- Chart packing preserves authored UV2 topology, but applies a per-chart density correction based on `sqrt(world surface area / source UV area)` before the final global atlas packing scale.
- Source `Scale In Lightmap` is included in the relative chart scale in Edit Mode.
- Combine logs report skipped disabled renderers and the normalized chart-density range in addition to chart count/global packing scale.

### Validation context

- Real-scene testing is currently on Unity 6.3 LTS `6000.3.6f1`.
- The supplied `HF_ModularReference.fbx` contains authored `LightmapUV` data on all mesh geometries, so importer-side **Generate Lightmap UVs** is not required for that asset.

## [2.1.2] - 2026-08-08

### Fixed

- Fixed Unity 6 Editor compilation errors in the `Padding Reference Size` popup.
- The `GUIContent` label overload of `EditorGUILayout.IntPopup` now receives `GUIContent[]` display options as required by the Unity Editor API.
- No lightmap UV packing behavior changed from 2.1.1.

## [2.1.1] - 2026-08-08

### Fixed

- Replaced the first whole-source-mesh UV2 repacker with a **chart-level repacker**.
- All existing source UV2 charts now use one global scale, preserving relative texel density across adjacent modular pieces instead of scaling every source mesh independently.
- Existing chart topology is preserved; the repack path does not run `GenerateSecondaryUVSet` and does not intentionally create new vertices.
- Chart padding is explicit in texels instead of being an arbitrary normalized per-mesh border.
- Preserve & Repack no longer uses `CombineInstance.lightmapScaleOffset` / `hasLightmapData`.

### Added

- `Chart Padding (texels)` setting, default `2`.
- `Padding Reference Size` setting, default `512`.
- Combine log reports chart count and the global UV packing scale.

## [2.1.0] - 2026-08-08

### Added

- Added **Lightmap UV Mode** with `None`, `Preserve Source UV2`, `Preserve And Repack Source UV2`, and `Regenerate UV2` modes.

### Changed

- Regenerate UV2 no longer forces UInt32 unconditionally. Index format selection uses the source index-count upper bound, so small meshes can stay UInt16.

## [2.0.2] - 2026-08-08

### Fixed

- **Restore / Undo Combine** restores the exact pre-combine active/enabled state instead of enabling every inactive descendant and every disabled child MeshRenderer.
- The Editor captures the state of child MeshFilter GameObjects and MeshRenderers before combine and keeps the restore snapshot in Unity `SessionState` for the current Editor session.

## [2.0.1] - 2026-08-08

### Added

- Added **Restore / Undo Combine** for fast iteration in the Editor.

### Changed

- Read/Write validation is aggregated into one message instead of logging one warning for every source mesh.
- Edit Mode non-readable meshes are informational because Editor combining can still use them; runtime combining still fails with one aggregated error.

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
