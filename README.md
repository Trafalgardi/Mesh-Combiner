# Mesh Combiner for Unity

A small Unity editor/runtime utility for combining child `MeshFilter` meshes into a single mesh to reduce renderer/draw-call overhead.

This fork modernizes the original project for **Unity 6** workflows, with special attention to static geometry and baked lighting.

## Unity 6 modernization

The `agent/unity6-modernization` branch adds:

- transform-safe combining without temporarily unparenting/resetting the destination object;
- validation for missing meshes/renderers and runtime `Read/Write` requirements;
- correct preservation of all submeshes;
- multi-material grouping by actual material;
- 32-bit index buffers when required;
- lightmap UV2 generation with success/failure checking;
- 32-bit indices before UV2 unwrap so UV chart splitting cannot hit the 65,535-vertex limit;
- automatic `Contribute GI`, `Receive GI = Lightmaps`, and lightmap seam stitching when UV2 generation is enabled;
- optional `Update/Create MeshCollider` support;
- Editor Undo support;
- safer asset saving with unique generated mesh paths;
- source objects are deactivated by default instead of destroyed.

## Missing collider issue

The original implementation only replaced `MeshFilter.sharedMesh`. It did not update or create a `MeshCollider`, which is the cause of upstream issue #2 ("Missing colider").

Enable **Update/Create MeshCollider** to assign the final combined render mesh to a `MeshCollider` on the destination GameObject.

Important: this uses the **combined render mesh** for collision. It does not merge Box/Capsule/Sphere colliders or preserve a custom low-poly collision setup from child objects.

## Lightmapping workflow

Recommended workflow for static scene geometry:

1. Add `MeshCombiner` to an empty/root GameObject with `MeshFilter` and `MeshRenderer`.
2. Put the source meshes under that root.
3. Enable **Create Multi-Material Mesh** if the hierarchy uses more than one material.
4. Enable **Generate Lightmap UV2**.
5. Click **Combine Meshes**.
6. Save the generated combined mesh asset.
7. Bake lighting.

When **Generate Lightmap UV2** is enabled in Edit Mode, the destination object is also marked `Contribute GI`, its renderer is set to receive GI from lightmaps, and lightmap seam stitching is enabled.

Unity's UV unwrapper can create additional vertices while splitting UV charts. The fork switches the generated mesh to a 32-bit index buffer before unwrapping to prevent UV2 generation from failing at the 16-bit vertex limit.

## Runtime combining

Runtime combining requires source meshes to be CPU-readable (`Read/Write Enabled`). This costs memory, so for static level geometry it is usually better to combine in the Editor, save the generated mesh asset, then ship the saved result.

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

## Installation

Copy the repository folders into your Unity project's `Assets` folder, preserving the `Editor` folder name.

## Basic code use

```csharp
MeshCombiner combiner = GetComponent<MeshCombiner>();
combiner.CreateMultiMaterialMesh = true;
combiner.UpdateOrCreateMeshCollider = true;
combiner.CombineMeshes(true);
```

## Original project

This repository is a fork of `dawid-t/Mesh-Combiner`.

The original project was released under the MIT License.
