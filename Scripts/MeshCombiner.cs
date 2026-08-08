using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MeshCombiner : MonoBehaviour
{
    private const int Mesh16BitBufferVertexLimit = 65535;
    private const int ValidationLogNameLimit = 6;

    [Header("Combine")]
    [SerializeField] private bool createMultiMaterialMesh;
    [SerializeField] private bool combineInactiveChildren;
    [SerializeField] private MeshFilter[] meshFiltersToSkip = new MeshFilter[0];

    [Header("Output")]
    [SerializeField] private bool deactivateCombinedChildren = true;
    [SerializeField] private bool deactivateCombinedChildrenMeshRenderers;
    [SerializeField] private bool updateOrCreateMeshCollider;
    [SerializeField] private bool generateUVMap;
    [SerializeField] private bool destroyCombinedChildren;

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

    public bool GenerateUVMap
    {
        get { return generateUVMap; }
        set { generateUVMap = value; }
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

    /// <summary>
    /// Combines child MeshFilters into the MeshFilter on this GameObject.
    /// Existing callers can keep using this method; use TryCombineMeshes when a success result is needed.
    /// </summary>
    public void CombineMeshes(bool showCreatedMeshInfo)
    {
        TryCombineMeshes(showCreatedMeshInfo);
    }

    /// <summary>
    /// Combines child MeshFilters into the MeshFilter on this GameObject.
    /// Returns false when the input is invalid or the mesh cannot be combined safely.
    /// </summary>
    public bool TryCombineMeshes(bool showCreatedMeshInfo)
    {
        List<MeshFilter> sourceMeshFilters;
        if (!TryGetMeshFiltersToCombine(out sourceMeshFilters))
        {
            return false;
        }

        Mesh combinedMesh;
        List<Material> outputMaterials;
        bool combined = createMultiMaterialMesh
            ? TryCombineMultiMaterial(sourceMeshFilters, out combinedMesh, out outputMaterials)
            : TryCombineSingleMaterial(sourceMeshFilters, out combinedMesh, out outputMaterials);

        if (!combined || combinedMesh == null)
        {
            return false;
        }

        combinedMesh.name = name;
        combinedMesh.RecalculateBounds();

        bool uvGenerated = GenerateUV(combinedMesh);

        MeshFilter destinationMeshFilter = GetComponent<MeshFilter>();
        MeshRenderer destinationMeshRenderer = GetComponent<MeshRenderer>();
        destinationMeshFilter.sharedMesh = combinedMesh;
        destinationMeshRenderer.sharedMaterials = outputMaterials.ToArray();

        PrepareDestinationForLightmapping(destinationMeshRenderer);
        UpdateMeshCollider(combinedMesh);
        DeactivateCombinedGameObjects(sourceMeshFilters);

        if (showCreatedMeshInfo)
        {
            string uvMessage = generateUVMap
                ? (uvGenerated ? ", lightmap UV2 generated" : ", lightmap UV2 generation failed")
                : string.Empty;

            Debug.Log(
                "<color=#00cc00><b>Mesh \"" + name + "\" was created from " + sourceMeshFilters.Count +
                " child meshes, " + combinedMesh.subMeshCount + " submeshes and " + combinedMesh.vertexCount +
                " final vertices" + uvMessage + ".</b></color>",
                this);
        }

        return true;
    }

    /// <summary>
    /// Clears the combined output and makes the source hierarchy visible again.
    /// Saved Mesh assets are not deleted. This intentionally restores all descendants because
    /// it is designed as a quick editor iteration command after a combine operation.
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

        int activatedObjects = 0;
        Transform[] descendants = GetComponentsInChildren<Transform>(true);
        foreach (Transform descendant in descendants)
        {
            if (descendant == null || descendant == transform)
            {
                continue;
            }

            if (!descendant.gameObject.activeSelf)
            {
                descendant.gameObject.SetActive(true);
                activatedObjects++;
            }
        }

        int enabledRenderers = 0;
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer renderer in renderers)
        {
            if (renderer == null || renderer == destinationMeshRenderer)
            {
                continue;
            }

            if (!renderer.enabled)
            {
                renderer.enabled = true;
                enabledRenderers++;
            }
        }

        if (showInfo)
        {
            Debug.Log(
                "Mesh Combiner: restored source hierarchy for \"" + name + "\". Cleared combined mesh/materials, activated " +
                activatedObjects + " child GameObjects and re-enabled " + enabledRenderers + " child MeshRenderers.",
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

    private bool TryCombineSingleMaterial(
        List<MeshFilter> meshFilters,
        out Mesh combinedMesh,
        out List<Material> outputMaterials)
    {
        combinedMesh = null;
        outputMaterials = new List<Material>();

        List<CombineInstance> combineInstances = new List<CombineInstance>();
        Material sharedMaterial = null;
        bool materialInitialized = false;
        long estimatedVertexCount = 0;
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

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

                combineInstances.Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = subMeshIndex,
                    transform = worldToLocal * meshFilter.transform.localToWorldMatrix
                });
                estimatedVertexCount += mesh.vertexCount;
            }
        }

        if (combineInstances.Count == 0)
        {
            Debug.LogError("Mesh Combiner: no submeshes were found to combine.", this);
            return false;
        }

        combinedMesh = CreateOutputMesh(estimatedVertexCount, generateUVMap);
        combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true, false);
        outputMaterials.Add(sharedMaterial);
        return true;
    }

    private bool TryCombineMultiMaterial(
        List<MeshFilter> meshFilters,
        out Mesh combinedMesh,
        out List<Material> outputMaterials)
    {
        combinedMesh = null;
        outputMaterials = new List<Material>();

        List<List<CombineInstance>> instancesByMaterial = new List<List<CombineInstance>>();
        List<long> estimatedVerticesByMaterial = new List<long>();
        Matrix4x4 worldToLocal = transform.worldToLocalMatrix;

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
                    estimatedVerticesByMaterial.Add(0);
                }

                instancesByMaterial[materialIndex].Add(new CombineInstance
                {
                    mesh = mesh,
                    subMeshIndex = subMeshIndex,
                    transform = worldToLocal * meshFilter.transform.localToWorldMatrix
                });

                // This is intentionally an upper bound. A mesh can contribute several submeshes,
                // so the same source vertices may be counted more than once. Overestimating is safe
                // because it only selects a 32-bit index buffer earlier.
                estimatedVerticesByMaterial[materialIndex] += mesh.vertexCount;
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
            for (int materialIndex = 0; materialIndex < outputMaterials.Count; materialIndex++)
            {
                Mesh materialMesh = CreateOutputMesh(estimatedVerticesByMaterial[materialIndex], false);
                materialMesh.name = name + "_Material_" + materialIndex;
                materialMesh.CombineMeshes(instancesByMaterial[materialIndex].ToArray(), true, true, false);

                temporarySubMeshes.Add(materialMesh);
                finalCombineInstances.Add(new CombineInstance
                {
                    mesh = materialMesh,
                    subMeshIndex = 0,
                    transform = Matrix4x4.identity
                });
            }

            long finalEstimatedVertexCount = 0;
            foreach (Mesh temporarySubMesh in temporarySubMeshes)
            {
                finalEstimatedVertexCount += temporarySubMesh.vertexCount;
            }

            combinedMesh = CreateOutputMesh(finalEstimatedVertexCount, generateUVMap);
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

    private Mesh CreateOutputMesh(long estimatedVertexCount, bool reserveForUvUnwrap)
    {
        Mesh mesh = new Mesh();

        if (reserveForUvUnwrap || estimatedVertexCount > Mesh16BitBufferVertexLimit)
        {
            mesh.indexFormat = IndexFormat.UInt32;
        }

        return mesh;
    }

    private bool GenerateUV(Mesh combinedMesh)
    {
        if (!generateUVMap)
        {
            return true;
        }

#if UNITY_EDITOR
        // CreateOutputMesh selected UInt32 before CombineMeshes when UV generation was requested.
        // Do not change indexFormat here: changing it after indices exist can invalidate mesh data.
        UnityEditor.UnwrapParam unwrapParam;
        UnityEditor.UnwrapParam.SetDefaults(out unwrapParam);

        bool success = UnityEditor.Unwrapping.GenerateSecondaryUVSet(combinedMesh, unwrapParam);
        if (!success)
        {
            Debug.LogError(
                "Mesh Combiner: Unity failed to generate secondary UVs for \"" + combinedMesh.name + "\".",
                this);
        }

        return success;
#else
        Debug.LogWarning(
            "Mesh Combiner: lightmap UV2 generation is Editor-only. The mesh was combined without generating UV2.",
            this);
        return false;
#endif
    }

    private void PrepareDestinationForLightmapping(MeshRenderer destinationMeshRenderer)
    {
#if UNITY_EDITOR
        if (!generateUVMap || Application.isPlaying)
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

        // Clearing first forces Unity/PhysX to recook the collider when the same Mesh object
        // is regenerated or changed.
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
