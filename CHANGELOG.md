# Changelog

All notable changes to this fork are documented in this file.

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
- This addresses the real test case where 119 selected wall objects produced 238 source meshes because every wall also contained a nested bake/helper proxy.
- Duplicate filtering does not depend on project-specific names.
- Combine logs report the skipped nested duplicate count.

### Validation note

- In the 2.2.0 test, UV Overlap was essentially clean while Texel Validity showed invalid texels exactly along modular boundaries and visible baked seams aligned with those boundaries. Duplicate geometry must be eliminated before topology welding/unwrap is evaluated.

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
- Internal coincident faces are not removed yet.
- Runtime combining requires CPU-readable source meshes.
