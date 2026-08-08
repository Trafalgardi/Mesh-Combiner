# Changelog

All notable changes to this fork are documented in this file.

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
