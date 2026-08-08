# Changelog

All notable changes to this fork are documented in this file.

## [2.2.1] - 2026-08-08

### Fixed

- Added automatic filtering for nested exact-duplicate source geometry.
- If a child MeshFilter references the same shared Mesh as an ancestor and both objects have effectively the same world transform, the deeper child is omitted from the combine.
- This addresses helper/bake-proxy hierarchies where one wall and one nested proxy were both being included. In the real-scene test, 119 selected walls produced 238 mesh sources before this fix.
- Duplicate filtering is based on mesh identity + hierarchy + world transform, not project-specific object names.
- Combine logs report the skipped nested duplicate count.

### Validation note

- In the 2.2.0 test, **UV Overlap** was essentially clean while **Texel Validity** showed invalid texels exactly along modular boundaries. Visible baked seams aligned with those boundaries. Duplicate geometry must be eliminated before topology welding or a topology-aware lightmap unwrap is evaluated.

## [2.2.0] - 2026-08-08

### Fixed

- **Preserve And Repack Source UV2** sizes UV charts from world-space surface area instead of trusting each source asset's normalized authored UV2 size.
- UV chart connectivity is detected through shared UV edges, so hard-normal vertex splits do not unnecessarily fragment authored islands.
- Child MeshFilters whose `MeshRenderer.enabled` is false are skipped automatically.

### Changed

- Chart packing applies per-chart density correction based on `sqrt(world surface area / source UV area)` before the final global atlas packing scale.
- Source `Scale In Lightmap` is included in Edit Mode.
- Combine logs report skipped disabled renderers and density range.

## [2.1.2] - 2026-08-08

### Fixed

- Fixed Unity 6 Editor compilation errors in the `Padding Reference Size` popup.

## [2.1.1] - 2026-08-08

### Fixed

- Replaced whole-source-mesh UV2 repacking with chart-level packing.
- Existing chart topology is preserved; this path does not run `GenerateSecondaryUVSet`.
- Chart padding is explicit in texels.

## [2.1.0] - 2026-08-08

### Added

- Added **Lightmap UV Mode** with `None`, `Preserve Source UV2`, `Preserve And Repack Source UV2`, and `Regenerate UV2` modes.

## [2.0.2] - 2026-08-08

### Fixed

- **Restore / Undo Combine** restores the exact pre-combine active/enabled state.

## [2.0.1] - 2026-08-08

### Added

- Added **Restore / Undo Combine**.

### Changed

- Read/Write validation is aggregated into one message.

## [2.0.0] - 2026-08-08

### Added

- Unity Package Manager support.
- Unity `.meta` files for all package assets/folders.
- Runtime and Editor assembly definitions.
- Optional Update/Create MeshCollider output.
- Unity 6 lightmap UV2 generation validation.
- Automatic GI setup.
- Editor Undo support.

### Changed

- Reworked combining to use destination-local matrices without changing hierarchy transforms.
- Improved multi-material/submesh preservation and index handling.

### Known limitations

- No topology welding or Boolean union.
- Internal coincident faces are not removed yet.
- Runtime combining requires CPU-readable source meshes.
