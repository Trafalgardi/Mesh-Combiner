# Mesh Combiner for Unity

A Unity 6 editor/runtime utility for combining child `MeshFilter` meshes into a single mesh to reduce renderer/draw-call overhead, with a workflow aimed at static geometry and baked lighting.

This repository is a modernized fork of `dawid-t/Mesh-Combiner`.

## Install via Unity Package Manager

### Package Manager window

1. Open **Window -> Package Management -> Package Manager**.
2. Click **+**.
3. Choose **Install package from git URL...**.
4. Paste:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git
```

Until the Unity 6 modernization PR is merged, install the development branch instead:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization
```

The repository contains `package.json` in the root, so no `?path=` suffix is required.

### Packages/manifest.json

```json
{
  "dependencies": {
    "com.trafalgardi.mesh-combiner": "https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization"
  }
}
```

Git must be installed and available in `PATH` for Git-based Unity Package Manager dependencies.

### Updating a Git branch installation

Unity pins the resolved Git commit in `Packages/packages-lock.json`. If Package Manager does not show an Update action, remove the `com.trafalgardi.mesh-combiner` lock entry or remove/re-add the package URL.

## Requirements

- Unity 6.0 (`6000.0`) or newer.
- Git in `PATH` for UPM Git installation.
- Runtime combining requires source meshes with `Read/Write Enabled`.

Package ID: `com.trafalgardi.mesh-combiner`

Current version: **2.1.1**

## Main features

- transform-safe combining without temporarily unparenting/resetting the destination;
- single-material and multi-material/submesh preservation;
- automatic UInt16/UInt32 index selection;
- optional combined `MeshCollider`;
- aggregated validation logs;
- `Contribute GI`, `Receive GI = Lightmaps`, and seam-stitch setup for lightmapped output;
- exact-state **Restore / Undo Combine** workflow in the Editor;
- combined mesh asset saving;
- multiple lightmap UV2 workflows.

## Lightmap UV Mode

The Inspector exposes four modes.

### None

Does not process lightmap UVs.

Use this when the output will not use baked lightmaps or when another system will prepare UV2 later.

### Preserve Source UV2

Keeps source `Mesh.uv2` values unchanged.

This is mainly diagnostic. Modular meshes commonly reuse the same 0..1 UV2 range, so after combining their charts can overlap.

### Preserve And Repack Source UV2

**Recommended test mode for modular static geometry that already has valid UV2.**

Version 2.1.1 detects the existing UV2 charts in every source mesh and packs all charts into one final 0..1 UV2 atlas.

Important properties of this mode:

- it keeps the existing chart topology;
- it does not call `GenerateSecondaryUVSet`;
- it does not intentionally split vertices;
- all charts use **one global scale**, so adjacent modular pieces keep their relative lightmap texel density;
- every chart gets an explicit padding border;
- temporary UV2-modified mesh copies are used during combine, so source assets are not modified.

The first 2.1.0 repack experiment packed whole source meshes into equal cells and scaled each source independently. Real-scene testing showed that this changed texel density exactly at modular boundaries and produced visible baked seams. 2.1.1 replaces that approach with chart-level packing.

#### Chart Padding (texels)

Default: `2`.

Reserves a border around every UV2 chart. If **UV Overlap** visualization still shows red chart neighborhoods, increase this value.

#### Padding Reference Size

Default: `512`.

Padding is converted to normalized UV space using this reference resolution. A 2-texel padding at a 512 reference size is intentionally conservative when the final scene lightmap is 1024.

### Regenerate UV2

Runs Unity's `Unwrapping.GenerateSecondaryUVSet` on the final combined mesh.

This can produce a completely different chart layout and can split vertices. In real modular-wall testing it increased the combined mesh from **10,616** to **14,940** vertices while triangle count stayed at **5,320**, and it changed visible baked seams.

Use this as a fallback when source meshes do not have usable lightmap UVs.

## Recommended baked-light workflow

1. Put the static meshes below a root object.
2. Add `MeshFilter`, `MeshRenderer`, and `MeshCombiner` to the root.
3. Enable **Create Multi-Material Mesh** when required.
4. Start with **Lightmap UV Mode = Preserve And Repack Source UV2**.
5. Keep **Chart Padding = 2** and **Padding Reference Size = 512** for the first test.
6. Click **Combine Meshes**.
7. Check mesh vertex/triangle count.
8. Check Scene View **UV Overlap** and **Texel Validity**.
9. Bake lighting.
10. Use **Restore / Undo Combine** before another A/B comparison.

## Restore / Undo Combine

The custom Inspector records source hierarchy state immediately before combine in Unity `SessionState`.

Restore:

- clears the destination combined mesh;
- clears destination materials;
- clears the generated MeshCollider reference when applicable;
- restores each source GameObject to its exact previous `activeSelf` state;
- restores each source MeshRenderer to its exact previous `enabled` state;
- does not activate helpers/variants that were already disabled;
- removes an unsaved transient output mesh through Unity Undo;
- keeps saved `.asset` meshes.

The restore snapshot is Editor-session scoped. **Destroy Combined Children** is destructive and cannot use exact restore.

## MeshCollider

Enable **Update/Create MeshCollider** to assign the combined render mesh to a MeshCollider on the destination object.

This addresses the original upstream "Missing colider" issue. It does not merge Box/Sphere/Capsule colliders or preserve custom low-poly collision meshes.

## Runtime combining

Runtime combining requires CPU-readable source meshes (`Read/Write Enabled`). For static level geometry it is normally better to combine in the Editor, save the generated mesh asset, and ship the saved result.

## What this tool still does not do

`Mesh.CombineMeshes` is not a topology union operation. This fork currently does not:

- weld coincident geometry vertices;
- remove internal coplanar faces where modular meshes touch;
- close geometry gaps;
- perform Boolean union/intersection;
- simplify mesh topology.

If two cubes touch face-to-face, their hidden internal faces still exist after combine. Topology welding/internal-face removal should remain a separate opt-in step because blindly merging geometry can break hard normals, UV seams, materials, and intentional interior surfaces.

## Basic code use

```csharp
MeshCombiner combiner = GetComponent<MeshCombiner>();
combiner.CreateMultiMaterialMesh = true;
combiner.UvMode = LightmapUvMode.PreserveAndRepackSourceUv2;
combiner.UpdateOrCreateMeshCollider = true;
combiner.CombineMeshes(true);
```

The exact-state Restore snapshot is an Editor Inspector workflow and is intentionally not exposed as a runtime restoration API.

## License

MIT, following the original project.
