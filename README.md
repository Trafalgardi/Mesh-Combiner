# Mesh Combiner for Unity

A small Unity editor/runtime utility for combining child `MeshFilter` meshes into a single mesh to reduce renderer/draw-call overhead.

This fork modernizes the original project for **Unity 6** workflows, with special attention to static geometry and baked lighting.

## Install via Unity Package Manager

### Package Manager window

1. Open **Window -> Package Management -> Package Manager**.
2. Click the **+** button.
3. Choose **Install package from git URL...**.
4. Paste:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git
```

5. Click **Install**.

The repository contains `package.json` at its root, so no `?path=` suffix is required.

### Install the current Unity 6 development branch

Until the Unity 6 modernization PR is merged into `master`, install this branch directly:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization
```

### Add manually to Packages/manifest.json

You can also add the package directly to your Unity project's `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.trafalgardi.mesh-combiner": "https://github.com/Trafalgardi/Mesh-Combiner.git"
  }
}
```

For the current development branch:

```json
{
  "dependencies": {
    "com.trafalgardi.mesh-combiner": "https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization"
  }
}
```

Git must be installed and available in `PATH` for Unity Package Manager Git dependencies.

### Updating a Git branch installation

Unity records the resolved Git commit in `Packages/packages-lock.json`. If the package was already installed from `#agent/unity6-modernization` before a new fix was pushed, use the Package Manager **Update** action when available, or remove the `com.trafalgardi.mesh-combiner` entry from `Packages/packages-lock.json` and let Unity resolve packages again.

If Unity still shows stale package contents, remove the package and add the same Git URL again.

## Requirements

- Unity 6.0 (`6000.0`) or newer.
- Git available in `PATH` when installing from a Git URL.
- Runtime combining requires source meshes with `Read/Write Enabled`.

Package name: `com.trafalgardi.mesh-combiner`

Current package version: `2.0.1`

## Unity 6 modernization

The Unity 6 version adds:

- transform-safe combining without temporarily unparenting/resetting the destination object;
- validation for missing meshes/renderers and runtime `Read/Write` requirements;
- aggregated validation logs instead of one warning per source mesh;
- correct preservation of all submeshes;
- multi-material grouping by actual material;
- 32-bit index buffers when required;
- lightmap UV2 generation with success/failure checking;
- 32-bit indices before UV2 unwrap so UV chart splitting cannot hit the 65,535-vertex limit;
- automatic `Contribute GI`, `Receive GI = Lightmaps`, and lightmap seam stitching when UV2 generation is enabled;
- optional `Update/Create MeshCollider` support;
- Editor Undo support;
- a **Restore / Undo Combine** button for fast Combine -> Bake -> Restore iteration;
- safer asset saving with unique generated mesh paths;
- source objects are deactivated by default instead of destroyed.

## Restore / Undo Combine

Use **Restore / Undo Combine** in the `MeshCombiner` inspector to return to the source hierarchy after testing a combined mesh.

The restore action:

- clears the destination `MeshFilter.sharedMesh`;
- clears the destination renderer materials;
- clears the combined `MeshCollider` mesh reference when it points to the combined mesh;
- reactivates inactive child GameObjects;
- re-enables disabled child `MeshRenderer` components;
- deletes an unsaved transient generated Mesh through Unity Undo;
- keeps a generated Mesh that was already saved as a `.asset` in the Project.

This restore command cannot reconstruct child objects after **Destroy Combined Children** was used. Keep destructive mode disabled for normal iteration.

## Missing collider issue

The original implementation only replaced `MeshFilter.sharedMesh`. It did not update or create a `MeshCollider`, which is the cause of upstream issue #2 ("Missing colider").

Enable **Update/Create MeshCollider** to assign the final combined render mesh to a `MeshCollider` on the destination GameObject.

Important: this uses the **combined render mesh** for collision. It does not merge Box/Capsule/Sphere colliders or preserve a custom low-poly collision setup from child objects.

## Lightmapping workflow

Recommended workflow for static scene geometry:

1. Add `MeshCombiner` to an empty/root GameObject with `MeshFilter` and `MeshRenderer`.
2. Put the source meshes under that root.
3. Enable **Create Multi-Material Mesh** if the hierarchy uses more than one material.
4. Enable **Generate Lightmap UV2** when you want a fresh combined UV2 layout.
5. Click **Combine Meshes**.
6. Save the generated combined mesh asset if the result is useful.
7. Bake lighting.
8. Use **Restore / Undo Combine** to return to the source hierarchy for the next comparison.

When **Generate Lightmap UV2** is enabled in Edit Mode, the destination object is also marked `Contribute GI`, its renderer is set to receive GI from lightmaps, and lightmap seam stitching is enabled.

Unity's UV unwrapper can create additional vertices while splitting UV charts. The fork switches the generated mesh to a 32-bit index buffer before unwrapping to prevent UV2 generation from failing at the 16-bit vertex limit.

## Runtime combining

Runtime combining requires source meshes to be CPU-readable (`Read/Write Enabled`). This costs memory, so for static level geometry it is usually better to combine in the Editor, save the generated mesh asset, then ship the saved result.

In Edit Mode, non-readable source meshes are accepted and reported in a single informational log message instead of one warning per mesh.

## Geometry cleanup: what this tool does NOT do

`Mesh.CombineMeshes` combines vertex/index data into one mesh. It is **not** a Boolean union or topology optimizer.

This version currently does **not**:

- weld coincident vertices;
- remove duplicate/internal faces where two meshes touch;
- close gaps;
- repair normals;
- perform Boolean union/intersection;
- simplify mesh topology.

If two cubes touch face-to-face, the two internal faces still exist after combine. A dedicated static-geometry cleanup pass should be implemented and tested separately because blindly welding vertices or deleting coplanar faces can break hard normals, UV seams, intentional interior surfaces, and modular geometry.

## Basic code use

```csharp
MeshCombiner combiner = GetComponent<MeshCombiner>();
combiner.CreateMultiMaterialMesh = true;
combiner.UpdateOrCreateMeshCollider = true;
combiner.CombineMeshes(true);
```

To restore the source hierarchy from code:

```csharp
combiner.RestoreCombinedState(true);
```

## Original project

This repository is a fork of `dawid-t/Mesh-Combiner`.

The original project was released under the MIT License.
