# Mesh Combiner for Unity 6

A maintained Unity 6 fork of `dawid-t/Mesh-Combiner` focused on two practical goals:

- reduce renderer / draw-submission overhead by combining static meshes;
- prepare modular geometry for reliable baked-lighting workflows.

The fork keeps the original idea Editor-friendly, but adds UPM packaging, safer recovery, material/submesh handling, preflight analysis, MeshCollider output, duplicate filtering, geometry cleanup, and several UV2 strategies including the seam-aware **Coplanar Stitched UV2** mode.

## Installation

### Stable — `master`

Open **Window -> Package Management -> Package Manager**, click **+**, choose **Install package from git URL...**, and paste:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git
```

### Development — `dev`

For active development/testing, pin UPM to the permanent development branch:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git#dev
```

`dev` is intentionally the moving development channel. Features are developed and validated there first, then periodically promoted to `master` as stable releases.

Package ID:

```text
com.trafalgardi.mesh-combiner
```

Current `dev` version: **2.6.0-dev.1**

Requirements:

- Unity 6.0 (`6000.0`) or newer;
- Git available in `PATH` for Git-URL UPM installation;
- runtime combining requires source meshes with **Read/Write Enabled**;
- Edit Mode combining can use imported meshes with Read/Write disabled.

## Quick start

1. Create/select a parent GameObject.
2. Add `MeshFilter`, `MeshRenderer`, and `MeshCombiner`.
3. Put source meshes under that GameObject.
4. Configure source, material, output and lightmap settings.
5. Click **Analyze / Validate** to inspect exactly what will be included or ignored.
6. Click **Combine Meshes**. Combine reruns validation automatically before mutating the hierarchy.
7. Inspect the generated mesh and, for baked geometry, the Lighting debug views.
8. Optionally save the generated mesh as a `.asset`.

For normal Editor workflows, keep source objects recoverable instead of destroying them.

## Main features

- destination-local transform-safe mesh combining;
- dedicated foldout-based custom Inspector with contextual settings and tooltips;
- Analyzer / preflight validation before hierarchy mutation;
- read-only Included and Ignored source lists grouped by reason;
- explicit **User Ignore List**;
- single-material and multi-material output;
- source submesh/material preservation;
- UInt16 / UInt32 index-buffer selection;
- optional combined `MeshCollider`;
- aggregated validation logs instead of per-mesh warning spam;
- disabled-renderer filtering;
- generic nested exact-duplicate helper/proxy filtering;
- multiple lightmap UV2 modes;
- integrated **Coplanar Stitched UV2** mode with T-junction support;
- optional exact opposing-face cleanup;
- baked-GI renderer setup;
- Editor Undo support;
- crash-persistent exact restore;
- emergency force recovery without a snapshot;
- generated mesh asset saving;
- UPM package layout, asmdefs and committed `.meta` files.

## Inspector and Analyzer

The Inspector is organized into contextual sections. Settings that do not affect the selected workflow are hidden. For example, lightmap packing controls are shown only for UV2 modes that actually pack charts, and opposing-face tolerance is shown only when the cleanup feature is enabled.

Every main checkbox/option has an Editor tooltip explaining what it changes and its relevant trade-offs.

### Analyze / Validate

**Analyze / Validate** is non-destructive. It scans the current hierarchy and creates a read-only preflight report in the Inspector.

The summary reports:

- discovered MeshFilters;
- included vs ignored sources;
- renderer count before/after estimate;
- source submesh/material draw estimate vs combined output estimate;
- unique materials;
- source vertices and triangles;
- expected UInt16 / UInt32 index format;
- blocking errors, warnings and informational notes.

The Analyzer also lists every ignored source grouped by reason.

Current automatic ignore reasons:

- **User Ignore** — explicitly listed by the user;
- **Inactive Hierarchy** — inactive while **Combine Inactive Children** is disabled;
- **Missing Mesh** — `MeshFilter.sharedMesh` is null;
- **Missing MeshRenderer** — no `MeshRenderer` on the same GameObject;
- **Disabled MeshRenderer** — renderer is disabled;
- **Nested Exact Duplicate** — a deeper child has the same shared Mesh and effectively the same world transform as an eligible ancestor.

The editable **User Ignore List** is separate from automatic ignore rules, so project-specific exclusions remain explicit rather than being hidden in naming conventions or internal heuristics.

Analyzer issues can also detect common preflight problems such as incompatible single-material input, missing material slots, missing UV2 for modes that require authored UV2, non-triangle topology for UV workflows, and runtime Read/Write restrictions.

**Combine Meshes reruns the Analyzer automatically.** If the preflight report contains blocking errors, Combine is stopped before a restore snapshot/hierarchy mutation is performed. Deep mode-specific validation inside the combiner still remains authoritative for conditions that require actually building the output mesh.

## Source selection and duplicate filtering

The combiner searches child `MeshFilter` components below the target root.

It automatically skips:

- the destination/root `MeshFilter`;
- entries in **User Ignore List**;
- inactive hierarchy branches when **Combine Inactive Children** is disabled;
- missing meshes;
- missing `MeshRenderer` components;
- disabled `MeshRenderer` components;
- nested exact duplicates.

A nested exact duplicate is skipped when a deeper child references the same `sharedMesh` and has effectively the same world transform as an ancestor below the combiner root. This is useful for helper/bake-proxy hierarchies and does **not** depend on object names.

## Materials and submeshes

### Create Multi-Material Mesh = Off

Use this only when all source submeshes use the same material. The Analyzer reports incompatible material sets before Combine instead of allowing the operation to fail after hierarchy preparation.

### Create Multi-Material Mesh = On

Source submeshes are grouped by shared `Material` reference. Repeated references to the same material do not create unnecessary duplicate material slots.

Combining multiple materials does not magically make them one draw call: different materials still require different material/submesh draws. The main win is fewer renderers and less submission overhead.

## Lightmap UV modes

Baked lightmaps use `Mesh.uv2`.

### None

Leaves UV2 untouched by the combiner. Use this for non-lightmapped output or when another pipeline will generate UV2.

### Preserve Source UV2

Copies source UV2 unchanged. This is mostly diagnostic because repeated modular meshes often reuse the same `0..1` UV2 space and therefore overlap after combining.

### Preserve And Repack Source UV2

Reuses authored UV2 charts and repacks them into one non-overlapping atlas.

The repacker:

1. finds source UV2 charts;
2. measures world-space surface area and source UV area;
3. accounts for source **Scale In Lightmap** in Edit Mode;
4. derives relative texel density;
5. packs charts with configurable texel padding;
6. preserves source chart topology.

This mode avoids topology changes, but independent source charts remain independent charts. It therefore cannot remove every modular baked seam.

### Regenerate UV2

Combines first and then calls Unity's secondary UV unwrapper.

Useful when source UV2 is missing or unusable, but Unity may split vertices and move seams while generating new charts.

### Coplanar Stitched UV2

This is the preferred mode for modular static architecture when visually continuous coplanar pieces should bake as one lightmap surface.

During **Combine Meshes**, the combiner:

1. combines the render geometry normally;
2. reads triangle geometry in world space;
3. connects triangles across exact shared geometric edges when normals are coplanar;
4. detects compatible collinear partially-overlapping seam edges, including T-junctions;
5. builds connected coplanar surfaces;
6. planar-projects each connected surface into one continuous UV2 chart;
7. packs the charts into one atlas;
8. writes UV2 only.

It does **not** weld render vertices and does not change positions, triangle indices, UV0, normals, tangents or materials.

The standard Inspector mode uses the validated defaults:

```text
Edge position tolerance: 0.0001 Unity units
Coplanar angle:          1 degree
Chart padding:           2 texels
Padding reference:       512
```

`Chart Padding` and `Padding Reference Size` are shown only when they affect the selected UV mode. The advanced tool remains available under:

```text
Tools -> Mesh Combiner -> Coplanar Stitched UV2...
```

Use the advanced tool when manually testing a previously combined mesh or when a special asset needs different tolerance / angle values.

### T-junction safety and why opening support is not window-specific

Window, door, arch and cutout modules often segment a shared boundary differently:

```text
neighbor module
|---------------------------|

opening module
|--------|----|-------------|
```

The T-junction pass is generic geometry matching; it has no knowledge of windows, doors or project object names.

A partial edge pair is stitched only when all relevant tests pass:

- owning triangle normals pass the configured coplanar-angle test;
- edge bounding boxes overlap within position tolerance;
- edge directions are parallel/anti-parallel within at most 1 degree;
- both edge endpoints lie on the other edge's infinite line within tolerance;
- the projected segments have a real overlap larger than tolerance;
- the two triangles are not already part of the same generated chart.

Ordinary two-owner coplanar triangulation edges inside one smooth surface are deliberately removed from the T-junction candidate set. Hard/open/non-manifold boundary records remain candidates so disconnected modular surfaces can be joined.

This makes false joins deliberately difficult, but no heuristic is mathematically impossible to misuse. If two unrelated surfaces are physically coincident/collinear within tolerance and also coplanar, the algorithm can intentionally treat them as one lightmap surface. The standard `0.0001` tolerance and `1 degree` angle are kept conservative for that reason.

## Lighting diagnostics

Useful Scene View debug modes:

- **Baked Lightmap** — inspect texel density and chart continuity;
- **UV Overlap** — detect overlapping sampling regions;
- **Texel Validity** — identify texels invalidated by backface/geometry conditions.

Important: Coplanar Stitched UV2 intentionally keeps render topology disconnected. Unity can therefore mark some stitched boundary texels in `UV Overlap` / `Texel Validity`, especially around hard perimeters and cutouts, even when the actual baked surface is clean. Treat debug views as diagnostics, not the sole pass/fail criterion. Always inspect the final bake.

## Geometry cleanup

### Remove Exact Opposing Faces

Optional and disabled by default.

It removes pairs of coincident triangles when:

- the same three destination-local positions match within **Position Tolerance**;
- the faces point in opposite directions.

The cleanup only changes triangle index buffers. It does not weld vertices or rewrite normals, tangents, UVs, colors, skinning data or other attributes.

This is intentionally conservative. It is **not** a Boolean union and does not remove partially overlapping coplanar polygons or coincident surfaces with different triangulation.

## Restore and recovery

### Restore Exact State

Before Combine, the Editor stores source `activeSelf` and `MeshRenderer.enabled` state.

The snapshot is persisted before hierarchy mutation and uses stable `GlobalObjectId` references, so it survives domain reloads and Editor restarts/crashes while the scene objects still exist.

### Force Recover Sources (No Snapshot)

Emergency fallback when a valid exact snapshot is unavailable.

It clears combined output and re-enables descendant mesh sources. Because there is no original snapshot, intentionally disabled helpers or variants can also be enabled.

### Destroy Combined Children

Destructive mode. Exact Restore and Force Recover cannot reconstruct objects that were actually destroyed. Prefer source deactivation for normal Editor workflows.

## MeshCollider output

Enable **Update/Create MeshCollider** to assign the generated render mesh to a destination `MeshCollider`.

The package does not merge arbitrary `BoxCollider`, `CapsuleCollider`, `SphereCollider`, or dedicated low-poly collision hierarchies into a separate optimized collision mesh.

## Saving generated meshes

Use **Save Combined Mesh** to save a generated mesh as a `.asset` below `Assets`.

Example:

```text
Generated/CombinedMeshes
```

Saved mesh assets are detached, not deleted, by Restore/Recovery.

## Optimization guidance

Manual combining can reduce:

- renderer count;
- per-renderer CPU overhead;
- draw submission overhead when compatible geometry/materials are grouped.

Trade-offs:

- pieces can no longer be culled independently;
- combining an entire level into one mesh can make culling worse;
- multiple materials still require multiple submesh/material draws;
- modern URP/HDRP projects should still evaluate SRP Batcher / GPU-driven rendering separately.

Prefer bounded spatial clusters such as rooms, building sections, floors or static prop groups instead of blindly combining a whole map.

## Current topology limitations

`Mesh.CombineMeshes` concatenates geometry. It is not a general Boolean/topology optimizer.

The package does not yet provide a general-purpose solution for:

- arbitrary Boolean union;
- partial coplanar face removal;
- coincident polygon matching across different triangulations;
- destructive weld-by-position;
- general non-manifold repair;
- automatic removal of all hidden/internal geometry;
- unused-vertex compaction after exact-face cleanup.

Coplanar Stitched UV2 solves lightmap continuity without changing render topology; geometry cleanup remains deliberately conservative.

## License and upstream

MIT license, following the original project.

Original project: `dawid-t/Mesh-Combiner`.
