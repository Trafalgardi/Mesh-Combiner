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

The repository contains `package.json` in the root, so no `?path=` suffix is required.

Package ID: `com.trafalgardi.mesh-combiner`

Current version: **2.2.1**

## Requirements

- Unity 6.0 (`6000.0`) or newer.
- Git in `PATH` for UPM Git installation.
- Runtime combining requires source meshes with `Read/Write Enabled`.

## Main features

- transform-safe combining without temporarily changing hierarchy transforms;
- single-material and multi-material/submesh preservation;
- automatic UInt16/UInt32 selection;
- optional combined `MeshCollider`;
- aggregated validation logs;
- filtering of disabled renderers;
- filtering of nested exact-duplicate meshes;
- automatic GI setup for lightmapped output;
- exact-state **Restore / Undo Combine** workflow;
- combined mesh asset saving;
- multiple lightmap UV2 workflows.

## Duplicate source filtering

Version 2.2.1 automatically skips a nested child MeshFilter when an ancestor below the MeshCombiner root:

- references the exact same shared Mesh asset; and
- has effectively the same world transform.

The deeper object is treated as an exact duplicate helper/proxy and omitted from the combined mesh. This does not rely on project-specific names.

The combine log reports how many nested exact duplicates were removed.

## Lightmap UV Mode

### None

Does not process lightmap UVs.

### Preserve Source UV2

Keeps source `Mesh.uv2` as-is. This is mainly diagnostic because repeated modules often overlap in UV2 after combine.

### Preserve And Repack Source UV2

Experimental mode for modular static geometry with authored UV2.

It:

- detects existing UV islands through shared UV edges;
- preserves authored chart topology;
- derives relative chart size from world-space surface area and source UV area;
- includes source `Scale In Lightmap` in Edit Mode;
- repacks charts into one non-overlapping UV2 atlas;
- does not call `GenerateSecondaryUVSet`, so it does not intentionally split vertices.

Default settings:

```text
Chart Padding = 2 texels
Padding Reference Size = 512
```

### Regenerate UV2

Runs Unity's `Unwrapping.GenerateSecondaryUVSet` on the final combined mesh. This can split vertices and change chart boundaries.

## Recommended baked-light workflow

1. Put the static meshes below a root object.
2. Add `MeshFilter`, `MeshRenderer`, and `MeshCombiner` to the root.
3. Enable **Create Multi-Material Mesh** when needed.
4. Start with **Preserve And Repack Source UV2**.
5. Click **Combine Meshes**.
6. Check Console for source count and skipped exact duplicates.
7. Inspect **UV Overlap** and **Texel Validity**.
8. Bake lighting.
9. Use **Restore / Undo Combine** before the next comparison.

## Restore / Undo Combine

The custom Inspector captures the source hierarchy state before combine and restores exact `activeSelf` / `MeshRenderer.enabled` values afterward.

Saved `.asset` meshes are kept; transient output meshes are removed when restoring.

## MeshCollider

Enable **Update/Create MeshCollider** to assign the final combined render mesh to a MeshCollider on the destination object.

This addresses the original upstream missing-collider issue. Primitive/custom collision meshes are not merged.

## Runtime combining

Runtime combining requires CPU-readable source meshes. For static level geometry, Editor combine + saved mesh asset is generally the intended workflow.

## Current limitations

`Mesh.CombineMeshes` is not a topology union. This fork still does not:

- weld coincident boundary vertices;
- remove internal coplanar faces;
- close geometric gaps;
- perform Boolean union;
- simplify topology.

If duplicate filtering is clean but **Texel Validity** remains invalid exactly along modular joins, the next planned step is a topology-aware lightmap mode rather than further UV packing tweaks.

## License

MIT, following the original project.
