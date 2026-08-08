# Mesh Combiner for Unity

A Unity 6 editor/runtime utility for combining child `MeshFilter` meshes into a single mesh to reduce renderer/draw-call overhead, with a workflow aimed at static geometry and baked lighting.

This repository is a modernized fork of `dawid-t/Mesh-Combiner`.

## Install via Unity Package Manager

Use **Window -> Package Management -> Package Manager -> + -> Install package from git URL...** and paste:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git
```

Until the Unity 6 modernization PR is merged, use:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization
```

Manual `Packages/manifest.json` dependency:

```json
"com.trafalgardi.mesh-combiner": "https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization"
```

Unity pins Git dependencies in `Packages/packages-lock.json`. If a branch update is not picked up, use Package Manager Update, remove the lock entry, or remove/re-add the package.

## Requirements

- Unity 6.0 (`6000.0`) or newer.
- Real-scene validation is being performed on Unity **6.3 LTS / 6000.3.6f1**.
- Git in `PATH` for UPM Git installation.
- Runtime combining requires source meshes with `Read/Write Enabled`.

Package ID: `com.trafalgardi.mesh-combiner`

Current version: **2.2.0**

## Main features

- transform-safe mesh combining;
- single and multi-material/submesh preservation;
- UInt16/UInt32 selection;
- optional combined `MeshCollider`;
- disabled child renderers are skipped so hidden bake proxies/helpers are not duplicated;
- exact-state **Restore / Undo Combine**;
- combined mesh asset saving;
- baked-light UV2 preservation/repacking and regeneration modes.

## Lightmap UV Mode

### None

No UV2 processing.

### Preserve Source UV2

Keeps source `Mesh.uv2` unchanged. Mainly diagnostic because modular instances commonly reuse the same 0..1 UV2 space and overlap after combine.

### Preserve And Repack Source UV2

Recommended for modular static geometry with authored lightmap UVs.

Version 2.2.0:

- detects authored UV2 islands through shared UV edges;
- calculates world-space area and source UV area for every chart;
- applies relative linear density correction based on `sqrt(world surface area / source UV area)`;
- includes source **Scale In Lightmap** in Edit Mode;
- packs all corrected charts into one final 0..1 atlas;
- keeps authored chart topology;
- does not call `GenerateSecondaryUVSet`;
- does not intentionally create vertices;
- edits temporary mesh copies only.

This is required for modular packs where different sized assets each normalize their own lightmap UVs to fill most of 0..1. Their original UV size is therefore not a valid cross-object texel-density measure after they become a single MeshRenderer.

#### Chart Padding

Default: **2 texels** at a **512** reference size.

Increase padding only when Unity's **UV Overlap** visualization shows chart-neighborhood overlap.

### Regenerate UV2

Runs Unity's `Unwrapping.GenerateSecondaryUVSet` on the combined mesh. This is a fallback for missing/broken source UV2 and may split vertices/change seams.

## Disabled renderers / bake proxies

A source `MeshFilter` is skipped when its sibling `MeshRenderer.enabled` is false. The combine log reports the count and examples.

This avoids accidentally baking hidden proxy/helper geometry into the generated mesh.

## Recommended baked-light test

1. Set **Preserve And Repack Source UV2**.
2. Keep padding `2 @ 512`.
3. Combine.
4. Read the log: source count, skipped disabled renderers, chart count, packing scale, density range.
5. Check vertex/triangle counts.
6. Check **Baked Lightmap**, **UV Overlap**, and **Texel Validity** visualization.
7. Bake.
8. Compare modular boundaries.
9. Restore before the next A/B run.

## Restore / Undo Combine

The Editor inspector snapshots source `activeSelf` and `MeshRenderer.enabled` states. Restore clears the generated mesh/materials/collider reference and returns sources to the exact recorded state. Unsaved generated meshes are destroyed through Unity Undo; saved mesh assets are retained.

## MeshCollider

Enable **Update/Create MeshCollider** to assign the combined render mesh to a destination MeshCollider. Primitive/custom low-poly child colliders are not merged.

## What this tool still does not do

`Mesh.CombineMeshes` is not a topology union. This fork currently does not:

- weld coincident geometry vertices;
- remove internal coplanar faces where modular meshes touch;
- close geometry gaps;
- perform Boolean union/intersection;
- simplify topology.

Those operations remain a separate future static-geometry cleanup step because careless welding/internal-face removal can break normals, UV seams, materials, and intentional interior surfaces.

## Basic code use

```csharp
MeshCombiner combiner = GetComponent<MeshCombiner>();
combiner.CreateMultiMaterialMesh = true;
combiner.UvMode = LightmapUvMode.PreserveAndRepackSourceUv2;
combiner.UpdateOrCreateMeshCollider = true;
combiner.CombineMeshes(true);
```

## License

MIT, following the original project.
