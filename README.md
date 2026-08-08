# Mesh Combiner for Unity 6

A Unity 6 mesh-combining utility focused on reducing renderer/draw-call overhead and preparing static geometry for baked-lighting workflows.

This repository is a maintained fork of `dawid-t/Mesh-Combiner`. The fork adds Unity 6 compatibility work, UPM installation, safer Editor workflows, multi-material handling, validation, collider output, lightmap UV2 tools, conservative geometry cleanup, duplicate-source filtering, and crash-persistent recovery.

Current package version: **2.4.0**

## Installation

### Unity Package Manager — Git URL

Open **Window -> Package Management -> Package Manager**, click **+**, choose **Install package from git URL...**, and use:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization
```

After this development branch is merged, install the default branch with:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git
```

Package ID:

```text
com.trafalgardi.mesh-combiner
```

Requirements:

- Unity 6.0 (`6000.0`) or newer;
- Git available in `PATH` for Git-based UPM installation;
- runtime combining requires source meshes with **Read/Write Enabled**;
- Editor combining can use imported meshes with Read/Write disabled.

## Quick start

1. Create/select a parent GameObject.
2. Add `MeshFilter`, `MeshRenderer`, and `MeshCombiner`.
3. Put source meshes below this object.
4. Configure material, cleanup, lightmap, collider, and output options.
5. Click **Combine Meshes**.
6. Inspect the generated mesh and baked-lighting debug views.
7. Optionally click **Save Combined Mesh** to save the transient mesh as an asset.

By default, source GameObjects are deactivated instead of destroyed so the operation remains recoverable.

## Main features

- transform-safe destination-local combining;
- single-material and multi-material output;
- submesh preservation and shared-material grouping;
- UInt16/UInt32 index-buffer selection;
- aggregated source validation;
- disabled-renderer filtering;
- generic nested exact-duplicate/helper-proxy filtering;
- optional exact opposing-face cleanup;
- several lightmap UV2 workflows;
- experimental coplanar-stitched UV2 generation for modular seams;
- automatic baked-GI renderer setup;
- optional combined `MeshCollider`;
- crash-persistent exact restore state;
- emergency source recovery without a snapshot;
- Editor Undo integration;
- generated mesh asset saving;
- UPM layout with Runtime and Editor asmdefs.

## Source selection and validation

The combiner searches child `MeshFilter` components below the target object.

Automatically skipped inputs include:

- the destination/root `MeshFilter`;
- entries listed in **Mesh Filters To Skip**;
- objects without a mesh;
- objects without a `MeshRenderer`;
- objects whose `MeshRenderer.enabled` is false;
- nested exact duplicates.

### Nested exact-duplicate filtering

A deeper child mesh is skipped when an ancestor below the combiner root:

- references the same `sharedMesh`;
- has effectively the same world transform.

This is generic and does not depend on names such as `Proxy`, `BakeProxy`, or any project-specific convention.

### Read/Write

Editor combining can read imported meshes with Read/Write disabled. These are reported in one aggregated informational log.

Runtime combining requires CPU-readable source mesh data, so Read/Write must be enabled for runtime-generated combines.

## Materials and submeshes

### Create Multi-Material Mesh = Off

Use this when every source submesh uses the same material. The combiner validates the material layout and refuses to silently flatten unrelated materials.

### Create Multi-Material Mesh = On

Source submeshes are grouped by shared `Material` reference. Reusing the same material does not create duplicate output material slots.

Combining does not make unrelated materials render in one draw call: multiple materials still require multiple submesh/material draws.

## Geometry Cleanup

### Remove Exact Opposing Faces

Optional and **off by default**.

After combining, the cleanup can remove two triangles when they:

- use the same three destination-local positions within **Position Tolerance**;
- occupy the same exact geometric triangle;
- face in opposite directions.

Default:

```text
Remove Exact Opposing Faces = Off
Position Tolerance = 0.0001
```

The operation removes triangle indices only. It does **not** weld/compact vertices or rewrite normals, tangents, UV0, UV2, colors, skinning data, or other vertex attributes.

This is intentionally conservative. Intentionally double-sided geometry can also contain exact opposite-wound triangle pairs, which is why the option is not enabled automatically.

This is **not** a Boolean union. It does not remove:

- partial coplanar overlaps;
- coincident surfaces using different triangulation/diagonals;
- T-junction overlaps;
- intersecting geometry that does not share the same three triangle positions.

## Lightmap UV Mode

Baked lightmaps use `Mesh.uv2`, shown as the second UV channel in Unity's mesh inspection UI.

### None

No UV2 processing.

Use for non-lightmapped output or when another tool will generate UV2 afterward.

### Preserve Source UV2

Keeps source UV2 unchanged.

Useful mainly for diagnostics or when all source meshes were already authored into one shared, non-overlapping UV2 space. Repeated modular assets normally overlap in this mode.

### Preserve And Repack Source UV2

Preserves authored UV2 chart topology while repacking charts into one atlas.

The combiner:

1. reads source UV2;
2. discovers UV charts through shared UV edges;
3. measures world-space surface area and source UV area;
4. includes source **Scale In Lightmap** in Edit Mode;
5. derives relative world-space texel density;
6. packs charts into one non-overlapping UV2 atlas;
7. avoids intentionally creating new vertices.

Default packing settings:

```text
Chart Padding = 2 texels
Padding Reference Size = 512
```

Different charts can show different checker phase/offset in Unity's Baked Lightmap visualization. Matching checker **size** is the relevant texel-density diagnostic.

### Regenerate UV2

Combines geometry first, applies enabled exact-face cleanup, then calls Unity's secondary UV unwrapper.

This can split vertices and can move baked seams because the combined geometry is fully unwrapped again.

## Experimental: Coplanar Stitched UV2

Version 2.4.0 adds an experimental seam-focused generator under:

```text
Tools -> Mesh Combiner -> Coplanar Stitched UV2...
```

This tool is intentionally separate from the main `Lightmap UV Mode` until it has been validated on more real-world geometry.

### What it does

Given an already combined mesh, it:

1. reads triangle geometry in world space;
2. matches triangle edges by world-space endpoint positions;
3. connects neighboring triangles only when their normals are within **Coplanar Angle**;
4. allows disconnected source modules to join the same generated lightmap chart when their geometric boundary edge matches;
5. planar-projects each connected coplanar surface;
6. packs all generated surfaces with one global world-space scale;
7. writes the result only to UV2.

The goal is to make a flat modular wall behave like one continuous lightmap chart even though its render geometry still contains separate vertex/index islands from the original modules.

### What it does not change

The experimental tool does **not** modify:

- triangle topology;
- vertex positions;
- UV0;
- normals;
- tangents;
- materials.

It clones the current combined mesh before replacing UV2, so imported/saved mesh assets are not edited in place. If a `MeshCollider` references the old combined mesh, the tool updates it to the clone.

### Safety behavior

If one vertex index is shared between multiple generated hard-angle charts and correct output would require vertex splitting, generation aborts instead of silently corrupting UV2.

Current experimental implementation requires triangle topology.

Suggested starting settings:

```text
Edge Position Tolerance = 0.0001
Coplanar Angle = 1 degree
Chart Padding = 2 texels
Padding Reference Size = 512
```

### Suggested workflow

For seam diagnosis:

1. Combine with `Lightmap UV Mode = None` or create a clean combined mesh first.
2. Select the combined output GameObject.
3. Open **Tools -> Mesh Combiner -> Coplanar Stitched UV2...**.
4. Generate stitched UV2.
5. Inspect **Baked Lightmap**, **UV Overlap**, and **Texel Validity**.
6. Bake again and compare the same modular boundaries.

If the stitched checker becomes continuous across a flat boundary and the baked seam disappears, the coplanar mode is a good candidate to become a normal built-in UV mode.

## Baked-lighting setup

For normal combiner lightmap modes other than `None`, the Editor workflow configures the destination for baked GI:

- `Contribute GI` enabled;
- `Receive GI = Lightmaps`;
- `Stitch Seams` enabled.

The Coplanar Stitched UV2 Editor tool also ensures these settings on its target.

Useful Unity Scene View diagnostics:

- **Baked Lightmap** — inspect texel density and chart continuity;
- **UV Overlap** — detect overlapping sampling neighborhoods;
- **Texel Validity** — detect invalid lightmap texels/backface conditions.

A clean UV Overlap/Validity result does not automatically guarantee seamless lighting: disconnected chart boundaries, normal discontinuities, geometry intersections, and lightmapper filtering can still produce visible seams.

## Restore and recovery

### Restore Exact State

Before Combine, the Inspector saves source `activeSelf` and `MeshRenderer.enabled` state.

The snapshot is persisted before hierarchy mutation and stores both current InstanceIDs and stable Unity `GlobalObjectId` values. It survives domain reloads and Editor restarts/crashes while referenced scene objects still exist.

**Restore Exact State**:

- clears the destination combined mesh;
- clears destination materials;
- clears the matching combined `MeshCollider` reference;
- restores source GameObject active states;
- restores source MeshRenderer enabled states;
- destroys only an unsaved/transient generated mesh;
- does not delete a combined mesh asset already saved in the project.

### Force Recover Sources (No Snapshot)

Emergency fallback for stale/crashed scenes without a usable exact snapshot.

It clears combined output and aggressively enables all descendant mesh-source GameObjects and MeshRenderers. This can also re-enable helpers/variants that were intentionally disabled before Combine.

### Destroy Combined Children

Destructive mode. Exact Restore and Force Recover cannot reconstruct GameObjects that were actually destroyed. Prefer deactivation for normal Editor workflows.

## MeshCollider output

Enable **Update/Create MeshCollider** to assign the generated combined render mesh to a `MeshCollider` on the destination GameObject.

This does not merge dedicated primitive/custom collision setups. A detailed render mesh can also be more expensive than a dedicated low-poly collision mesh.

## Saving combined meshes

Use **Save Combined Mesh** to write the transient output as a `.asset` below `Assets`.

Example folder value:

```text
Generated/CombinedMeshes
```

Saved meshes are detached, not deleted, by Restore/Recovery.

## Optimization notes

Manual mesh combining can reduce renderer count and CPU submission overhead, but it changes culling granularity.

Trade-offs:

- combined pieces can no longer be culled as independent renderers;
- combining an entire large level can make culling worse;
- multiple materials still require multiple submesh/material draws;
- exact-face cleanup does not currently compact unused vertices;
- modern URP/HDRP projects should still profile SRP Batcher and other current rendering paths.

Prefer logical spatial clusters such as a room, building section, or static prop group instead of blindly combining a whole scene.

## Current topology limitations

`Mesh.CombineMeshes` concatenates geometry; it is not a Boolean union or general topology optimizer.

The package still does not perform general-purpose:

- boundary vertex welding;
- partial coplanar overlap removal;
- polygon matching across different triangulation;
- T-junction resolution;
- non-manifold repair;
- arbitrary duplicate-triangle removal;
- unused-vertex compaction after exact-face cleanup.

The experimental Coplanar Stitched UV2 tool can bridge matching coplanar **lightmap** edges without welding render topology. This is intentionally separate from future geometry-welding/Boolean cleanup.

## License and upstream

MIT license, following the original project.

Original project: `dawid-t/Mesh-Combiner`.
