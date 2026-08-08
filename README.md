# Mesh Combiner for Unity

A Unity editor/runtime utility for combining child `MeshFilter` meshes into a single mesh to reduce renderer/draw-call overhead.

This fork modernizes the original project for **Unity 6**, with special attention to static geometry and baked lighting.

## Install via Unity Package Manager

### Package Manager window

1. Open **Window -> Package Management -> Package Manager**.
2. Click **+**.
3. Choose **Install package from git URL...**.
4. Paste:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git
```

Until the Unity 6 PR is merged into `master`, install the development branch:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization
```

The repository contains `package.json` at its root, so no `?path=` suffix is required.

### Packages/manifest.json

```json
{
  "dependencies": {
    "com.trafalgardi.mesh-combiner": "https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization"
  }
}
```

Git must be installed and available in `PATH` for Unity Package Manager Git dependencies.

### Updating a Git branch installation

Unity records the resolved Git commit in `Packages/packages-lock.json`. Use Package Manager **Update** when available. If Unity keeps an old branch commit, remove the `com.trafalgardi.mesh-combiner` entry from `Packages/packages-lock.json` and let Unity resolve the dependency again, or remove/re-add the package.

## Requirements

- Unity 6.0 (`6000.0`) or newer.
- Git available in `PATH` for Git UPM installs.
- Runtime combining requires source meshes with `Read/Write Enabled`.

Package ID: `com.trafalgardi.mesh-combiner`

Current package version: **2.1.0**

## Main features

- transform-safe combining without temporarily unparenting/resetting the destination object;
- all submeshes preserved;
- multi-material grouping by actual material;
- automatic UInt16/UInt32 selection;
- aggregated validation logs;
- optional `Update/Create MeshCollider`;
- exact-state **Restore / Undo Combine**;
- generated Mesh asset saving;
- Unity Package Manager support;
- explicit lightmap UV workflows for Unity 6.

## Lightmap UV modes

### Preserve & Repack Source UV2 — recommended for modular static geometry

Use this when source meshes already have valid lightmap UVs.

The combiner keeps each source mesh's existing `Mesh.uv2` charts and only scales/offsets the whole source UV2 layout into a non-overlapping cell in the final combined `0..1` UV space.

This avoids a full `GenerateSecondaryUVSet` pass over the complete building, so it does **not** intentionally split the mesh into a new chart layout.

The mode validates that every source mesh has a non-degenerate UV2 channel. If any source mesh has no usable UV2, combine stops and reports the affected meshes; switch to **Regenerate UV2** or fix the model import settings.

`Repack Padding` is normalized atlas padding around each source-mesh UV rectangle. Default: `0.002`, approximately one pixel at a 512px lightmap.

### Preserve Source UV2

Copies source UV2 without repacking. This is mainly a diagnostic mode. Repeated modular meshes generally occupy the same `0..1` UV area, so the combined result will usually contain UV overlaps.

### Regenerate UV2

Combines geometry first and then runs Unity's `Unwrapping.GenerateSecondaryUVSet` over the complete combined mesh.

This can create new UV charts, split vertices and change baked seams. In the test modular wall scene it increased the combined mesh from **10,616 vertices to 14,940 vertices** while triangle count stayed at **5,320**.

The combiner no longer forces UInt32 just because regeneration is enabled. It uses source index counts as an upper bound: if the regenerated mesh cannot possibly exceed 65,535 vertices, it stays UInt16.

### None

No special lightmap UV processing. Use this for non-lightmapped output or if another pipeline will generate UV2 later.

## Recommended lightmapping workflow

1. Add `MeshCombiner` to the root GameObject with `MeshFilter` and `MeshRenderer`.
2. Put the static source meshes below it.
3. Enable **Create Multi-Material Mesh** when more than one material is used.
4. Set **Lightmap UV Mode -> Preserve And Repack Source UV2** for imported modular assets that already contain UV2.
5. Keep **Destroy Combined Children** disabled.
6. Click **Combine Meshes**.
7. Check the generated mesh vertex/triangle counts.
8. In Scene View inspect **Baked GI -> UV Overlap** and **Texel Validity**.
9. Bake lighting and compare seams/lightmap size against the uncombined source hierarchy.
10. Click **Restore / Undo Combine** before the next A/B test.

Any lightmap UV mode other than `None` marks the destination as `Contribute GI`, sets `Receive GI = Lightmaps`, and enables lightmap seam stitching in Edit Mode.

## Restore / Undo Combine

Immediately before combine, the custom Editor records the active state of child MeshFilter GameObjects and the enabled state of their MeshRenderers in Unity `SessionState`.

**Restore / Undo Combine**:

- clears the destination combined mesh and materials;
- clears the generated MeshCollider mesh reference when applicable;
- restores only the GameObjects/Renderers changed by the combine operation to their exact pre-combine state;
- keeps source helpers/variants that were already inactive before combine inactive;
- removes unsaved transient combined Mesh objects through Unity Undo;
- keeps a combined Mesh already saved as a `.asset`.

**Destroy Combined Children** is destructive and cannot use exact restore.

## Missing collider issue

The original implementation only replaced `MeshFilter.sharedMesh`. It did not update/create a `MeshCollider`, which caused upstream issue #2 ("Missing colider").

Enable **Update/Create MeshCollider** to assign the combined render mesh to a destination `MeshCollider`.

This does not merge Box/Capsule/Sphere colliders or preserve custom low-poly child collision meshes.

## Runtime combining

Runtime combining requires source meshes to be CPU-readable (`Read/Write Enabled`). For static level geometry it is usually better to combine in the Editor, save the generated mesh asset, and ship the saved result.

In Edit Mode, non-readable source meshes are accepted and reported in one informational log instead of one warning per source mesh.

## Geometry cleanup: not implemented yet

`Mesh.CombineMeshes` combines vertex/index data. It is **not** a Boolean union or topology optimizer.

This version does not yet:

- weld coincident vertices;
- remove duplicate/internal faces where meshes touch;
- close gaps;
- repair normals;
- perform Boolean union/intersection;
- simplify topology.

If two cubes touch face-to-face, both internal faces still exist after combine.

## Basic code use

```csharp
MeshCombiner combiner = GetComponent<MeshCombiner>();
combiner.CreateMultiMaterialMesh = true;
combiner.UvMode = LightmapUvMode.PreserveAndRepackSourceUv2;
combiner.UpdateOrCreateMeshCollider = true;
combiner.CombineMeshes(true);
```

The exact source-state restore is an Editor inspector workflow because it relies on the pre-combine Editor snapshot.

## Original project

This repository is a fork of `dawid-t/Mesh-Combiner` and remains under the MIT License.
