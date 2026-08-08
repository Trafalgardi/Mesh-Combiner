# Mesh Combiner for Unity 6

A Unity 6 mesh-combining utility focused on reducing renderer/draw-call overhead and preparing static geometry for baked-lighting workflows.

This repository is a maintained fork of `dawid-t/Mesh-Combiner`. The fork adds Unity 6 compatibility work, UPM installation, safer Editor workflows, multi-material handling, validation, collider output, several lightmap UV2 workflows, conservative geometry cleanup, duplicate-source filtering, and crash-persistent recovery.

## Installation

### Unity Package Manager — Git URL

Open **Window -> Package Management -> Package Manager**, click **+**, choose **Install package from git URL...**, and paste:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization
```

After the modernization branch is merged, the default branch can be installed with:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git
```

Package ID:

```text
com.trafalgardi.mesh-combiner
```

Current package version: **2.4.2**

## Requirements

- Unity 6.0 (`6000.0`) or newer.
- Git available in `PATH` for Git-URL UPM installation.
- Runtime combining requires source meshes with **Read/Write Enabled**.
- Editor combining can use imported meshes with Read/Write disabled.

## Quick start

1. Create/select a parent GameObject.
2. Add `MeshFilter`, `MeshRenderer`, and `MeshCombiner`.
3. Put source meshes below that GameObject.
4. Configure combine/material/lightmap/output options.
5. Click **Combine Meshes**.
6. Inspect the generated mesh and lighting debug views.
7. Optionally save the generated mesh as a `.asset`.

By default the source hierarchy is deactivated rather than destroyed so the operation remains recoverable.

## Main features

- transform-safe destination-local mesh combining;
- single-material and multi-material output;
- submesh/material preservation;
- UInt16/UInt32 index-buffer handling;
- optional combined `MeshCollider`;
- source validation and aggregated logs;
- disabled-renderer filtering;
- generic nested exact-duplicate helper/proxy filtering;
- multiple lightmap UV2 modes;
- experimental coplanar stitched UV2 generation;
- closed-mesh T-junction / partial-edge-aware coplanar stitching;
- optional exact opposing-face cleanup;
- automatic baked-GI setup;
- Editor Undo support;
- crash-persistent exact restore;
- emergency source recovery without a snapshot;
- generated mesh asset saving;
- UPM package layout and asmdefs.

## Source selection

The combiner searches child `MeshFilter` components below the target root.

It automatically skips:

- the destination/root `MeshFilter`;
- entries in **Mesh Filters To Skip**;
- missing meshes;
- missing `MeshRenderer` components;
- disabled `MeshRenderer` components;
- nested exact duplicates.

### Nested exact-duplicate filtering

A deeper child is skipped when an ancestor below the combiner root references the same `sharedMesh` and has effectively the same world transform. This is useful for helper/bake-proxy hierarchies and does not depend on object names.

## Read/Write

In Edit Mode, the package can combine imported meshes whose Read/Write importer flag is disabled.

Runtime combining needs CPU-side mesh data, so source meshes combined while the application is running must be readable.

## Materials and submeshes

### Create Multi-Material Mesh = Off

Use this only when all source submeshes use the same material. The combiner validates the source layout rather than silently flattening incompatible materials.

### Create Multi-Material Mesh = On

Source submeshes are grouped by shared `Material` reference. Repeated references to the same material do not create unnecessary duplicate material slots.

Combining multiple materials still produces multiple material/submesh draws. Mesh combining reduces renderer/object overhead; it does not turn unrelated materials into one draw call.

## Lightmap UV modes

Baked lightmaps use `Mesh.uv2`.

### None

Leaves lightmap UV processing to another pipeline.

### Preserve Source UV2

Copies source UV2 unchanged. This is mainly diagnostic: independently authored modular meshes often occupy the same `0..1` UV space and therefore overlap after combining.

### Preserve And Repack Source UV2

Reuses authored UV2 charts and repacks them into one non-overlapping atlas. Relative chart size is based on world-space surface area and source UV area, with source **Scale In Lightmap** taken into account in Edit Mode.

Different charts may show different checker phase/offset in Unity's Baked Lightmap visualization. Matching checker **size/density** is the important part.

### Regenerate UV2

Combines first and then calls Unity's secondary UV unwrapper. This is useful when source UV2 is unusable, but Unity can split vertices and move baked seams while generating new charts.

## Experimental: Coplanar Stitched UV2

Open:

```text
Tools -> Mesh Combiner -> Coplanar Stitched UV2...
```

This tool operates on an already combined mesh. It targets baked seams caused by modular pieces that form one visually continuous planar surface but still have disconnected lightmap charts.

The tool:

- reads triangle geometry in world space;
- joins triangles across exact matching geometric edges when their normals are coplanar;
- also joins collinear partially-overlapping seam edges;
- supports T-junctions where one long edge touches one or more shorter edges;
- builds connected coplanar surfaces;
- planar-projects each surface into one continuous UV2 chart;
- packs the charts into one atlas;
- writes only UV2;
- does not modify positions, triangles, UV0, normals, tangents, or materials;
- clones the current mesh before writing UV2;
- aborts rather than silently corrupting UV2 when one vertex index is shared by incompatible hard-angle charts.

### Closed meshes and seam candidates

A visible modular boundary is not necessarily an **open mesh boundary**.

For a closed wall module, the front-face perimeter edge is normally shared by two triangles from different surfaces, for example:

```text
front face
    |
    | shared geometric edge
    |
side/end face
```

So `owners.Count == 1` is not a valid generic test for modular seams.

Since **2.4.2**, the stitcher classifies seam candidates as follows:

- an edge with one owner is a candidate;
- an edge with exactly two coplanar owners is treated as an ordinary interior triangulation edge and skipped;
- hard edges with different owner normals are candidates;
- non-manifold / multi-owner edge groups are candidates.

Partial/T-junction matching then compares compatible coplanar candidate records and stitches only segments that are collinear and overlap in world space.

This keeps the logic generic for closed modular walls, architecture, props, cutouts, doors, windows, arches, and other segmented meshes without relying on object names.

### Why T-junction stitching exists

Two flush modules can describe the same geometric boundary with different segmentation:

```text
neighbor edge
|-------------------------|

cutout/opening module
|--------|-----|----------|
```

The surface is continuous, but there is no identical endpoint-to-endpoint edge pair. The partial matcher allows the long edge to connect to the shorter collinear segments.

Matching is conservative:

- owning triangle normals must pass **Coplanar Angle**;
- edge lines must coincide within **Edge Position Tolerance**;
- the segments must overlap by a non-zero length;
- partial-edge direction matching is capped at 1 degree even if Coplanar Angle is higher.

The result log reports:

- exact shared-edge connections;
- partial/T-junction connections;
- total stitched connections;
- seam-candidate edge records inspected;
- resulting chart count;
- packing scale.

This feature remains experimental and separate from the normal `Lightmap UV Mode` enum until it has wider production validation.

### Debug-view note

The stitcher deliberately does **not** weld render vertices. Two modules can remain topologically disconnected while receiving continuous UV2 coordinates along a geometric seam.

Unity's **UV Overlap** or **Texel Validity** debug views can therefore flag boundary texels around complex cutouts/jambs even when the final baked surface improves. Use the final baked result together with the debug views; do not use one debug mode as the sole pass/fail criterion.

## Unity lightmap diagnostics

Useful Scene View debug modes:

- **Baked Lightmap** — inspect effective texel density and chart continuity;
- **UV Overlap** — detect overlapping sampling regions;
- **Texel Validity** — identify texels invalidated by backface/geometry conditions.

A clean UV Overlap and Texel Validity view does not by itself guarantee seamless lighting. A visible seam can still exist where one visually flat wall is split into separate UV2 charts.

## Geometry cleanup

### Remove Exact Opposing Faces

Optional and disabled by default.

It removes pairs of coincident triangles when the same three destination-local positions match within **Position Tolerance** and the faces point in opposite directions.

The cleanup only changes triangle index buffers. It does not weld vertices or rewrite normals, tangents, UVs, colors, or other vertex attributes.

### Important limitation

This is not a Boolean union. It does not remove:

- partially overlapping coplanar surfaces;
- coincident surfaces with different triangulation/diagonals;
- T-junction polygons merely because they visually touch;
- arbitrary intersecting geometry.

A zero removal count can therefore be correct even when two closed modules appear to sit perfectly flush.

## Restore and recovery

### Restore Exact State

Before Combine, the Editor stores source `activeSelf` and `MeshRenderer.enabled` state using stable Unity `GlobalObjectId` references. The snapshot is persisted before hierarchy mutation and survives domain reloads / Editor restarts when the scene objects still exist.

Restore Exact State clears combined output and restores the recorded source state.

### Force Recover Sources (No Snapshot)

Emergency fallback when a usable exact snapshot is unavailable. It clears destination mesh/material output, clears a matching combined `MeshCollider`, activates descendant MeshFilter GameObjects, and enables descendant MeshRenderers.

Because no exact snapshot is available, intentionally disabled helpers or variants can also be enabled.

### Destroy Combined Children

Destructive mode. Exact restore and force recovery cannot reconstruct objects that were actually destroyed. Prefer source deactivation for normal Editor workflows.

## MeshCollider output

Enable **Update/Create MeshCollider** to assign the generated render mesh to a destination `MeshCollider`.

The package does not merge arbitrary `BoxCollider`, `CapsuleCollider`, `SphereCollider`, or dedicated low-poly collision hierarchies into a separate optimized collision mesh.

## Saving generated meshes

Use **Save Combined Mesh** to save a generated mesh as a `.asset` below `Assets`.

Saved mesh assets are detached, not deleted, by Restore/Recovery.

## Optimization notes

Manual combining can reduce renderer count, per-renderer CPU overhead, and draw submission overhead when compatible geometry/materials are grouped.

Trade-offs:

- pieces can no longer be culled independently;
- combining an entire level into one mesh can make culling worse;
- multiple materials still require multiple material/submesh draws;
- modern URP/HDRP projects should still evaluate SRP Batcher and other current rendering optimizations.

Prefer bounded spatial clusters such as rooms, building sections, or static prop groups instead of blindly combining a whole scene.

## Current topology limitations

`Mesh.CombineMeshes` concatenates geometry. It is not a general Boolean/topology optimizer.

The package still does not provide a general-purpose solution for arbitrary Boolean union, partial coplanar face removal, polygon matching across different triangulations, destructive weld-by-position, general non-manifold repair, automatic removal of all hidden/internal geometry, or unused-vertex compaction after exact-face cleanup.

The coplanar UV2 stitcher can bridge exact seams and partial/T-junction seam segments for **lightmap UV continuity** without modifying render topology.

## License and upstream

MIT license, following the original project.

Original project: `dawid-t/Mesh-Combiner`.
