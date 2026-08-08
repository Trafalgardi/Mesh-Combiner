# Mesh Combiner for Unity 6

A maintained Unity 6 fork of `dawid-t/Mesh-Combiner` focused on two practical goals:

- reduce renderer / draw-submission overhead by combining static meshes;
- prepare modular geometry for reliable baked-lighting workflows.

The fork keeps the original Editor-friendly idea and adds UPM packaging, workflow-aware recovery, preflight analysis, material/submesh handling, MeshCollider output, duplicate filtering, conservative geometry cleanup, and several UV2 strategies including seam-aware **Coplanar Stitched UV2** with T-junction support.

## Installation

### Stable — `master`

In **Window -> Package Management -> Package Manager**, choose **Install package from git URL...** and use:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git
```

### Development — `dev`

For active development/testing:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git#dev
```

`dev` is the moving development channel. Features are tested there first and periodically promoted to `master`.

Package ID:

```text
com.trafalgardi.mesh-combiner
```

Current `dev` version: **2.6.0-dev.2**

Requirements:

- Unity 6.0 (`6000.0`) or newer;
- Git available in `PATH` for Git-URL UPM installation;
- runtime combining requires source meshes with **Read/Write Enabled**;
- Edit Mode combining can use imported meshes with Read/Write disabled.

## Quick start

1. Add `MeshFilter`, `MeshRenderer`, and `MeshCombiner` to a parent GameObject.
2. Put source meshes under that GameObject.
3. Configure source, material, output and UV2 settings.
4. Optionally press **Analyze / Validate** to inspect the exact source set and ignore reasons.
5. Press **Combine Meshes**. Combine always reruns validation before mutating the hierarchy.
6. Bake lighting if applicable.
7. Optionally save the generated Mesh as a `.asset`.
8. Use **Restore Exact State** to return to the pre-combine hierarchy.

For normal Editor workflows, prefer recoverable source deactivation instead of destructive deletion.

## Main features

- transform-safe destination-local mesh combining;
- custom workflow-aware Inspector with contextual controls and tooltips;
- non-destructive Analyzer / preflight validation;
- explicit **User Ignore List** plus automatic ignore rules;
- read-only Included / Ignored source reports grouped by reason;
- single-material and multi-material output;
- source submesh/material preservation;
- UInt16 / UInt32 index-buffer selection;
- optional combined `MeshCollider`;
- generic nested exact-duplicate helper/proxy filtering;
- multiple lightmap UV2 modes;
- integrated **Coplanar Stitched UV2** with exact-edge and partial/T-junction stitching;
- optional exact opposing-face cleanup;
- baked-GI renderer setup;
- crash-persistent exact restore and combined-state tracking;
- emergency force recovery;
- generated mesh asset saving;
- UPM package layout, asmdefs and committed `.meta` files.

## Inspector workflow states

The Inspector is state-aware instead of continuously reinterpreting deactivated sources as errors.

### READY

No active combined result is tracked. Source controls and Analyzer are available.

### COMBINED

A combined output Mesh and exact restore snapshot are active. Source validation is paused because the hierarchy was intentionally changed by Combine. The Inspector shows result metrics instead of reporting deactivated source objects as invalid.

The result card includes:

- Mesh name;
- vertices;
- triangles;
- submeshes;
- index format;
- material count;
- exact restore availability;
- baked lightmap assignment.

When Unity has assigned a baked lightmap to the destination renderer, the state is shown as **COMBINED • BAKED**.

### OUTPUT WITHOUT SNAPSHOT

The tool recognizes combined output, but the exact restore snapshot is unavailable. The Inspector shows a recovery warning and offers aggressive **Force Recover Sources** instead of pretending the source set is valid for a new combine.

### INTERRUPTED / RECOVERABLE

A crash-persistent snapshot exists but the combined output was never assigned, typically because an operation was interrupted between snapshot capture and output creation. Restore is offered immediately.

Combined state and restore data are scoped per project/object and survive Editor/domain reloads through stable `GlobalObjectId` references.

A pre-existing Mesh on the root is not automatically classified as combined output just because `MeshFilter.sharedMesh` is non-null.

## Inspector design

The Inspector uses lightweight foldout-header sections rather than nesting every section inside large help boxes.

Settings are contextual:

- lightmap packing controls appear only for UV modes that pack charts;
- exact-face tolerance appears only when opposing-face cleanup is enabled;
- child renderer disabling appears only when whole child GameObjects are not being deactivated;
- destructive deletion is hidden under an explicit destructive subsection;
- source configuration is disabled while a combined result is active.

Included, Ignored and Issues lists are **collapsed by default**. They open only when requested by the user.

Every primary control has an Editor tooltip describing what it changes and the relevant trade-off.

## Analyze / Validate

**Analyze / Validate** is optional and non-destructive. It scans the current source hierarchy and creates a read-only preflight report.

The summary includes:

- discovered MeshFilters;
- included / ignored counts;
- renderer count before/after estimate;
- source vs output submesh/material draw estimate;
- source vertices and triangles;
- expected UInt16 / UInt32 index format;
- blocking errors, warnings and informational notes.

Analyzer output is not automatically regenerated every time the Inspector opens. If source settings change, the existing report is marked stale. **Combine Meshes always runs a fresh Analyzer pass automatically.**

### Ignore reasons

Automatic and explicit ignores are separated:

- **User Ignore** — explicitly listed in User Ignore List;
- **Inactive Hierarchy** — inactive while Combine Inactive Children is disabled;
- **Missing Mesh** — `MeshFilter.sharedMesh` is null;
- **Missing MeshRenderer** — no MeshRenderer on the same GameObject;
- **Disabled MeshRenderer** — renderer is disabled;
- **Nested Exact Duplicate** — deeper child references the same Mesh and effectively the same world transform as an eligible ancestor.

The ignored-source list is read-only and grouped by reason so automatic heuristics are visible rather than hidden implementation rules.

Analyzer validation also checks common conditions such as incompatible single-material input, insufficient material slots, null materials, source UV2 requirements, non-triangle topology in UV workflows, and runtime Read/Write restrictions.

## Source selection and duplicate filtering

The combiner searches descendant `MeshFilter` components below the target root and excludes the destination/root MeshFilter itself.

A nested exact duplicate is skipped when a deeper child uses the same `sharedMesh` and effectively the same world transform as an eligible ancestor. This is useful for helper/bake-proxy hierarchies and does **not** depend on object names.

## Materials and submeshes

### Create Multi-Material Mesh = Off

Use only when all included source submeshes use the same material reference. Analyzer blocks incompatible input before hierarchy mutation.

### Create Multi-Material Mesh = On

Different shared material references become separate output submeshes. Repeated references are grouped.

Combining multiple materials does not guarantee one draw call: different material/submesh combinations can still require separate draw submissions. The main benefit is reduced renderer and submission overhead.

## Lightmap UV modes

Baked lightmaps use `Mesh.uv2`.

### None

Leaves UV2 untouched. Useful for non-lightmapped output or external UV2 pipelines.

### Preserve Source UV2

Copies source UV2 unchanged. Repeated modular assets often overlap in `0..1`, so this is mainly useful when sources already share a valid atlas.

### Preserve And Repack Source UV2

Reuses authored UV2 charts and repacks them into one non-overlapping atlas. World-space surface area and source Scale In Lightmap contribute to relative texel density.

Chart topology is preserved, so disconnected modular charts remain disconnected and can still create baked joins.

### Regenerate UV2

Runs Unity secondary UV unwrap on the combined Mesh. Source UV2 is not required, but Unity can split vertices while creating chart seams.

### Coplanar Stitched UV2

Preferred for modular static architecture where visually continuous coplanar pieces should bake as one lightmap surface.

During Combine the tool:

1. combines render geometry normally;
2. reads triangle geometry in world space;
3. joins exact shared geometric edges when owning normals are coplanar;
4. joins compatible collinear partially-overlapping edges, including T-junctions;
5. builds connected coplanar surfaces;
6. planar-projects each connected surface into one continuous UV2 chart;
7. packs charts into one atlas;
8. writes UV2 only.

It does **not** weld render vertices and does not change positions, triangle indices, UV0, normals, tangents or materials.

Validated standard settings:

```text
Edge position tolerance: 0.0001 Unity units
Coplanar angle:          1 degree
Chart padding:           2 texels
Padding reference:       512
```

Advanced manual testing remains available at:

```text
Tools -> Mesh Combiner -> Coplanar Stitched UV2...
```

### T-junction safety

Opening support is generic geometry matching; it has no knowledge of windows, doors, arches or object names.

A partial edge pair stitches only when relevant tests pass, including:

- coplanar owning triangle normals;
- overlapping edge bounds within tolerance;
- nearly parallel/anti-parallel edge directions;
- collinear edge lines within tolerance;
- real segment overlap larger than tolerance;
- separate generated chart groups.

Ordinary two-owner coplanar triangulation edges inside one surface are removed from the T-junction candidate set. Open, hard and non-manifold seam records remain candidates.

No heuristic is mathematically impossible to misuse. Two unrelated surfaces that are physically coincident/collinear within the conservative tolerance and coplanar angle can intentionally be treated as one lightmap surface.

## Lighting diagnostics

Useful Scene View modes:

- **Baked Lightmap** — texel density and chart continuity;
- **UV Overlap** — overlapping sampling regions;
- **Texel Validity** — invalidated texels caused by geometry/backface conditions.

Coplanar Stitched UV2 intentionally leaves render topology disconnected. Unity can therefore mark some stitched-boundary texels in UV Overlap / Texel Validity even when the actual final bake is visually clean. Treat these modes as diagnostics, not the sole pass/fail criterion.

## Geometry cleanup

### Remove Exact Opposing Faces

Optional and disabled by default.

Removes pairs of coincident triangles only when the same three positions match within Position Tolerance and winding is opposite.

It changes triangle index buffers only. It does not weld vertices or rewrite normals, tangents, UVs, colors or skinning data.

This is intentionally conservative and is **not** a Boolean union. Partial coplanar overlap and different triangulations are not removed.

## Restore and recovery

### Restore Exact State

Before Combine, the Editor stores source `activeSelf` and `MeshRenderer.enabled` values. The snapshot is persisted before hierarchy mutation and uses stable `GlobalObjectId` references.

### Force Recover Sources

Emergency fallback. Clears generated output and aggressively re-enables descendant mesh source GameObjects and MeshRenderers. Without an exact snapshot, intentionally disabled helpers/variants can also be enabled.

### Destroy Combined Children

Destructive mode. Deleted hierarchy objects cannot be recreated by Restore or Force Recover. Prefer source deactivation for normal production workflows.

## MeshCollider output

Enable **Update/Create MeshCollider** to assign the generated render Mesh to a destination MeshCollider.

The package does not merge arbitrary primitive/custom child colliders into a separately optimized collision mesh.

## Saving generated meshes

Use **Save Combined Mesh** while in combined state to persist the generated Mesh below `Assets`.

Example:

```text
Generated/CombinedMeshes
```

Saved Mesh assets are detached rather than deleted by Restore/Recovery.

## Optimization guidance

Manual combining can reduce renderer count, per-renderer CPU cost and draw-submission overhead when compatible geometry/materials are grouped.

Trade-offs:

- pieces cannot be culled independently after combining;
- combining an entire level into one Mesh can make culling worse;
- multiple materials still require multiple submesh/material draws;
- modern URP/HDRP projects should evaluate SRP Batcher / GPU-driven rendering separately.

Prefer bounded spatial clusters such as rooms, building sections, floors or static prop groups rather than blindly combining a whole map.

## Current topology limitations

`Mesh.CombineMeshes` concatenates geometry; it is not a general Boolean/topology optimizer.

The package does not yet provide a general solution for:

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
