# Mesh Combiner for Unity

A Unity 6 editor/runtime utility for combining child `MeshFilter` meshes into a single mesh to reduce renderer/draw-call overhead, with a workflow aimed at static geometry and baked lighting.

This repository is a modernized fork of `dawid-t/Mesh-Combiner`.

## Install via Unity Package Manager

1. Open **Window -> Package Management -> Package Manager**.
2. Click **+**.
3. Choose **Install package from git URL...**.
4. Paste:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization
```

Package ID: `com.trafalgardi.mesh-combiner`

Current version: **2.2.1**

## Requirements

- Unity 6.0 (`6000.0`) or newer.
- Git in `PATH` for UPM Git installation.
- Runtime combining requires source meshes with `Read/Write Enabled`.

## Main features

- transform-safe combining;
- single/multi-material and submesh preservation;
- UInt16/UInt32 selection;
- optional combined `MeshCollider`;
- aggregated validation logs;
- disabled-renderer filtering;
- nested exact-duplicate filtering;
- GI/lightmap setup;
- exact-state **Restore / Undo Combine**;
- combined mesh asset saving;
- multiple UV2 workflows.

## Nested exact duplicates

Version 2.2.1 automatically skips a nested child MeshFilter when an ancestor below the MeshCombiner root references the exact same shared Mesh and both objects have effectively the same world transform.

The deeper object is treated as a duplicate helper/proxy. This is generic and does not depend on names such as `HF_WallBakeProxy`.

The Console reports how many duplicates were skipped.

## Lightmap UV Mode

### None

No lightmap UV processing.

### Preserve Source UV2

Keeps source `Mesh.uv2` unchanged. Mainly diagnostic because repeated modules often overlap after combine.

### Preserve And Repack Source UV2

Experimental mode for modular static geometry with authored UV2. It preserves authored chart topology, derives relative chart scale from world-space surface area/source UV area, includes source `Scale In Lightmap` in Edit Mode, and packs charts into one non-overlapping atlas without calling `GenerateSecondaryUVSet`.

Defaults:

```text
Chart Padding = 2 texels
Padding Reference Size = 512
```

### Regenerate UV2

Runs Unity's `Unwrapping.GenerateSecondaryUVSet` on the final mesh. It can split vertices and create different chart boundaries.

## Recommended baked-light workflow

1. Add `MeshFilter`, `MeshRenderer`, and `MeshCombiner` to the root.
2. Start with **Preserve And Repack Source UV2**.
3. Combine.
4. Check source count and skipped duplicate count in Console.
5. Inspect **UV Overlap** and **Texel Validity**.
6. Bake.
7. Use **Restore / Undo Combine** before another comparison.

## Current limitations

`Mesh.CombineMeshes` is not a topology union. This fork still does not weld coincident boundary vertices or remove internal faces. If duplicate filtering is clean but **Texel Validity** remains red exactly on modular boundaries, the next planned step is topology-aware welding/unwrap rather than further UV packing tweaks.

## License

MIT, following the original project.
