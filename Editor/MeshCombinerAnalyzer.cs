using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

internal enum MeshCombinerIgnoreReason
{
    UserIgnore,
    InactiveHierarchy,
    MissingMesh,
    MissingMeshRenderer,
    DisabledMeshRenderer,
    NestedExactDuplicate
}

internal enum MeshCombinerIssueSeverity
{
    Info,
    Warning,
    Error
}

internal sealed class MeshCombinerAnalysisEntry
{
    public MeshFilter MeshFilter { get; }
    public MeshCombinerIgnoreReason Reason { get; }
    public string Details { get; }

    public MeshCombinerAnalysisEntry(MeshFilter meshFilter, MeshCombinerIgnoreReason reason, string details)
    {
        MeshFilter = meshFilter;
        Reason = reason;
        Details = details;
    }
}

internal sealed class MeshCombinerAnalysisIssue
{
    public MeshCombinerIssueSeverity Severity { get; }
    public UnityEngine.Object Context { get; }
    public string Message { get; }

    public MeshCombinerAnalysisIssue(MeshCombinerIssueSeverity severity, string message, UnityEngine.Object context = null)
    {
        Severity = severity;
        Message = message;
        Context = context;
    }
}

internal sealed class MeshCombinerAnalysisReport
{
    public readonly List<MeshFilter> Included = new List<MeshFilter>();
    public readonly List<MeshCombinerAnalysisEntry> Ignored = new List<MeshCombinerAnalysisEntry>();
    public readonly List<MeshCombinerAnalysisIssue> Issues = new List<MeshCombinerAnalysisIssue>();

    public int TotalDiscoveredMeshFilters;
    public int SourceRendererCount;
    public int SourceSubmeshCount;
    public int OutputSubmeshEstimate;
    public int UniqueMaterialCount;
    public int NonReadableMeshCount;
    public long SourceVertexCount;
    public long SourceTriangleCount;
    public long OutputVertexUpperBound;

    public bool HasErrors => Issues.Any(issue => issue.Severity == MeshCombinerIssueSeverity.Error);
    public IndexFormat ExpectedIndexFormat => OutputVertexUpperBound > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
}

internal static class MeshCombinerAnalyzer
{
    private const float MatrixComparisonEpsilon = 0.00001f;

    public static MeshCombinerAnalysisReport Analyze(
        MeshCombiner combiner,
        IReadOnlyCollection<MeshFilter> userIgnored,
        bool combineInactiveChildren,
        bool createMultiMaterialMesh,
        LightmapUvMode lightmapUvMode)
    {
        MeshCombinerAnalysisReport report = new MeshCombinerAnalysisReport();
        if (combiner == null)
        {
            report.Issues.Add(new MeshCombinerAnalysisIssue(MeshCombinerIssueSeverity.Error, "Mesh Combiner target is missing."));
            return report;
        }

        MeshFilter destination = combiner.GetComponent<MeshFilter>();
        HashSet<MeshFilter> explicitIgnore = new HashSet<MeshFilter>(
            userIgnored == null ? Enumerable.Empty<MeshFilter>() : userIgnored.Where(item => item != null));

        MeshFilter[] allBelowRoot = combiner.GetComponentsInChildren<MeshFilter>(true);
        HashSet<MeshFilter> traversalSet = new HashSet<MeshFilter>(
            combiner.GetComponentsInChildren<MeshFilter>(combineInactiveChildren));

        report.TotalDiscoveredMeshFilters = allBelowRoot.Count(item => item != null && item != destination);

        List<MeshFilter> candidates = new List<MeshFilter>();
        foreach (MeshFilter meshFilter in allBelowRoot)
        {
            if (meshFilter == null || meshFilter == destination)
                continue;

            if (explicitIgnore.Contains(meshFilter))
            {
                report.Ignored.Add(new MeshCombinerAnalysisEntry(
                    meshFilter,
                    MeshCombinerIgnoreReason.UserIgnore,
                    "Explicitly listed in User Ignore List."));
                continue;
            }

            if (!traversalSet.Contains(meshFilter))
            {
                report.Ignored.Add(new MeshCombinerAnalysisEntry(
                    meshFilter,
                    MeshCombinerIgnoreReason.InactiveHierarchy,
                    "Inactive in the current hierarchy while Combine Inactive Children is disabled."));
                continue;
            }

            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null)
            {
                report.Ignored.Add(new MeshCombinerAnalysisEntry(
                    meshFilter,
                    MeshCombinerIgnoreReason.MissingMesh,
                    "MeshFilter has no shared Mesh."));
                continue;
            }

            MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                report.Ignored.Add(new MeshCombinerAnalysisEntry(
                    meshFilter,
                    MeshCombinerIgnoreReason.MissingMeshRenderer,
                    "MeshFilter has no MeshRenderer on the same GameObject."));
                continue;
            }

            if (!renderer.enabled)
            {
                report.Ignored.Add(new MeshCombinerAnalysisEntry(
                    meshFilter,
                    MeshCombinerIgnoreReason.DisabledMeshRenderer,
                    "MeshRenderer is disabled."));
                continue;
            }

            candidates.Add(meshFilter);
        }

        RemoveNestedExactDuplicates(combiner.transform, candidates, report);
        report.Included.AddRange(candidates);
        report.SourceRendererCount = candidates.Count;

        if (candidates.Count == 0)
        {
            report.Issues.Add(new MeshCombinerAnalysisIssue(
                MeshCombinerIssueSeverity.Error,
                "No valid source MeshFilters remain after ignore/filter rules."));
            return report;
        }

        HashSet<Material> uniqueMaterials = new HashSet<Material>();
        bool foundNullMaterial = false;
        bool foundNonTriangleTopology = false;
        List<MeshFilter> missingUv2 = new List<MeshFilter>();

        foreach (MeshFilter meshFilter in candidates)
        {
            Mesh mesh = meshFilter.sharedMesh;
            MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
            Material[] materials = renderer.sharedMaterials;

            report.SourceVertexCount += mesh.vertexCount;
            report.SourceSubmeshCount += mesh.subMeshCount;

            if (!mesh.isReadable)
                report.NonReadableMeshCount++;

            if (materials.Length < mesh.subMeshCount)
            {
                report.Issues.Add(new MeshCombinerAnalysisIssue(
                    MeshCombinerIssueSeverity.Error,
                    "Renderer has " + materials.Length + " material slots for " + mesh.subMeshCount + " mesh submeshes.",
                    meshFilter));
            }

            int usableMaterialCount = Mathf.Min(materials.Length, mesh.subMeshCount);
            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                MeshTopology topology = mesh.GetTopology(subMeshIndex);
                if (topology == MeshTopology.Triangles)
                {
                    long indexCount = (long)mesh.GetIndexCount(subMeshIndex);
                    report.SourceTriangleCount += indexCount / 3L;
                    report.OutputVertexUpperBound += indexCount;
                }
                else
                {
                    foundNonTriangleTopology = true;
                    report.OutputVertexUpperBound += (long)mesh.GetIndexCount(subMeshIndex);
                }

                if (subMeshIndex < usableMaterialCount)
                {
                    Material material = materials[subMeshIndex];
                    if (material == null) foundNullMaterial = true;
                    else uniqueMaterials.Add(material);
                }
            }

            Vector2[] uv2 = mesh.uv2;
            if (uv2 == null || uv2.Length == 0 || uv2.Length != mesh.vertexCount)
                missingUv2.Add(meshFilter);
        }

        report.UniqueMaterialCount = uniqueMaterials.Count + (foundNullMaterial ? 1 : 0);
        report.OutputSubmeshEstimate = createMultiMaterialMesh
            ? report.UniqueMaterialCount
            : (report.SourceRendererCount > 0 ? 1 : 0);

        if (!createMultiMaterialMesh && report.UniqueMaterialCount > 1)
        {
            report.Issues.Add(new MeshCombinerAnalysisIssue(
                MeshCombinerIssueSeverity.Error,
                "Single Material mode would combine sources that use " + report.UniqueMaterialCount +
                " different material references. Enable Create Multi-Material Mesh or adjust the source set."));
        }

        if (foundNullMaterial)
        {
            report.Issues.Add(new MeshCombinerAnalysisIssue(
                MeshCombinerIssueSeverity.Warning,
                "One or more included submeshes use a null material reference."));
        }

        if (report.NonReadableMeshCount > 0)
        {
            report.Issues.Add(new MeshCombinerAnalysisIssue(
                MeshCombinerIssueSeverity.Info,
                report.NonReadableMeshCount +
                " included source meshes have Read/Write disabled. Edit Mode combining is supported; runtime combining requires CPU-readable meshes."));
        }

        if (foundNonTriangleTopology)
        {
            MeshCombinerIssueSeverity severity =
                lightmapUvMode == LightmapUvMode.None || lightmapUvMode == LightmapUvMode.PreserveSourceUv2
                    ? MeshCombinerIssueSeverity.Warning
                    : MeshCombinerIssueSeverity.Error;

            report.Issues.Add(new MeshCombinerAnalysisIssue(
                severity,
                "One or more included submeshes are not triangle topology. UV2 repack/regeneration/stitch workflows expect triangle geometry."));
        }

        if (missingUv2.Count > 0)
        {
            if (lightmapUvMode == LightmapUvMode.PreserveAndRepackSourceUv2)
            {
                report.Issues.Add(new MeshCombinerAnalysisIssue(
                    MeshCombinerIssueSeverity.Error,
                    missingUv2.Count + " included meshes are missing valid UV2, but Preserve And Repack Source UV2 requires UV2 on every source."));
            }
            else if (lightmapUvMode == LightmapUvMode.PreserveSourceUv2)
            {
                report.Issues.Add(new MeshCombinerAnalysisIssue(
                    MeshCombinerIssueSeverity.Warning,
                    missingUv2.Count + " included meshes are missing valid UV2. Preserve Source UV2 will not create it."));
            }
        }

        if (lightmapUvMode == LightmapUvMode.CoplanarStitchedUv2)
        {
            report.Issues.Add(new MeshCombinerAnalysisIssue(
                MeshCombinerIssueSeverity.Info,
                "Coplanar Stitched UV2 does not depend on source UV2. It rebuilds UV2 from combined triangle geometry and only joins edges that pass geometric coplanarity/overlap tests."));
        }

        return report;
    }

    private static void RemoveNestedExactDuplicates(
        Transform root,
        List<MeshFilter> candidates,
        MeshCombinerAnalysisReport report)
    {
        HashSet<MeshFilter> available = new HashSet<MeshFilter>(candidates);
        List<MeshFilter> ordered = candidates
            .OrderByDescending(candidate => GetHierarchyDepth(candidate.transform))
            .ToList();

        foreach (MeshFilter candidate in ordered)
        {
            if (!available.Contains(candidate))
                continue;

            Transform ancestor = candidate.transform.parent;
            while (ancestor != null && ancestor != root)
            {
                MeshFilter ancestorMeshFilter = ancestor.GetComponent<MeshFilter>();
                if (ancestorMeshFilter != null &&
                    available.Contains(ancestorMeshFilter) &&
                    ancestorMeshFilter.sharedMesh == candidate.sharedMesh &&
                    MatricesApproximatelyEqual(
                        ancestorMeshFilter.transform.localToWorldMatrix,
                        candidate.transform.localToWorldMatrix))
                {
                    available.Remove(candidate);
                    candidates.Remove(candidate);
                    report.Ignored.Add(new MeshCombinerAnalysisEntry(
                        candidate,
                        MeshCombinerIgnoreReason.NestedExactDuplicate,
                        "Same shared Mesh and world transform as ancestor " + ancestorMeshFilter.name + "."));
                    break;
                }

                ancestor = ancestor.parent;
            }
        }
    }

    private static bool MatricesApproximatelyEqual(Matrix4x4 a, Matrix4x4 b)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                if (Mathf.Abs(a[row, column] - b[row, column]) > MatrixComparisonEpsilon)
                    return false;
            }
        }

        return true;
    }

    private static int GetHierarchyDepth(Transform target)
    {
        int depth = 0;
        while (target != null)
        {
            depth++;
            target = target.parent;
        }

        return depth;
    }
}
