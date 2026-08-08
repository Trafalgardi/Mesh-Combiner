using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public enum LightmapUvMode
{
    None = 0,
    PreserveSourceUv2 = 1,
    PreserveAndRepackSourceUv2 = 2,
    RegenerateUv2 = 3,
    CoplanarStitchedUv2 = 4
}

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MeshCombiner : MonoBehaviour
{
    private const int Mesh16BitBufferVertexLimit = 65535;
    private const int ValidationLogNameLimit = 6;
    private const float MinimumUvExtent = 0.000001f;
    private const float MinimumArea = 0.0000000001f;
    private const float PackingEpsilon = 0.000001f;
    private const float UvEdgeQuantization = 100000f;
    private const float MatrixComparisonEpsilon = 0.00001f;
    private const float DefaultCoplanarPositionTolerance = 0.0001f;
    private const float DefaultCoplanarAngleDegrees = 1f;

    [Header("Combine")]
    [SerializeField] private bool createMultiMaterialMesh;
    [SerializeField] private bool combineInactiveChildren;
    [SerializeField] private MeshFilter[] meshFiltersToSkip = Array.Empty<MeshFilter>();

    [Header("Geometry Cleanup")]
    [SerializeField] private bool removeExactOpposingFaces;
    [SerializeField, Min(0.000001f)] private float exactFacePositionTolerance = 0.0001f;

    [Header("Output")]
    [SerializeField] private bool deactivateCombinedChildren = true;
    [SerializeField] private bool deactivateCombinedChildrenMeshRenderers;
    [SerializeField] private bool updateOrCreateMeshCollider;
    [SerializeField] private bool destroyCombinedChildren;

    [Header("Lightmap UV")]
    [SerializeField] private LightmapUvMode lightmapUvMode = LightmapUvMode.PreserveAndRepackSourceUv2;
    [SerializeField, Range(1, 16)] private int repackPaddingTexels = 2;
    [SerializeField] private int repackPaddingReferenceResolution = 512;

    [Header("Asset")]
    [SerializeField] private string folderPath = "Prefabs/CombinedMeshes";

    private int _lastRepackChartCount;
    private float _lastRepackScale = 1f;
    private float _lastDensityScaleRangeMin = 1f;
    private float _lastDensityScaleRangeMax = 1f;
    private int _lastSkippedDisabledRenderers;
    private int _lastSkippedNestedDuplicates;
    private int _lastRemovedExactOpposingFacePairs;
    private MeshCoplanarLightmapUv.Result _lastCoplanarResult;
    private bool _hasLastCoplanarResult;

    private sealed class UvChart
    {
        public MeshFilter meshFilter;
        public List<int> vertexIndices;
        public Rect sourceBounds;
        public Vector2 packedMin;
        public float relativeDensityScale = 1f;
        public int stableOrder;
    }

    private struct TriangleData
    {
        public int a;
        public int b;
        public int c;
    }

    private readonly struct UvPointKey : IEquatable<UvPointKey>, IComparable<UvPointKey>
    {
        private readonly int _x;
        private readonly int _y;

        public UvPointKey(Vector2 uv)
        {
            _x = Mathf.RoundToInt(uv.x * UvEdgeQuantization);
            _y = Mathf.RoundToInt(uv.y * UvEdgeQuantization);
        }

        public int CompareTo(UvPointKey other)
        {
            int xCompare = _x.CompareTo(other._x);
            return xCompare != 0 ? xCompare : _y.CompareTo(other._y);
        }

        public bool Equals(UvPointKey other) => _x == other._x && _y == other._y;
        public override bool Equals(object obj) => obj is UvPointKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (_x * 397) ^ _y; }
        }
    }

    private readonly struct UvEdgeKey : IEquatable<UvEdgeKey>
    {
        private readonly UvPointKey _a;
        private readonly UvPointKey _b;

        public UvEdgeKey(Vector2 a, Vector2 b)
        {
            UvPointKey pointA = new UvPointKey(a);
            UvPointKey pointB = new UvPointKey(b);
            if (pointA.CompareTo(pointB) <= 0)
            {
                _a = pointA;
                _b = pointB;
            }
            else
            {
                _a = pointB;
                _b = pointA;
            }
        }

        public bool Equals(UvEdgeKey other) => _a.Equals(other._a) && _b.Equals(other._b);
        public override bool Equals(object obj) => obj is UvEdgeKey other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (_a.GetHashCode() * 397) ^ _b.GetHashCode(); }
        }
    }

    private sealed class DisjointSet
    {
        private readonly int[] _parent;
        private readonly byte[] _rank;

        public DisjointSet(int count)
        {
            _parent = new int[count];
            _rank = new byte[count];
            for (int i = 0; i < count; i++) _parent[i] = i;
        }

        public int Find(int value)
        {
            int root = value;
            while (_parent[root] != root) root = _parent[root];
            while (_parent[value] != value)
            {
                int next = _parent[value];
                _parent[value] = root;
                value = next;
            }
            return root;
        }

        public void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA == rootB) return;
            if (_rank[rootA] < _rank[rootB]) _parent[rootA] = rootB;
            else if (_rank[rootA] > _rank[rootB]) _parent[rootB] = rootA;
            else
            {
                _parent[rootB] = rootA;
                _rank[rootA]++;
            }
        }
    }

    public bool CreateMultiMaterialMesh { get => createMultiMaterialMesh; set => createMultiMaterialMesh = value; }
    public bool CombineInactiveChildren { get => combineInactiveChildren; set => combineInactiveChildren = value; }
    public bool RemoveExactOpposingFaces { get => removeExactOpposingFaces; set => removeExactOpposingFaces = value; }
    public float ExactFacePositionTolerance { get => exactFacePositionTolerance; set => exactFacePositionTolerance = Mathf.Max(0.000001f, value); }
    public bool UpdateOrCreateMeshCollider { get => updateOrCreateMeshCollider; set => updateOrCreateMeshCollider = value; }
    public LightmapUvMode UvMode { get => lightmapUvMode; set => lightmapUvMode = value; }
    public int RepackPaddingTexels { get => repackPaddingTexels; set => repackPaddingTexels = Mathf.Clamp(value, 1, 16); }
    public int RepackPaddingReferenceResolution { get => repackPaddingReferenceResolution; set => repackPaddingReferenceResolution = Mathf.Clamp(value, 128, 4096); }
    public bool GenerateUVMap { get => lightmapUvMode == LightmapUvMode.RegenerateUv2; set => lightmapUvMode = value ? LightmapUvMode.RegenerateUv2 : LightmapUvMode.None; }
    public string FolderPath { get => folderPath; set => folderPath = value; }

    public bool DeactivateCombinedChildren
    {
        get => deactivateCombinedChildren;
        set
        {
            deactivateCombinedChildren = value;
            CheckDeactivateCombinedChildren();
        }
    }

    public bool DeactivateCombinedChildrenMeshRenderers
    {
        get => deactivateCombinedChildrenMeshRenderers;
        set
        {
            deactivateCombinedChildrenMeshRenderers = value;
            CheckDeactivateCombinedChildren();
        }
    }

    public bool DestroyCombinedChildren
    {
        get => destroyCombinedChildren;
        set
        {
            destroyCombinedChildren = value;
            CheckDestroyCombinedChildren();
        }
    }

    private void CheckDeactivateCombinedChildren()
    {
        if (deactivateCombinedChildren || deactivateCombinedChildrenMeshRenderers) destroyCombinedChildren = false;
    }

    private void CheckDestroyCombinedChildren()
    {
        if (!destroyCombinedChildren) return;
        deactivateCombinedChildren = false;
        deactivateCombinedChildrenMeshRenderers = false;
    }

    public void CombineMeshes(bool showCreatedMeshInfo) => TryCombineMeshes(showCreatedMeshInfo);

    public bool TryCombineMeshes(bool showCreatedMeshInfo)
    {
        if (!TryGetMeshFiltersToCombine(out List<MeshFilter> sourceMeshFilters)) return false;
        if (!TryPrepareSourceMeshesForUvMode(sourceMeshFilters, out Dictionary<MeshFilter, Mesh> sourceMeshes, out List<Mesh> temporaryUvMeshes)) return false;

        Mesh combinedMesh = null;
        List<Material> outputMaterials;
        try
        {
            bool combined = createMultiMaterialMesh
                ? TryCombineMultiMaterial(sourceMeshFilters, sourceMeshes, out combinedMesh, out outputMaterials)
                : TryCombineSingleMaterial(sourceMeshFilters, sourceMeshes, out combinedMesh, out outputMaterials);
            if (!combined || combinedMesh == null) return false;

            combinedMesh.name = name;
            _lastRemovedExactOpposingFacePairs = removeExactOpposingFaces
                ? MeshExactFaceCleanup.RemoveExactOpposingTrianglePairs(combinedMesh, exactFacePositionTolerance)
                : 0;
            combinedMesh.RecalculateBounds();

            _hasLastCoplanarResult = false;
            int verticesBeforeUvGeneration = combinedMesh.vertexCount;
            if (lightmapUvMode == LightmapUvMode.RegenerateUv2 && !GenerateUVIfRequested(combinedMesh))
            {
                DestroyTemporaryMesh(combinedMesh);
                return false;
            }

            if (lightmapUvMode == LightmapUvMode.CoplanarStitchedUv2)
            {
                if (!MeshCoplanarLightmapUv.TryGenerate(
                        combinedMesh,
                        transform.localToWorldMatrix,
                        DefaultCoplanarPositionTolerance,
                        DefaultCoplanarAngleDegrees,
                        Mathf.Clamp(repackPaddingTexels, 1, 16),
                        Mathf.Clamp(repackPaddingReferenceResolution, 128, 4096),
                        out _lastCoplanarResult,
                        out string coplanarError))
                {
                    Debug.LogError("Mesh Combiner: Coplanar Stitched UV2 failed for \"" + name + "\": " + coplanarError, this);
                    DestroyTemporaryMesh(combinedMesh);
                    return false;
                }

                _hasLastCoplanarResult = true;
            }

            MeshFilter destinationMeshFilter = GetComponent<MeshFilter>();
            MeshRenderer destinationMeshRenderer = GetComponent<MeshRenderer>();
            destinationMeshFilter.sharedMesh = combinedMesh;
            destinationMeshRenderer.sharedMaterials = outputMaterials.ToArray();
            PrepareDestinationForLightmapping(destinationMeshRenderer);
            UpdateMeshCollider(combinedMesh);
            DeactivateCombinedGameObjects(sourceMeshFilters);

            if (showCreatedMeshInfo)
            {
                int addedVertices = combinedMesh.vertexCount - verticesBeforeUvGeneration;
                Debug.Log("<color=#00cc00><b>Mesh \"" + name + "\" was created from " + sourceMeshFilters.Count +
                          " child meshes, " + combinedMesh.subMeshCount + " submeshes, " + combinedMesh.vertexCount +
                          " vertices and " + CountTriangles(combinedMesh) + " triangles" + BuildGeometryCleanupResultMessage() +
                          BuildLightmapResultMessage(addedVertices) + ".</b></color>", this);
            }
            return true;
        }
        finally
        {
            foreach (Mesh temporaryMesh in temporaryUvMeshes) DestroyTemporaryMesh(temporaryMesh);
        }
    }

    public void RestoreCombinedState(bool showInfo)
    {
        MeshFilter destinationMeshFilter = GetComponent<MeshFilter>();
        MeshRenderer destinationMeshRenderer = GetComponent<MeshRenderer>();
        Mesh combinedMesh = destinationMeshFilter.sharedMesh;
        destinationMeshFilter.sharedMesh = null;
        destinationMeshRenderer.sharedMaterials = Array.Empty<Material>();
        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider != null && (combinedMesh == null || meshCollider.sharedMesh == combinedMesh)) meshCollider.sharedMesh = null;
        if (showInfo)
        {
            Debug.Log("Mesh Combiner: cleared combined output for \"" + name +
                      "\". Use the Editor restore/recovery controls to restore source hierarchy state.", this);
        }
    }

    private bool TryGetMeshFiltersToCombine(out List<MeshFilter> meshFilters)
    {
        MeshFilter destinationMeshFilter = GetComponent<MeshFilter>();
        HashSet<MeshFilter> explicitlySkipped = new HashSet<MeshFilter>(
            meshFiltersToSkip == null ? Enumerable.Empty<MeshFilter>() : meshFiltersToSkip.Where(meshFilter => meshFilter != null));

        meshFilters = GetComponentsInChildren<MeshFilter>(combineInactiveChildren)
            .Where(meshFilter => meshFilter != null && meshFilter != destinationMeshFilter && !explicitlySkipped.Contains(meshFilter))
            .ToList();

        if (meshFilters.Count == 0)
        {
            Debug.LogError("Mesh Combiner: no child MeshFilters were found to combine.", this);
            return false;
        }

        List<string> missingMeshNames = new List<string>();
        List<string> missingRendererNames = new List<string>();
        List<string> disabledRendererNames = new List<string>();
        List<string> nonReadableMeshNames = new List<string>();

        for (int i = meshFilters.Count - 1; i >= 0; i--)
        {
            MeshFilter meshFilter = meshFilters[i];
            Mesh mesh = meshFilter.sharedMesh;
            MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
            if (mesh == null)
            {
                missingMeshNames.Add(meshFilter.name);
                meshFilters.RemoveAt(i);
                continue;
            }
            if (meshRenderer == null)
            {
                missingRendererNames.Add(meshFilter.name);
                meshFilters.RemoveAt(i);
                continue;
            }
            if (!meshRenderer.enabled)
            {
                disabledRendererNames.Add(meshFilter.name + " [" + mesh.name + "]");
                meshFilters.RemoveAt(i);
                continue;
            }
        }

        List<string> nestedDuplicateNames = RemoveNestedExactDuplicates(meshFilters);
        _lastSkippedDisabledRenderers = disabledRendererNames.Count;
        _lastSkippedNestedDuplicates = nestedDuplicateNames.Count;

        foreach (MeshFilter meshFilter in meshFilters)
        {
            if (!meshFilter.sharedMesh.isReadable)
                nonReadableMeshNames.Add(meshFilter.name + " [" + meshFilter.sharedMesh.name + "]");
        }

        LogSkippedInputs("no Mesh", missingMeshNames);
        LogSkippedInputs("no MeshRenderer", missingRendererNames);
        if (disabledRendererNames.Count > 0)
        {
            Debug.Log("Mesh Combiner: skipped " + disabledRendererNames.Count +
                      " child meshes whose MeshRenderer is disabled. Examples: " +
                      FormatValidationNames(disabledRendererNames) + ".", this);
        }
        if (nestedDuplicateNames.Count > 0)
        {
            Debug.Log("Mesh Combiner: skipped " + nestedDuplicateNames.Count +
                      " nested exact-duplicate meshes (same shared Mesh and world transform as an ancestor). " +
                      "This removes helper/bake-proxy copies without relying on object names. Examples: " +
                      FormatValidationNames(nestedDuplicateNames) + ".", this);
        }

        if (nonReadableMeshNames.Count > 0)
        {
            string examples = FormatValidationNames(nonReadableMeshNames);
            if (Application.isPlaying)
            {
                Debug.LogError("Mesh Combiner: " + nonReadableMeshNames.Count +
                               " source meshes have Read/Write disabled. Runtime combining requires CPU-readable source meshes. Examples: " + examples + ".", this);
                return false;
            }
            Debug.Log("Mesh Combiner: " + nonReadableMeshNames.Count +
                      " source meshes have Read/Write disabled. This is OK for Edit Mode combining; enable Read/Write only if these meshes must also be combined at runtime. Examples: " + examples + ".", this);
        }

        if (meshFilters.Count == 0)
        {
            Debug.LogError("Mesh Combiner: no valid child meshes remain after validation and duplicate filtering.", this);
            return false;
        }
        return true;
    }

    private List<string> RemoveNestedExactDuplicates(List<MeshFilter> meshFilters)
    {
        List<string> skippedNames = new List<string>();
        HashSet<MeshFilter> available = new HashSet<MeshFilter>(meshFilters);
        List<MeshFilter> ordered = meshFilters.OrderByDescending(meshFilter => GetHierarchyDepth(meshFilter.transform)).ToList();

        foreach (MeshFilter candidate in ordered)
        {
            if (!available.Contains(candidate)) continue;
            Transform ancestor = candidate.transform.parent;
            while (ancestor != null && ancestor != transform)
            {
                MeshFilter ancestorMeshFilter = ancestor.GetComponent<MeshFilter>();
                if (ancestorMeshFilter != null && available.Contains(ancestorMeshFilter) &&
                    ancestorMeshFilter.sharedMesh == candidate.sharedMesh &&
                    MatricesApproximatelyEqual(ancestorMeshFilter.transform.localToWorldMatrix, candidate.transform.localToWorldMatrix))
                {
                    available.Remove(candidate);
                    meshFilters.Remove(candidate);
                    skippedNames.Add(candidate.name + " [" + candidate.sharedMesh.name + "]");
                    break;
                }
                ancestor = ancestor.parent;
            }
        }
        return skippedNames;
    }

    private static bool MatricesApproximatelyEqual(Matrix4x4 a, Matrix4x4 b)
    {
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                if (Mathf.Abs(a[row, column] - b[row, column]) > MatrixComparisonEpsilon) return false;
            }
        }
        return true;
    }

    private bool TryPrepareSourceMeshesForUvMode(
        List<MeshFilter> meshFilters,
        out Dictionary<MeshFilter, Mesh> sourceMeshes,
        out List<Mesh> temporaryMeshes)
    {
        sourceMeshes = meshFilters.ToDictionary(meshFilter => meshFilter, meshFilter => meshFilter.sharedMesh);
        temporaryMeshes = new List<Mesh>();
        _lastRepackChartCount = 0;
        _lastRepackScale = 1f;
        _lastDensityScaleRangeMin = 1f;
        _lastDensityScaleRangeMax = 1f;
        if (lightmapUvMode != LightmapUvMode.PreserveAndRepackSourceUv2) return true;

        List<UvChart> allCharts = new List<UvChart>();
        Dictionary<MeshFilter, List<UvChart>> chartsByMeshFilter = new Dictionary<MeshFilter, List<UvChart>>();
        List<string> invalidUv2Names = new List<string>();
        int stableOrder = 0;

        foreach (MeshFilter meshFilter in meshFilters)
        {
            Mesh mesh = meshFilter.sharedMesh;
            Vector2[] uv2 = mesh.uv2;
            if (uv2 == null || uv2.Length != mesh.vertexCount || uv2.Length == 0)
            {
                invalidUv2Names.Add(meshFilter.name + " [" + mesh.name + ": missing UV2]");
                continue;
            }
            if (!TryExtractUvCharts(meshFilter, mesh, uv2, ref stableOrder, out List<UvChart> meshCharts, out string chartError))
            {
                invalidUv2Names.Add(meshFilter.name + " [" + mesh.name + ": " + chartError + "]");
                continue;
            }
            chartsByMeshFilter.Add(meshFilter, meshCharts);
            allCharts.AddRange(meshCharts);
        }

        if (invalidUv2Names.Count > 0)
        {
            Debug.LogError("Mesh Combiner: Preserve & Repack Source UV2 requires valid triangle UV2 charts on every source mesh. " +
                           invalidUv2Names.Count + " source meshes failed validation. Examples: " + FormatValidationNames(invalidUv2Names) +
                           ". Fix/generate source lightmap UVs or switch Lightmap UV Mode to Regenerate UV2.", this);
            return false;
        }

        NormalizeChartDensityScales(allCharts);
        float padding = Mathf.Clamp(repackPaddingTexels, 1, 16) /
                        (float)Mathf.Clamp(repackPaddingReferenceResolution, 128, 4096);
        if (!TryPackUvCharts(allCharts, padding, out float globalScale))
        {
            Debug.LogError("Mesh Combiner: UV2 chart packing failed. There are " + allCharts.Count +
                           " charts and the requested padding is " + repackPaddingTexels + " texels at a " +
                           repackPaddingReferenceResolution + " reference resolution. Lower the padding or use Regenerate UV2.", this);
            return false;
        }

        foreach (MeshFilter meshFilter in meshFilters)
        {
            Mesh sourceMesh = meshFilter.sharedMesh;
            Mesh workingMesh = Instantiate(sourceMesh);
            workingMesh.name = sourceMesh.name + "_RepackedUv2";
            Vector2[] uv2 = workingMesh.uv2;
            foreach (UvChart chart in chartsByMeshFilter[meshFilter])
            {
                Vector2 sourceMin = chart.sourceBounds.min;
                float finalScale = chart.relativeDensityScale * globalScale;
                foreach (int vertexIndex in chart.vertexIndices)
                {
                    Vector2 localUv = uv2[vertexIndex] - sourceMin;
                    uv2[vertexIndex] = chart.packedMin + localUv * finalScale;
                }
            }
            workingMesh.uv2 = uv2;
            sourceMeshes[meshFilter] = workingMesh;
            temporaryMeshes.Add(workingMesh);
        }

        _lastRepackChartCount = allCharts.Count;
        _lastRepackScale = globalScale;
        return true;
    }

    private bool TryExtractUvCharts(
        MeshFilter meshFilter,
        Mesh mesh,
        Vector2[] uv2,
        ref int stableOrder,
        out List<UvChart> charts,
        out string error)
    {
        charts = new List<UvChart>();
        error = null;
        List<TriangleData> triangles = new List<TriangleData>();

        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) != MeshTopology.Triangles)
            {
                error = "non-triangle submesh " + subMeshIndex;
                return false;
            }
            int[] indices = mesh.GetIndices(subMeshIndex);
            if (indices.Length % 3 != 0)
            {
                error = "invalid triangle index count";
                return false;
            }
            for (int i = 0; i < indices.Length; i += 3)
                triangles.Add(new TriangleData { a = indices[i], b = indices[i + 1], c = indices[i + 2] });
        }

        if (triangles.Count == 0)
        {
            error = "no triangles";
            return false;
        }

        DisjointSet triangleSets = new DisjointSet(triangles.Count);
        Dictionary<UvEdgeKey, int> edgeOwners = new Dictionary<UvEdgeKey, int>();
        for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            TriangleData triangle = triangles[triangleIndex];
            RegisterUvEdge(uv2[triangle.a], uv2[triangle.b], triangleIndex, triangleSets, edgeOwners);
            RegisterUvEdge(uv2[triangle.b], uv2[triangle.c], triangleIndex, triangleSets, edgeOwners);
            RegisterUvEdge(uv2[triangle.c], uv2[triangle.a], triangleIndex, triangleSets, edgeOwners);
        }

        Dictionary<int, List<int>> triangleIndicesByRoot = new Dictionary<int, List<int>>();
        for (int triangleIndex = 0; triangleIndex < triangles.Count; triangleIndex++)
        {
            int root = triangleSets.Find(triangleIndex);
            if (!triangleIndicesByRoot.TryGetValue(root, out List<int> triangleIndices))
            {
                triangleIndices = new List<int>();
                triangleIndicesByRoot.Add(root, triangleIndices);
            }
            triangleIndices.Add(triangleIndex);
        }

        Vector3[] vertices = mesh.vertices;
        Matrix4x4 localToWorld = meshFilter.transform.localToWorldMatrix;
        float sourceScaleInLightmap = 1f;
#if UNITY_EDITOR
        MeshRenderer sourceRenderer = meshFilter.GetComponent<MeshRenderer>();
        if (!Application.isPlaying && sourceRenderer != null)
            sourceScaleInLightmap = Mathf.Max(0.0001f, sourceRenderer.scaleInLightmap);
#endif

        foreach (List<int> triangleIndices in triangleIndicesByRoot.Values)
        {
            HashSet<int> vertexSet = new HashSet<int>();
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            float uvArea = 0f;
            float worldArea = 0f;

            foreach (int triangleIndex in triangleIndices)
            {
                TriangleData triangle = triangles[triangleIndex];
                vertexSet.Add(triangle.a);
                vertexSet.Add(triangle.b);
                vertexSet.Add(triangle.c);

                Vector2 uvA = uv2[triangle.a];
                Vector2 uvB = uv2[triangle.b];
                Vector2 uvC = uv2[triangle.c];
                min = Vector2.Min(min, Vector2.Min(uvA, Vector2.Min(uvB, uvC)));
                max = Vector2.Max(max, Vector2.Max(uvA, Vector2.Max(uvB, uvC)));
                uvArea += Mathf.Abs(Cross2D(uvB - uvA, uvC - uvA)) * 0.5f;

                Vector3 worldA = localToWorld.MultiplyPoint3x4(vertices[triangle.a]);
                Vector3 worldB = localToWorld.MultiplyPoint3x4(vertices[triangle.b]);
                Vector3 worldC = localToWorld.MultiplyPoint3x4(vertices[triangle.c]);
                worldArea += Vector3.Cross(worldB - worldA, worldC - worldA).magnitude * 0.5f;
            }

            Vector2 size = max - min;
            if (size.x <= MinimumUvExtent || size.y <= MinimumUvExtent || uvArea <= MinimumArea || worldArea <= MinimumArea)
            {
                error = "degenerate UV2/world-area chart";
                return false;
            }

            charts.Add(new UvChart
            {
                meshFilter = meshFilter,
                vertexIndices = vertexSet.ToList(),
                sourceBounds = new Rect(min, size),
                relativeDensityScale = Mathf.Sqrt(worldArea / uvArea) * sourceScaleInLightmap,
                stableOrder = stableOrder++
            });
        }

        if (charts.Count == 0)
        {
            error = "no UV2 charts";
            return false;
        }
        return true;
    }

    private static void RegisterUvEdge(
        Vector2 a,
        Vector2 b,
        int triangleIndex,
        DisjointSet triangleSets,
        Dictionary<UvEdgeKey, int> edgeOwners)
    {
        UvEdgeKey edge = new UvEdgeKey(a, b);
        if (edgeOwners.TryGetValue(edge, out int ownerTriangle)) triangleSets.Union(ownerTriangle, triangleIndex);
        else edgeOwners.Add(edge, triangleIndex);
    }

    private void NormalizeChartDensityScales(List<UvChart> charts)
    {
        float maxScale = charts.Max(chart => chart.relativeDensityScale);
        if (maxScale <= MinimumUvExtent) maxScale = 1f;
        float minNormalized = float.PositiveInfinity;
        float maxNormalized = 0f;
        foreach (UvChart chart in charts)
        {
            chart.relativeDensityScale /= maxScale;
            minNormalized = Mathf.Min(minNormalized, chart.relativeDensityScale);
            maxNormalized = Mathf.Max(maxNormalized, chart.relativeDensityScale);
        }
        _lastDensityScaleRangeMin = minNormalized;
        _lastDensityScaleRangeMax = maxNormalized;
    }

    private bool TryPackUvCharts(List<UvChart> charts, float padding, out float scale)
    {
        scale = 0f;
        if (charts.Count == 0) return false;
        List<UvChart> orderedCharts = charts
            .OrderByDescending(chart => chart.sourceBounds.height * chart.relativeDensityScale)
            .ThenByDescending(chart => chart.sourceBounds.width * chart.relativeDensityScale)
            .ThenBy(chart => chart.stableOrder)
            .ToList();

        if (!TryShelfPack(orderedCharts, 0f, padding, false)) return false;
        if (TryShelfPack(orderedCharts, 1f, padding, true))
        {
            scale = 1f;
            return true;
        }

        float low = 0f;
        float high = 1f;
        for (int iteration = 0; iteration < 30; iteration++)
        {
            float candidate = (low + high) * 0.5f;
            if (TryShelfPack(orderedCharts, candidate, padding, false)) low = candidate;
            else high = candidate;
        }

        if (low <= MinimumUvExtent || !TryShelfPack(orderedCharts, low, padding, true)) return false;
        scale = low;
        return true;
    }

    private static bool TryShelfPack(List<UvChart> charts, float scale, float padding, bool writePlacement)
    {
        float cursorX = 0f;
        float cursorY = 0f;
        float rowHeight = 0f;
        foreach (UvChart chart in charts)
        {
            float chartScale = chart.relativeDensityScale * scale;
            float contentWidth = chart.sourceBounds.width * chartScale;
            float contentHeight = chart.sourceBounds.height * chartScale;
            float outerWidth = contentWidth + padding * 2f;
            float outerHeight = contentHeight + padding * 2f;
            if (outerWidth > 1f + PackingEpsilon || outerHeight > 1f + PackingEpsilon) return false;
            if (cursorX > 0f && cursorX + outerWidth > 1f + PackingEpsilon)
            {
                cursorY += rowHeight;
                cursorX = 0f;
                rowHeight = 0f;
            }
            if (cursorY + outerHeight > 1f + PackingEpsilon) return false;
            if (writePlacement) chart.packedMin = new Vector2(cursorX + padding, cursorY + padding);
            cursorX += outerWidth;
            rowHeight = Mathf.Max(rowHeight, outerHeight);
        }
        return true;
    }

    private bool TryCombineSingleMaterial(
        List<MeshFilter> meshFilters,
        Dictionary<MeshFilter, Mesh> sourceMeshes,
        out Mesh combinedMesh,
        out List<Material> outputMaterials)
    {
        combinedMesh = null;
        outputMaterials = new List<Material>();
        List<CombineInstance> combineInstances = new List<CombineInstance>();
        Material sharedMaterial = null;
        bool materialInitialized = false;
        long outputVertexUpperBound = 0;
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

        foreach (MeshFilter meshFilter in meshFilters)
        {
            Mesh mesh = sourceMeshes[meshFilter];
            MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length < mesh.subMeshCount)
            {
                Debug.LogError("Mesh Combiner: \"" + meshFilter.name + "\" has " + mesh.subMeshCount +
                               " submeshes but only " + materials.Length +
                               " materials. Fix the renderer or use a valid material layout before combining.", meshFilter);
                return false;
            }

            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                Material material = materials[subMeshIndex];
                if (!materialInitialized)
                {
                    sharedMaterial = material;
                    materialInitialized = true;
                }
                else if (material != sharedMaterial)
                {
                    Debug.LogError("Mesh Combiner: Single Material mode found more than one material. " +
                                   "Enable Create Multi-Material Mesh to preserve all materials.", meshFilter);
                    return false;
                }

                combineInstances.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = subMeshIndex,
                    transform = worldToLocal * meshFilter.transform.localToWorldMatrix
                });
                outputVertexUpperBound += (long)mesh.GetIndexCount(subMeshIndex);
            }
        }

        if (combineInstances.Count == 0)
        {
            Debug.LogError("Mesh Combiner: no submeshes were found to combine.", this);
            return false;
        }

        combinedMesh = CreateOutputMesh(outputVertexUpperBound);
        combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true, false);
        outputMaterials.Add(sharedMaterial);
        return true;
    }

    private bool TryCombineMultiMaterial(
        List<MeshFilter> meshFilters,
        Dictionary<MeshFilter, Mesh> sourceMeshes,
        out Mesh combinedMesh,
        out List<Material> outputMaterials)
    {
        combinedMesh = null;
        outputMaterials = new List<Material>();
        List<List<CombineInstance>> instancesByMaterial = new List<List<CombineInstance>>();
        List<long> vertexUpperBoundByMaterial = new List<long>();
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

        foreach (MeshFilter meshFilter in meshFilters)
        {
            Mesh mesh = sourceMeshes[meshFilter];
            MeshRenderer renderer = meshFilter.GetComponent<MeshRenderer>();
            Material[] materials = renderer.sharedMaterials;
            if (materials.Length < mesh.subMeshCount)
            {
                Debug.LogError("Mesh Combiner: \"" + meshFilter.name + "\" has " + mesh.subMeshCount +
                               " submeshes but only " + materials.Length + " materials.", meshFilter);
                return false;
            }

            for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
            {
                Material material = materials[subMeshIndex];
                int materialIndex = outputMaterials.IndexOf(material);
                if (materialIndex < 0)
                {
                    materialIndex = outputMaterials.Count;
                    outputMaterials.Add(material);
                    instancesByMaterial.Add(new List<CombineInstance>());
                    vertexUpperBoundByMaterial.Add(0);
                }

                instancesByMaterial[materialIndex].Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = subMeshIndex,
                    transform = worldToLocal * meshFilter.transform.localToWorldMatrix
                });
                vertexUpperBoundByMaterial[materialIndex] += (long)mesh.GetIndexCount(subMeshIndex);
            }
        }

        if (outputMaterials.Count == 0)
        {
            Debug.LogError("Mesh Combiner: no materials/submeshes were found to combine.", this);
            return false;
        }

        List<Mesh> temporarySubMeshes = new List<Mesh>();
        List<CombineInstance> finalCombineInstances = new List<CombineInstance>();
        try
        {
            long finalVertexUpperBound = 0;
            for (int materialIndex = 0; materialIndex < outputMaterials.Count; materialIndex++)
            {
                Mesh materialMesh = CreateOutputMesh(vertexUpperBoundByMaterial[materialIndex]);
                materialMesh.name = name + "_Material_" + materialIndex;
                materialMesh.CombineMeshes(instancesByMaterial[materialIndex].ToArray(), true, true, false);
                temporarySubMeshes.Add(materialMesh);
                finalVertexUpperBound += materialMesh.vertexCount;
                finalCombineInstances.Add(new CombineInstance
                {
                    mesh = materialMesh,
                    subMeshIndex = 0,
                    transform = Matrix4x4.identity
                });
            }

            combinedMesh = CreateOutputMesh(finalVertexUpperBound);
            combinedMesh.CombineMeshes(finalCombineInstances.ToArray(), false, false, false);
            return true;
        }
        finally
        {
            foreach (Mesh temporaryMesh in temporarySubMeshes) DestroyTemporaryMesh(temporaryMesh);
        }
    }

    private static Mesh CreateOutputMesh(long outputVertexUpperBound)
    {
        Mesh mesh = new Mesh();
        if (outputVertexUpperBound > Mesh16BitBufferVertexLimit) mesh.indexFormat = IndexFormat.UInt32;
        return mesh;
    }

    private bool GenerateUVIfRequested(Mesh combinedMesh)
    {
        if (lightmapUvMode != LightmapUvMode.RegenerateUv2) return true;
#if UNITY_EDITOR
        UnityEditor.UnwrapParam.SetDefaults(out UnityEditor.UnwrapParam unwrapParam);
        bool success = UnityEditor.Unwrapping.GenerateSecondaryUVSet(combinedMesh, unwrapParam);
        if (!success)
        {
            Debug.LogError("Mesh Combiner: Unity failed to regenerate UV2 for \"" + combinedMesh.name +
                           "\". The output mesh was not assigned.", this);
        }
        return success;
#else
        Debug.LogError("Mesh Combiner: Regenerate UV2 is Editor-only. Use None/Preserve/Coplanar modes for runtime combining.", this);
        return false;
#endif
    }

    private void PrepareDestinationForLightmapping(MeshRenderer destinationMeshRenderer)
    {
#if UNITY_EDITOR
        if (lightmapUvMode == LightmapUvMode.None || Application.isPlaying) return;
        UnityEditor.StaticEditorFlags staticFlags =
            UnityEditor.GameObjectUtility.GetStaticEditorFlags(gameObject) | UnityEditor.StaticEditorFlags.ContributeGI;
        UnityEditor.GameObjectUtility.SetStaticEditorFlags(gameObject, staticFlags);
        destinationMeshRenderer.receiveGI = ReceiveGI.Lightmaps;
        destinationMeshRenderer.stitchLightmapSeams = true;
#endif
    }

    private void UpdateMeshCollider(Mesh combinedMesh)
    {
        if (!updateOrCreateMeshCollider) return;
        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) meshCollider = UnityEditor.Undo.AddComponent<MeshCollider>(gameObject);
            else
#endif
                meshCollider = gameObject.AddComponent<MeshCollider>();
        }
        meshCollider.sharedMesh = null;
        meshCollider.sharedMesh = combinedMesh;
    }

    private void DeactivateCombinedGameObjects(List<MeshFilter> meshFilters)
    {
        IEnumerable<MeshFilter> orderedMeshFilters = destroyCombinedChildren
            ? meshFilters.OrderByDescending(meshFilter => GetHierarchyDepth(meshFilter.transform))
            : meshFilters;

        foreach (MeshFilter meshFilter in orderedMeshFilters)
        {
            if (meshFilter == null) continue;
            if (destroyCombinedChildren)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying) UnityEditor.Undo.DestroyObjectImmediate(meshFilter.gameObject);
                else
#endif
                    Destroy(meshFilter.gameObject);
                continue;
            }
            if (deactivateCombinedChildren)
            {
                meshFilter.gameObject.SetActive(false);
                continue;
            }
            if (deactivateCombinedChildrenMeshRenderers)
            {
                MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
                if (meshRenderer != null) meshRenderer.enabled = false;
            }
        }
    }

    private string BuildGeometryCleanupResultMessage()
    {
        if (!removeExactOpposingFaces) return string.Empty;
        return ", removed " + _lastRemovedExactOpposingFacePairs +
               " exact opposing face pairs (" + (_lastRemovedExactOpposingFacePairs * 2) +
               " triangles) at tolerance " + exactFacePositionTolerance.ToString("0.######");
    }

    private string BuildLightmapResultMessage(int addedVertices)
    {
        string sourceFiltering = string.Empty;
        if (_lastSkippedDisabledRenderers > 0)
            sourceFiltering += ", skipped " + _lastSkippedDisabledRenderers + " disabled renderers";
        if (_lastSkippedNestedDuplicates > 0)
            sourceFiltering += ", skipped " + _lastSkippedNestedDuplicates + " nested exact duplicates";

        switch (lightmapUvMode)
        {
            case LightmapUvMode.None:
                return ", no lightmap UV processing" + sourceFiltering;
            case LightmapUvMode.PreserveSourceUv2:
                return ", source UV2 preserved without repacking" + sourceFiltering;
            case LightmapUvMode.PreserveAndRepackSourceUv2:
                return ", " + _lastRepackChartCount +
                       " source UV2 charts repacked by world-space surface area, global packing scale " +
                       _lastRepackScale.ToString("0.###") + ", density range " +
                       _lastDensityScaleRangeMin.ToString("0.###") + ".." +
                       _lastDensityScaleRangeMax.ToString("0.###") + " and " +
                       repackPaddingTexels + " texel padding @ " +
                       repackPaddingReferenceResolution + " reference" + sourceFiltering;
            case LightmapUvMode.RegenerateUv2:
                return ", UV2 regenerated (+" + Mathf.Max(0, addedVertices) +
                       " vertices from chart splits)" + sourceFiltering;
            case LightmapUvMode.CoplanarStitchedUv2:
                if (!_hasLastCoplanarResult) return ", Coplanar Stitched UV2 requested but no result was recorded" + sourceFiltering;
                return ", Coplanar Stitched UV2: " + _lastCoplanarResult.chartCount +
                       " charts, " + _lastCoplanarResult.exactEdgeConnections + " exact + " +
                       _lastCoplanarResult.partialCollinearEdgeConnections + " partial/T-junction connections, " +
                       _lastCoplanarResult.boundaryEdgeCount + " seam-candidate edge records, packing scale " +
                       _lastCoplanarResult.packingScale.ToString("0.###") + ", tolerance " +
                       DefaultCoplanarPositionTolerance.ToString("0.######") + ", angle " +
                       DefaultCoplanarAngleDegrees.ToString("0.###") + " deg, " + repackPaddingTexels +
                       " texel padding @ " + repackPaddingReferenceResolution + " reference" + sourceFiltering;
            default:
                return sourceFiltering;
        }
    }

    private static float Cross2D(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    private static long CountTriangles(Mesh mesh)
    {
        long triangleCount = 0;
        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) == MeshTopology.Triangles)
                triangleCount += (long)mesh.GetIndexCount(subMeshIndex) / 3L;
        }
        return triangleCount;
    }

    private void LogSkippedInputs(string reason, List<string> names)
    {
        if (names.Count == 0) return;
        Debug.LogWarning("Mesh Combiner: skipped " + names.Count + " child objects with " + reason +
                         ". Examples: " + FormatValidationNames(names) + ".", this);
    }

    private static string FormatValidationNames(List<string> names)
    {
        int visibleCount = Mathf.Min(names.Count, ValidationLogNameLimit);
        string value = string.Join(", ", names.Take(visibleCount));
        if (names.Count > visibleCount) value += ", +" + (names.Count - visibleCount) + " more";
        return value;
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

    private void DestroyTemporaryMesh(Mesh mesh)
    {
        if (mesh == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            DestroyImmediate(mesh);
            return;
        }
#endif
        Destroy(mesh);
    }
}
