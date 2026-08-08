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

The deeper object is treated as a duplicate helper/proxy. This does not depend on names such as `HF_WallBakeProxy`.

## Lightmap UV Mode

- `None`
- `Preserve Source UV2`
- `Preserve And Repack Source UV2`
- `Regenerate UV2`

`Preserve And Repack Source UV2` preserves authored chart topology, derives relative chart scale from world-space surface area/source UV area, includes source `Scale In Lightmap` in Edit Mode, and repacks charts into one non-overlapping UV2 atlas.

Defaults:

```text
Chart Padding = 2 texels
Padding Reference Size = 512
```

## Restore / Undo Combine

The custom Inspector captures exact source `activeSelf` and `MeshRenderer.enabled` state before combine and restores it afterward.

## Current topology limitation

`Mesh.CombineMeshes` does not weld modular boundaries. If duplicate filtering is clean but **Texel Validity** remains invalid exactly along joins, the next step is topology-aware welding/lightmap unwrap rather than further UV packing changes.

## License

MIT, following the original project.
