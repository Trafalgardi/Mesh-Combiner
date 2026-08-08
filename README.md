# Mesh Combiner for Unity

A modernized Unity 6 mesh-combining utility for reducing renderer/draw-call overhead and preparing static geometry for baked-lighting workflows.

This project is a fork of `dawid-t/Mesh-Combiner`, substantially updated for current Unity versions, UPM installation, reversible Editor workflows, lightmap UV handling, validation, and collider output.

## Installation

### Unity Package Manager — Git URL

1. Open **Window -> Package Management -> Package Manager**.
2. Click **+**.
3. Choose **Install package from git URL...**.
4. Paste the repository URL.

Current development branch:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git#agent/unity6-modernization
```

After the modernization branch is merged, the package can be installed from the default branch with:

```text
https://github.com/Trafalgardi/Mesh-Combiner.git
```

Package ID:

```text
com.trafalgardi.mesh-combiner
```

Current package version: **2.2.2**

## Requirements

- Unity 6.0 (`6000.0`) or newer.
- Git available in `PATH` when installing through a Git URL.
- Runtime combining requires source meshes with **Read/Write Enabled** because Unity must access CPU-side mesh data.
- Editor combining can work with imported meshes that have Read/Write disabled.

## Quick start

1. Create or select a parent GameObject.
2. Add `MeshFilter`, `MeshRenderer`, and `MeshCombiner` to it.
3. Put the meshes that should be combined under that GameObject.
4. Configure material/lightmap/output options in `MeshCombiner`.
5. Click **Combine Meshes**.
6. Validate the result.
7. Optionally click **Save Combined Mesh** to write the generated mesh as an asset under `Assets`.

By default source objects are deactivated instead of destroyed so the operation remains recoverable.

## Main features

- destination-local, transform-safe mesh combining;
- single-material and multi-material output;
- submesh preservation;
- shared-material grouping;
- UInt16/UInt32 index-buffer selection;
- optional combined `MeshCollider` output;
- aggregated validation logs;
- automatic skipping of disabled source renderers;
- automatic filtering of nested exact-duplicate helper/proxy geometry;
- several lightmap UV2 workflows;
- automatic baked-GI setup for lightmapped output;
- crash-persistent exact restore state;
- emergency recovery without a restore snapshot;
- Editor Undo integration;
- generated mesh asset saving;
- UPM package layout with Runtime and Editor asmdefs.

## Source selection and validation

The combiner searches child `MeshFilter` components under the target object.

The following inputs are skipped automatically:

- the destination/root `MeshFilter`;
- entries explicitly listed in **Mesh Filters To Skip**;
- child objects without a mesh;
- child objects without a `MeshRenderer`;
- child objects whose `MeshRenderer.enabled` is false;
- nested exact duplicates as described below.

Validation messages are aggregated instead of logging one warning per source mesh.

### Read/Write

Imported meshes with **Read/Write disabled** are valid for the Editor workflow. The Inspector reports them in one aggregated informational message.

Runtime combining requires CPU-readable source mesh data, so Read/Write must be enabled for meshes combined while the application is running.

## Nested exact-duplicate filtering

Some hierarchies contain helper or bake-proxy copies nested under the visible mesh object.

The combiner automatically skips the deeper `MeshFilter` when all of the following are true:

- an ancestor below the MeshCombiner root also has a `MeshFilter`;
- both reference the same `sharedMesh` asset;
- both have effectively identical world transforms.

This detection is generic and does **not** depend on project-specific object names.

It prevents exact duplicate render geometry from being included twice in the generated mesh.

## Materials and submeshes

### Create Multi-Material Mesh = Off

Use this only when all source submeshes use the same material.

The combiner validates the material layout and refuses to silently flatten different materials into one incorrect result.

### Create Multi-Material Mesh = On

Source submeshes are grouped by shared `Material` reference. Repeated use of the same material does not create duplicate output material slots.

A combined mesh with multiple output materials still requires multiple material/submesh draw calls. Combining reduces renderer/object overhead; it does not turn unrelated materials into one draw call.

## Lightmap UV Mode

Baked lightmaps use `Mesh.uv2` (shown as the second UV channel in Unity's mesh inspection UI).

Available modes:

### None

Does not modify lightmap UVs.

Use this for non-lightmapped output or when another pipeline will generate UV2 later.

### Preserve Source UV2

Copies source UV2 values unchanged.

This is mainly diagnostic. If several modular meshes independently use the same `0..1` UV area, their charts will overlap after the meshes are combined.

### Preserve And Repack Source UV2

Recommended experimental mode for modular static geometry that already contains valid authored lightmap UVs.

The combiner:

1. reads existing UV2 charts;
2. identifies chart connectivity through shared UV edges;
3. measures each chart's world-space surface area and source UV area;
4. includes the source renderer's **Scale In Lightmap** in Edit Mode;
5. derives relative chart density from world-space surface area;
6. packs the existing charts into one non-overlapping UV2 atlas;
7. preserves chart topology and does not intentionally add vertices.

Default packing settings:

```text
Chart Padding = 2 texels
Padding Reference Size = 512
```

The final combine log reports chart count, density range, packing scale, and filtered source counts.

Different UV charts can show a different checker **phase/offset** in Unity's Baked Lightmap visualization. That is normal: the charts live at different atlas coordinates. The important diagnostic is consistent checker **size/texel density**, not checker alignment between separate charts.

### Regenerate UV2

Combines the geometry first and then calls Unity's secondary UV unwrapper.

This is useful when source meshes do not contain usable UV2, but the unwrap can split vertices and can produce a substantially larger vertex buffer. It can also change the location of baked seams.

## Baked-lighting setup

For lightmap UV modes other than `None`, the Editor workflow configures the destination renderer for baked lighting:

- `Contribute GI` is enabled on the destination GameObject;
- `Receive GI` is set to **Lightmaps**;
- `Stitch Seams` is enabled.

For modular environments, validate the result with Unity's Scene View debug modes:

- **Baked Lightmap** — inspect effective texel density;
- **UV Overlap** — detect charts whose sampling neighborhoods overlap;
- **Texel Validity** — find texels invalidated by geometry/backface conditions.

A clean UV Overlap view does not guarantee seamless lighting. Disconnected modular boundaries can still produce invalid texels or visible baked seams.

## Restore and recovery

The Editor workflow intentionally keeps source geometry available unless **Destroy Combined Children** is enabled.

### Restore Exact State

Before Combine, the Inspector records the exact `activeSelf` and `MeshRenderer.enabled` state of source objects.

Since version 2.2.2 this snapshot:

- is persisted **before** Combine mutates the hierarchy;
- uses stable Unity `GlobalObjectId` references in addition to the current InstanceID;
- is stored outside the transient Editor session;
- survives domain reloads and Editor restarts/crashes when the referenced scene objects still exist.

**Restore Exact State**:

- clears the combined destination mesh;
- clears destination materials;
- clears the combined `MeshCollider` reference when applicable;
- restores source GameObject active states;
- restores source MeshRenderer enabled states;
- destroys only an unsaved/transient generated mesh;
- does not delete a combined mesh that was already saved as a project asset.

### Force Recover Sources (No Snapshot)

This is the emergency fallback for a crashed/stale scene where the exact snapshot is missing or cannot be resolved.

It:

- clears the destination `MeshFilter.sharedMesh`;
- clears destination materials;
- clears the matching combined `MeshCollider` reference;
- activates every descendant GameObject that has a `MeshFilter`;
- enables every descendant `MeshRenderer`.

Because it has no exact pre-combine state, this mode is intentionally aggressive and can re-enable helper objects or variants that were intentionally disabled before Combine.

### Destroy Combined Children

This mode is destructive. Neither exact restore nor force recovery can reconstruct objects that have actually been destroyed.

Prefer deactivation for normal Editor workflows.

## MeshCollider output

Enable **Update/Create MeshCollider** to assign the generated render mesh to a `MeshCollider` on the destination GameObject.

This addresses the common case where the combined render mesh otherwise has no matching collision mesh.

Limitations:

- custom `BoxCollider`, `CapsuleCollider`, `SphereCollider`, etc. are not merged;
- custom low-poly collision meshes are not automatically combined separately;
- using a detailed render mesh as a MeshCollider may be more expensive than a dedicated collision mesh.

## Saving the combined mesh

Use **Save Combined Mesh** to save the generated mesh as a `.asset` under `Assets`.

The folder field is relative to `Assets`, for example:

```text
Generated/CombinedMeshes
```

Saved meshes are not deleted by Restore/Recovery; the component is only detached from them.

## Optimization notes

Manual mesh combining can reduce:

- active renderer count;
- per-renderer CPU overhead;
- draw-call submission overhead when compatible geometry/materials can be grouped.

Trade-offs:

- combined pieces can no longer be culled independently as separate renderers;
- combining an entire large level into one mesh can make culling worse;
- multiple materials still produce multiple submesh/material draws;
- modern URP/HDRP projects should still evaluate SRP Batcher and other current rendering optimizations.

Prefer combining logical spatial clusters such as a room, building section, static prop group, or other bounded region rather than blindly combining a whole scene.

## Current topology limitations

`Mesh.CombineMeshes` concatenates geometry; it is **not** a Boolean union or topology optimizer.

The current package does not yet:

- weld coincident boundary vertices;
- merge disconnected modular edges;
- remove internal/coplanar faces;
- remove arbitrary duplicate triangles;
- repair gaps, non-manifold geometry, normals, or source topology.

If **UV Overlap** is clean but **Texel Validity** and visible baked seams remain exactly on modular joins, the likely next step is a topology-aware lightmap workflow rather than further atlas-packing adjustments.

Any future welding mode should be optional and conservative because blindly welding by position can break hard normals, UV0 seams, materials, and intentionally disconnected geometry.

## License and upstream

MIT license, following the original project.

Original project: `dawid-t/Mesh-Combiner`.
