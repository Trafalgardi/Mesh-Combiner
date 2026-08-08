using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public enum LightmapUvMode
{
    None = 0,
    PreserveSourceUv2 = 1,
    PreserveAndRepackSourceUv2 = 2,
    RegenerateUv2 = 3
}

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MeshCombiner : MonoBehaviour
{
    private const int Mesh16BitBufferVertexLimit = 65535;
    private const int ValidationLogNameLimit = 6;
    private const float MinimumUvExtent = 0.000001f;
    private const float PackingEpsilon = 0.000001f;

    [Header("Combine")]
    [SerializeField] private bool createMultiMaterialMesh;
    [SerializeField] private bool combineInactiveChildren;
    [SerializeField] private MeshFilter[] meshFiltersToSkip = new MeshFilter[0];

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

    private sealed class UvChart
    {
        public MeshFilter meshFilter;
        public List<int> vertexIndices;
        public Rect sourceBounds;
        public Vector2 packedMin;
        public int stableOrder;
    }

    private sealed class DisjointSet
    {
        private readonly int[] _parent;
        private readonly byte[] _rank;

        public DisjointSet(int count)
        {
            _parent = new int[count];
            _rank = new byte[count];
            for (int i = 0; i < count; i++)
            {
                _parent[i] = i;
            }
        }

        public int Find(int value)
        {
            int root = value;
            while (_parent[root] != root)
            {
                root = _parent[root];
            }

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
            if (rootA == rootB)
            {
                return;
            }

            if (_rank[rootA] < _rank[rootB])
            {
                _parent[rootA] = rootB;
            }
            else if (_rank[rootA] > _rank[rootB])
            {
                _parent[rootB] = rootA;
            }
            else
            {
                _parent[rootB] = rootA;
                _rank[rootA]++;
            }
        }
    }

    public bool CreateMultiMaterialMesh
    {
        get { return createMultiMaterialMesh; }
        set { createMultiMaterialMesh = value; }
    }

    public bool CombineInactiveChildren
    {
        get { return combineInactiveChildren; }
        set { combineInactiveChildren = value; }
    }

    public bool DeactivateCombinedChildren
    {
        get { return deactivateCombinedChildren; }
        set
        {
            deactivateCombinedChildren = value;
            CheckDeactivateCombinedChildren();
        }
    }

    public bool DeactivateCombinedChildrenMeshRenderers
    {
        get { return deactivateCombinedChildrenMeshRenderers; }
        set
        {
            deactivateCombinedChildrenMeshRenderers = value;
            CheckDeactivateCombinedChildren();
        }
    }

    public bool UpdateOrCreateMeshCollider
    {
        get { return updateOrCreateMeshCollider; }
        set { updateOrCreateMeshCollider = value; }
    }

    public LightmapUvMode UvMode
    {
        get { return lightmapUvMode; }
        set { lightmapUvMode = value; }
    }

    public int RepackPaddingTexels
    {
        get { return repackPaddingTexels; }
        set { repackPaddingTexels = Mathf.Clamp(value, 1, 16); }
    }

    public int RepackPaddingReferenceResolution
    {
        get { return repackPaddingReferenceResolution; }
        set { repackPaddingReferenceResolution = Mathf.Clamp(value, 128, 4096); }
    }

    // Backwards-compatible API from the first Unity 6 fork revision.
    public bool GenerateUVMap
    {
        get { return lightmapUvMode == LightmapUvMode.RegenerateUv2; }
        set { lightmapUvMode = value ? LightmapUvMode.RegenerateUv2 : LightmapUvMode.None; }
    }

    public bool DestroyCombinedChildren
    {
        get { return destroyCombinedChildren; }
        set
        {
            destroyCombinedChildren = value;
            CheckDestroyCombinedChildren();
        }
    }

    public string FolderPath
    {
        get { return folderPath; }
        set { folderPath = value; }
    }

    private void CheckDeactivateCombinedChildren()
    {
        if (deactivateCombinedChildren || deactivateCombinedChildrenMeshRenderers)
        {
            destroyCombinedChildren = false;
        }
    }

    private void CheckDestroyCombinedChildren()
    {
        if (destroyCombinedChildren)
        {
            deactivateCombinedChildren = false;
            deactivateCombinedChildrenMeshRenderers = false;
        }
    }

    public void CombineMeshes(bool showCreatedMeshInfo)
    {
        TryCombineMeshes(showCreatedMeshInfo);
    }

    public bool TryCombineMeshes(bool showCreatedMeshInfo)
    {
        List<MeshFilter> sourceMeshFilters;
        if (!TryGetMeshFiltersToCombine(out sourceMeshFilters))
        {
            return false;
        }

        Dictionary<MeshFilter, Mesh> sourceMeshes;
        List<Mesh> temporaryUvMeshes;
        if (!TryPrepareSourceMeshesForUvMode(sourceMeshFilters, out sourceMeshes, out temporaryUvMeshes))
        {
            return false;
        }

        Mesh combinedMesh = null;
        List<Material> outputMaterials;

        try
        {
            bool combined = createMultiMaterialMesh
                ? TryCombineMultiMaterial(sourceMeshFilters, sourceMeshes, out combinedMesh, out outputMaterials)
                : TryCombineSingleMaterial(sourceMeshFilters, sourceMeshes, out combinedMesh, out outputMaterials);

            if (!combined || combinedMesh == null)
            {
                return false;
            }

            combinedMesh.name = name;
            combinedMesh.RecalculateBounds();

            int verticesBeforeUvGeneration = combinedMesh.vertexCount;
            bool uvGenerated = GenerateUVIfRequested(combinedMesh);
            if (lightmapUvMode == LightmapUvMode.RegenerateUv2 && !uvGenerated)
            {
                DestroyTemporaryMesh(combinedMesh);
                return false;
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
                Debug.Log(
                    "<color=#00cc00><b>Mesh \"" + name + "\" was created from " + sourceMeshFilters.Count +
                    " child meshes, " + combinedMesh.subMeshCount + " submeshes, " + combinedMesh.vertexCount +
                    " vertices and " + CountTriangles(combinedMesh) + " triangles" + BuildLightmapResultMessage(addedVertices) +
                    ".</b></color>",
                    this);
            }

            return true;
        }
        finally
        {
            foreach (Mesh temporaryMesh in temporaryUvMeshes)
            {
                DestroyTemporaryMesh(temporaryMesh);
            }
        }
    }

    /// <summary>
    /// Clears only the combined output. Exact source hierarchy restoration is handled by the custom
    /// Editor inspector because it keeps a snapshot of the pre-combine active/enabled state.
    /// </summary>
    public void RestoreCombinedState(bool showInfo)
    {
        MeshFilter destinationMeshFilter = GetComponent<MeshFilter>();
        MeshRenderer destinationMeshRenderer = GetComponent<MeshRenderer>();
        Mesh combinedMesh = destinationMeshFilter.sharedMesh;

        destinationMeshFilter.sharedMesh = null;
        destinationMeshRenderer.sharedMaterials = new Material[0];

        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider != null && (combinedMesh == null || meshCollider.sharedMesh == combinedMesh))
        {
            meshCollider.sharedMesh = null;
        }

        if (showInfo)
        {
            Debug.Log(
                "Mesh Combiner: cleared combined output for \"" + name +
                "\". Use the Editor Restore / Undo Combine button to restore the exact source hierarchy state.",
                this);
        }
    }

    private bool TryGetMeshFiltersToCombine(out List<MeshFilter> meshFilters)
    {
        MeshFilter destinationMeshFilter = GetComponent<MeshFilter>();
        HashSet<MeshFilter> skipped = new HashSet<MeshFilter>(
            meshFiltersToSkip == null
                ? Enumerable.Empty<MeshFilter>()
                : meshFiltersToSkip.Where(meshFilter => meshFilter != null));

        meshFilters = GetComponentsInChildren<MeshFilter>(combineInactiveChildren)
            .Where(meshFilter => meshFilter != null)
            .Where(meshFilter => meshFilter != destinationMeshFilter)
            .Where(meshFilter => !skipped.Contains(meshFilter))
            .ToList();

        if (meshFilters.Count == 0)
        {
            Debug.LogError("Mesh Combiner: no child MeshFilters were found to combine.", this);
            return false;
        }

        List<string> missingMeshNames = new List<string>();
        List<string> missingRendererNames = new List<string>();
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

            if (!mesh.isReadable)
            {
                nonReadableMeshNames.Add(meshFilter.name + " [" + mesh.name + "]");
            }
        }

        LogSkippedInputs("no Mesh", missingMeshNames);
        LogSkippedInputs("no MeshRenderer", missingRendererNames);

        if (nonReadableMeshNames.Count > 0)
        {
            string examples = FormatValidationNames(nonReadableMeshNames);
            if (Application.isPlaying)
            {
                Debug.LogError(
                    "Mesh Combiner: " + nonReadableMeshNames.Count +
                    " source meshes have Read/Write disabled. Runtime combining requires CPU-readable source meshes. " +
                    "Examples: " + examples + ".",
                    this);
                return false;
            }

            Debug.Log(
                "Mesh Combiner: " + nonReadableMeshNames.Count +
                " source meshes have Read/Write disabled. This is OK for Edit Mode combining; " +
                "enable Read/Write only if these meshes must also be combined at runtime. Examples: " + examples + ".",
                this);
        }

        if (meshFilters.Count == 0)
        {
            Debug.LogError("Mesh Combiner: no valid child meshes remain after validation.", this);
            return false;
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

        if (lightmapUvMode != LightmapUvMode.PreserveAndRepackSourceUv2)
        {
            return true;
        }

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

            List<UvChart> meshCharts;
            string chartError;
            if (!TryExtractUvCharts(meshFilter, mesh, uv2, ref stableOrder, out meshCharts, out chartError))
            {
                invalidUv2Names.Add(meshFilter.name + " [" + mesh.name + ": " + chartError + "]");
                continue;
            }

            chartsByMeshFilter.Add(meshFilter, meshCharts);
            allCharts.AddRange(meshCharts);
        }

        if (invalidUv2Names.Count > 0)
        {
            Debug.LogError(
                "Mesh Combiner: Preserve & Repack Source UV2 requires valid triangle UV2 charts on every source mesh. " +
                invalidUv2Names.Count + " source meshes failed validation. Examples: " +
                FormatValidationNames(invalidUv2Names) +
                ". Fix/generate source lightmap UVs or switch Lightmap UV Mode to Regenerate UV2.",
                this);
            return false;
        }

        float padding = Mathf.Clamp(repackPaddingTexels, 1, 16) /
                        (float)Mathf.Clamp(repackPaddingReferenceResolution, 128, 4096);
        float globalScale;
        if (!TryPackUvCharts(allCharts, padding, out globalScale))
        {
            Debug.LogError(
                "Mesh Combiner: UV2 chart packing failed. There are " + allCharts.Count +
                " charts and the requested padding is " + repackPaddingTexels + " texels at a " +
                repackPaddingReferenceResolution + " reference resolution. Lower the padding or use Regenerate UV2.",
                this);
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
                foreach (int vertexIndex in chart.vertexIndices)
                {
                    Vector2 localUv = uv2[vertexIndex] - sourceMin;
                    uv2[vertexIndex] = chart.packedMin + localUv * globalScale;
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

        DisjointSet disjointSet = new DisjointSet(mesh.vertexCount);
        bool[] usedVertices = new bool[mesh.vertexCount];

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
            {
                int a = indices[i];
                int b = indices[i + 1];
                int c = indices[i + 2];

                usedVertices[a] = true;
                usedVertices[b] = true;
                usedVertices[c] = true;
                disjointSet.Union(a, b);
                disjointSet.Union(b, c);
                disjointSet.Union(c, a);
            }
        }

        Dictionary<int, List<int>> verticesByRoot = new Dictionary<int, List<int>>();
        for (int vertexIndex = 0; vertexIndex < usedVertices.Length; vertexIndex++)
        {
            if (!usedVertices[vertexIndex])
            {
                continue;
            }

            int root = disjointSet.Find(vertexIndex);
            List<int> vertexIndices;
            if (!verticesByRoot.TryGetValue(root, out vertexIndices))
            {
                vertexIndices = new List<int>();
                verticesByRoot.Add(root, vertexIndices);
            }
            vertexIndices.Add(vertexIndex);
        }

        foreach (List<int> vertexIndices in verticesByRoot.Values)
        {
            if (vertexIndices.Count == 0)
            {
                continue;
            }

            Vector2 min = uv2[vertexIndices[0]];
            Vector2 max = min;
            for (int i = 1; i < vertexIndices.Count; i++)
            {
                Vector2 uv = uv2[vertexIndices[i]];
                min = Vector2.Min(min, uv);
                max = Vector2.Max(max, uv);
            }

            Vector2 size = max - min;
            if (size.x <= MinimumUvExtent || size.y <= MinimumUvExtent)
            {
                error = "degenerate UV2 chart";
                return false;
            }

            charts.Add(new UvChart
            {
                meshFilter = meshFilter,
                vertexIndices = vertexIndices,
                sourceBounds = new Rect(min, size),
                stableOrder = stableOrder++
            });
        }

        if (charts.Count == 0)
        {
            error = "no triangle UV2 charts";
            return false;
        }

        return true;
    }

    private bool TryPackUvCharts(List<UvChart> charts, float padding, out float scale)
    {
        scale = 0f;
        if (charts.Count == 0)
        {
            return false;
        }

        List<UvChart> orderedCharts = charts
            .OrderByDescending(chart => chart.sourceBounds.height)
            .ThenByDescending(chart => chart.sourceBounds.width)
            .ThenBy(chart => chart.stableOrder)
            .ToList();

        if (!TryShelfPack(orderedCharts, 0f, padding, false))
        {
            return false;
        }

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
            if (TryShelfPack(orderedCharts, candidate, padding, false))
            {
                low = candidate;
            }
            else
            {
                high = candidate;
            }
        }

        if (low <= MinimumUvExtent)
        {
            return false;
        }

        if (!TryShelfPack(orderedCharts, low, padding, true))
        {
            return false;
        }

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
            float contentWidth = chart.sourceBounds.width * scale;
            float contentHeight = chart.sourceBounds.height * scale;
            float outerWidth = contentWidth + padding * 2f;
            float outerHeight = contentHeight + padding * 2f;

            if (outerWidth > 1f + PackingEpsilon || outerHeight > 1f + PackingEpsilon)
            {
                return false;
            }

            if (cursorX > 0f && cursorX + outerWidth > 1f + PackingEpsilon)
            {
                cursorY += rowHeight;
                cursorX = 0f;
                rowHeight = 0f;
            }

            if (cursorY + outerHeight > 1f + PackingEpsilon)
            {
                return false;
            }

            if (writePlacement)
            {
                chart.packedMin = new Vector2(cursorX + padding, cursorY + padding);
            }

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
                Debug.LogError(
                    "Mesh Combiner: \"" + meshFilter.name + "\" has " + mesh.subMeshCount +
                    " submeshes but only " + materials.Length +
                    " materials. Fix the renderer or use a valid material layout before combining.",
                    meshFilter);
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
                    Debug.LogError(
                        "Mesh Combiner: Single Material mode found more than one material. " +
                        "Enable Create Multi-Material Mesh to preserve all materials.",
                        meshFilter);
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
                Debug.LogError(
                    "Mesh Combiner: \"" + meshFilter.name + "\" has " + mesh.subMeshCount +
                    " submeshes but only " + materials.Length + " materials.",
                    meshFilter);
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
            foreach (Mesh temporaryMesh in temporarySubMeshes)
            {
                DestroyTemporaryMesh(temporaryMesh);
            }
        }
    }

    private static Mesh CreateOutputMesh(long outputVertexUpperBound)
    {
        Mesh mesh = new Mesh();
        if (outputVertexUpperBound > Mesh16BitBufferVertexLimit)
        {
            mesh.indexFormat = IndexFormat.UInt32;
        }
        return mesh;
    }

    private bool GenerateUVIfRequested(Mesh combinedMesh)
    {
        if (lightmapUvMode != LightmapUvMode.RegenerateUv2)
        {
            return true;
        }

#if UNITY_EDITOR
        UnityEditor.UnwrapParam unwrapParam;
        UnityEditor.UnwrapParam.SetDefaults(out unwrapParam);
        bool success = UnityEditor.Unwrapping.GenerateSecondaryUVSet(combinedMesh, unwrapParam);
        if (!success)
        {
            Debug.LogError(
                "Mesh Combiner: Unity failed to regenerate UV2 for \"" + combinedMesh.name +
                "\". The output mesh was not assigned.",
                this);
        }
        return success;
#else
        Debug.LogError(
            "Mesh Combiner: Regenerate UV2 is Editor-only. Use None/Preserve modes for runtime combining.",
            this);
        return false;
#endif
    }

    private void PrepareDestinationForLightmapping(MeshRenderer destinationMeshRenderer)
    {
#if UNITY_EDITOR
        if (lightmapUvMode == LightmapUvMode.None || Application.isPlaying)
        {
            return;
        }

        UnityEditor.StaticEditorFlags staticFlags =
            UnityEditor.GameObjectUtility.GetStaticEditorFlags(gameObject) |
            UnityEditor.StaticEditorFlags.ContributeGI;

        UnityEditor.GameObjectUtility.SetStaticEditorFlags(gameObject, staticFlags);
        destinationMeshRenderer.receiveGI = ReceiveGI.Lightmaps;
        destinationMeshRenderer.stitchLightmapSeams = true;
#endif
    }

    private void UpdateMeshCollider(Mesh combinedMesh)
    {
        if (!updateOrCreateMeshCollider)
        {
            return;
        }

        MeshCollider meshCollider = GetComponent<MeshCollider>();
        if (meshCollider == null)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                meshCollider = UnityEditor.Undo.AddComponent<MeshCollider>(gameObject);
            }
            else
#endif
            {
                meshCollider = gameObject.AddComponent<MeshCollider>();
            }
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
            if (meshFilter == null)
            {
                continue;
            }

            if (destroyCombinedChildren)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.Undo.DestroyObjectImmediate(meshFilter.gameObject);
                }
                else
#endif
                {
                    Destroy(meshFilter.gameObject);
                }
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
                if (meshRenderer != null)
                {
                    meshRenderer.enabled = false;
                }
            }
        }
    }

    private string BuildLightmapResultMessage(int addedVertices)
    {
        switch (lightmapUvMode)
        {
            case LightmapUvMode.None:
                return ", no lightmap UV processing";
            case LightmapUvMode.PreserveSourceUv2:
                return ", source UV2 preserved without repacking";
            case LightmapUvMode.PreserveAndRepackSourceUv2:
                return ", " + _lastRepackChartCount + " source UV2 charts repacked with one global scale " +
                       _lastRepackScale.ToString("0.###") + " and " + repackPaddingTexels + " texel padding @ " +
                       repackPaddingReferenceResolution + " reference";
            case LightmapUvMode.RegenerateUv2:
                return ", UV2 regenerated (+" + Mathf.Max(0, addedVertices) + " vertices from chart splits)";
            default:
                return string.Empty;
        }
    }

    private static long CountTriangles(Mesh mesh)
    {
        long triangleCount = 0;
        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) == MeshTopology.Triangles)
            {
                triangleCount += (long)mesh.GetIndexCount(subMeshIndex) / 3L;
            }
        }
        return triangleCount;
    }

    private void LogSkippedInputs(string reason, List<string> names)
    {
        if (names.Count == 0)
        {
            return;
        }

        Debug.LogWarning(
            "Mesh Combiner: skipped " + names.Count + " child objects with " + reason + ". Examples: " +
            FormatValidationNames(names) + ".",
            this);
    }

    private static string FormatValidationNames(List<string> names)
    {
        int visibleCount = Mathf.Min(names.Count, ValidationLogNameLimit);
        string value = string.Join(", ", names.Take(visibleCount).ToArray());
        if (names.Count > visibleCount)
        {
            value += ", +" + (names.Count - visibleCount) + " more";
        }
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
        if (mesh == null)
        {
            return;
        }

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
