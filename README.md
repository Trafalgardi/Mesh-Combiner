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

Current package version: **2.4.1**

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
- T-junction / partial-edge-aware coplanar stitching;
- optional exact opposing-face cleanup;
- automatic baked-GI setup;
- Editor Undo support;
- crash-persistent exact restore;
- emergency source recovery without a snapshot;
- generated mesh asset saving;
- UPM package layout and asmdefs.

## Experimental: Coplanar Stitched UV2

Open:

```text
Tools -> Mesh Combiner -> Coplanar Stitched UV2...
```

The tool joins coplanar triangles through exact shared geometric edges and collinear partially-overlapping boundary edges, including T-junctions produced by doors, windows, arches, cutouts, and other modular openings. It writes only UV2 and does not modify render geometry, UV0, normals, tangents, materials, or triangle indices.

Version 2.4.1 logs exact edge connections, partial/T-junction connections, total connections, boundary edges inspected, chart count, and packing scale.

## Restore and recovery

**Restore Exact State** uses crash-persistent `GlobalObjectId` snapshots. **Force Recover Sources (No Snapshot)** clears combined output and re-enables descendant mesh sources when no exact snapshot is available.

## Geometry cleanup

**Remove Exact Opposing Faces** is optional and conservative. It removes only exact coincident opposite-wound triangle pairs; it is not a Boolean union and does not remove differently triangulated or partially overlapping coplanar surfaces.

## Materials, lightmaps, collider output, and optimization

The package supports single/multi-material output, submeshes, optional combined MeshCollider, authored UV2 repacking, Unity UV2 regeneration, baked-GI setup, and generated mesh asset saving. Combining reduces renderer/object overhead but reduces independent culling granularity; prefer bounded spatial clusters instead of combining an entire level blindly.

## License and upstream

MIT license, following the original project.

Original project: `dawid-t/Mesh-Combiner`.
