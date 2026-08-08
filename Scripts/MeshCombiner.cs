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
    [SerializeField, Range(0f, 0.02f)] private float repackUvPadding = 0.002f;

    [Header("Asset")]
    [SerializeField] private string folderPath = "Prefabs/CombinedMeshes";

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

    public float RepackUvPadding
    {
        get { return repackUvPadding; }
        set { repackUvPadding = Mathf.Clamp(value, 0f, 0.02f); }
    }

    // Backwards-compatible API for callers that used the previous Unity 6 fork property.
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

        Dictionary<MeshFilter, Vector4> lightmapScaleOffsets;
        if (!TryBuildLightmapScaleOffsets(sourceMeshFilters, out lightmapScaleOffsets))
        {
            return false;
        }

        Mesh combinedMesh;
        List<Material> outputMaterials;
        bool combined = createMultiMaterialMesh
            ? TryCombineMultiMaterial(sourceMeshFilters, lightmapScaleOffsets, out combinedMesh, out outputMaterials)
            : TryCombineSingleMaterial(sourceMeshFilters, lightmapScaleOffsets, out combinedMesh, out outputMaterials);

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

    private bool TryBuildLightmapScaleOffsets(
        List<MeshFilter> meshFilters,
        out Dictionary<MeshFilter, Vector4> lightmapScaleOffsets)
    {
        lightmapScaleOffsets = new Dictionary<MeshFilter, Vector4>();

        if (lightmapUvMode != LightmapUvMode.PreserveAndRepackSourceUv2)
        {
            return true;
        }

        List<string> invalidUv2Names = new List<string>();
        Dictionary<MeshFilter, Rect> uvBoundsByMeshFilter = new Dictionary<MeshFilter, Rect>();

        foreach (MeshFilter meshFilter in meshFilters)
        {
            Mesh mesh = meshFilter.sharedMesh;
            Vector2[] uv2 = mesh.uv2;

            if (uv2 == null || uv2.Length != mesh.vertexCount || uv2.Length == 0)
            {
                invalidUv2Names.Add(meshFilter.name + " [" + mesh.name + "]");
                continue;
            }

            Vector2 min = uv2[0];
            Vector2 max = uv2[0];
            for (int i = 1; i < uv2.Length; i++)
            {
                min = Vector2.Min(min, uv2[i]);
                max = Vector2.Max(max, uv2[i]);
            }

            Vector2 size = max - min;
            if (size.x <= MinimumUvExtent || size.y <= MinimumUvExtent)
            {
                invalidUv2Names.Add(meshFilter.name + " [" + mesh.name + ": degenerate UV2]");
                continue;
            }

            uvBoundsByMeshFilter.Add(meshFilter, new Rect(min, size));
        }

        if (invalidUv2Names.Count > 0)
        {
            Debug.LogError(
                "Mesh Combiner: Preserve & Repack Source UV2 requires a valid UV2 channel on every source mesh. " +
                invalidUv2Names.Count + " source meshes are missing or have degenerate UV2. Examples: " +
                FormatValidationNames(invalidUv2Names) +
                ". Generate lightmap UVs on the source models or switch Lightmap UV Mode to Regenerate UV2.",
                this);
            return false;
        }

        int columns = Mathf.CeilToInt(Mathf.Sqrt(meshFilters.Count));
        int rows = Mathf.CeilToInt(meshFilters.Count / (float)columns);
        float cellWidth = 1f / columns;
        float cellHeight = 1f / rows;
        float padding = Mathf.Clamp(repackUvPadding, 0f, Mathf.Min(cellWidth, cellHeight) * 0.45f);

        for (int i = 0; i < meshFilters.Count; i++)
        {
            MeshFilter meshFilter = meshFilters[i];
            Rect sourceBounds = uvBoundsByMeshFilter[meshFilter];
            int column = i % columns;
            int row = i / columns;

            float targetMinX = column * cellWidth + padding;
            float targetMinY = row * cellHeight + padding;
            float targetWidth = cellWidth - padding * 2f;
            float targetHeight = cellHeight - padding * 2f;

            if (targetWidth <= MinimumUvExtent || targetHeight <= MinimumUvExtent)
            {
                Debug.LogError(
                    "Mesh Combiner: UV2 repack padding is too large for " + meshFilters.Count +
                    " source meshes. Lower Repack Padding.",
                    this);
                return false;
            }

            float uniformScale = Mathf.Min(targetWidth / sourceBounds.width, targetHeight / sourceBounds.height);
            float mappedWidth = sourceBounds.width * uniformScale;
            float mappedHeight = sourceBounds.height * uniformScale;
            float centeredMinX = targetMinX + (targetWidth - mappedWidth) * 0.5f;
            float centeredMinY = targetMinY + (targetHeight - mappedHeight) * 0.5f;
            float offsetX = centeredMinX - sourceBounds.xMin * uniformScale;
            float offsetY = centeredMinY - sourceBounds.yMin * uniformScale;

            lightmapScaleOffsets.Add(
                meshFilter,
                new Vector4(uniformScale, uniformScale, offsetX, offsetY));
        }

        return true;
    }

    private bool TryCombineSingleMaterial(
        List<MeshFilter> meshFilters,
        Dictionary<MeshFilter, Vector4> lightmapScaleOffsets,
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
        bool applyLightmapScaleOffsets = lightmapUvMode == LightmapUvMode.PreserveAndRepackSourceUv2;

        foreach (MeshFilter meshFilter in meshFilters)
        {
            Mesh mesh = meshFilter.sharedMesh;
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

                CombineInstance instance = new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = subMeshIndex,
                    transform = worldToLocal * meshFilter.transform.localToWorldMatrix
                };

                if (applyLightmapScaleOffsets)
                {
                    instance.lightmapScaleOffset = lightmapScaleOffsets[meshFilter];
                }

                combineInstances.Add(instance);
                outputVertexUpperBound += (long)mesh.GetIndexCount(subMeshIndex);
            }
        }

        if (combineInstances.Count == 0)
        {
            Debug.LogError("Mesh Combiner: no submeshes were found to combine.", this);
            return false;
        }

        combinedMesh = CreateOutputMesh(outputVertexUpperBound);
        combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true, applyLightmapScaleOffsets);
        outputMaterials.Add(sharedMaterial);
        return true;
    }

    private bool TryCombineMultiMaterial(
        List<MeshFilter> meshFilters,
        Dictionary<MeshFilter, Vector4> lightmapScaleOffsets,
        out Mesh combinedMesh,
        out List<Material> outputMaterials)
    {
        combinedMesh = null;
        outputMaterials = new List<Material>();
        List<List<CombineInstance>> instancesByMaterial = new List<List<CombineInstance>>();
        List<long> vertexUpperBoundByMaterial = new List<long>();
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
        bool applyLightmapScaleOffsets = lightmapUvMode == LightmapUvMode.PreserveAndRepackSourceUv2;

        foreach (MeshFilter meshFilter in meshFilters)
        {
            Mesh mesh = meshFilter.sharedMesh;
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

                CombineInstance instance = new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = subMeshIndex,
                    transform = worldToLocal * meshFilter.transform.localToWorldMatrix
                };

                if (applyLightmapScaleOffsets)
                {
                    instance.lightmapScaleOffset = lightmapScaleOffsets[meshFilter];
                }

                instancesByMaterial[materialIndex].Add(instance);
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
                materialMesh.CombineMeshes(
                    instancesByMaterial[materialIndex].ToArray(),
                    true,
                    true,
                    applyLightmapScaleOffsets);

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
                return ", source UV2 preserved and repacked without regenerating charts";
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
