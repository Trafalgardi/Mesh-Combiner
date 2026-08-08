using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

internal enum MeshCombinerWorkflowState
{
    Ready,
    Combined,
    OutputWithoutSnapshot,
    InterruptedRecoverable
}

internal static class MeshCombinerEditorState
{
    private const string RestoreStateKeyPrefix = "Trafalgardi.MeshCombiner.RestoreState.";
    private const string CombinedStateKeyPrefix = "Trafalgardi.MeshCombiner.CombinedState.";

    [Serializable]
    private sealed class RestoreSnapshot
    {
        public List<GameObjectState> gameObjects = new List<GameObjectState>();
        public List<RendererState> renderers = new List<RendererState>();
    }

    [Serializable]
    private sealed class GameObjectState
    {
        public int instanceId;
        public string globalObjectId;
        public bool activeSelf;
    }

    [Serializable]
    private sealed class RendererState
    {
        public int instanceId;
        public string globalObjectId;
        public bool enabled;
    }

    public static MeshCombinerWorkflowState GetWorkflowState(MeshCombiner combiner, MeshFilter destinationMeshFilter)
    {
        bool hasOutput = destinationMeshFilter != null && destinationMeshFilter.sharedMesh != null;
        bool hasSnapshot = HasRestoreSnapshot(combiner);
        bool markedCombined = IsMarkedCombined(combiner);

        if (hasOutput && hasSnapshot)
        {
            // Backward-compatible migration: older dev builds already had the snapshot but not the explicit marker.
            MarkCombined(combiner);
            return MeshCombinerWorkflowState.Combined;
        }

        if (hasOutput && markedCombined) return MeshCombinerWorkflowState.OutputWithoutSnapshot;
        if (hasSnapshot && !hasOutput) return MeshCombinerWorkflowState.InterruptedRecoverable;

        // A pre-existing Mesh on the root is not automatically considered combined output.
        return MeshCombinerWorkflowState.Ready;
    }

    public static bool HasBakedLightmap(MeshRenderer renderer, out int lightmapIndex)
    {
        lightmapIndex = renderer != null ? renderer.lightmapIndex : -1;
        if (renderer == null || lightmapIndex < 0) return false;

        LightmapData[] lightmaps = LightmapSettings.lightmaps;
        return lightmaps != null && lightmapIndex < lightmaps.Length && lightmaps[lightmapIndex] != null;
    }

    public static void CaptureAndSaveSnapshot(MeshCombiner combiner, MeshFilter destinationMeshFilter)
    {
        RestoreSnapshot snapshot = CaptureSnapshot(combiner, destinationMeshFilter);
        EditorPrefs.SetString(GetRestoreStateKey(combiner), JsonUtility.ToJson(snapshot));
    }

    public static void MarkCombined(MeshCombiner combiner)
    {
        if (combiner == null) return;
        EditorPrefs.SetBool(GetCombinedStateKey(combiner), true);
    }

    public static bool IsMarkedCombined(MeshCombiner combiner)
    {
        return combiner != null && EditorPrefs.GetBool(GetCombinedStateKey(combiner), false);
    }

    public static bool HasRestoreSnapshot(MeshCombiner combiner)
    {
        if (combiner == null) return false;
        return !string.IsNullOrEmpty(EditorPrefs.GetString(GetRestoreStateKey(combiner), string.Empty));
    }

    public static void ClearRestoreSnapshot(MeshCombiner combiner)
    {
        if (combiner == null) return;
        EditorPrefs.DeleteKey(GetRestoreStateKey(combiner));
    }

    public static void ClearWorkflowMarkers(MeshCombiner combiner)
    {
        if (combiner == null) return;
        EditorPrefs.DeleteKey(GetCombinedStateKey(combiner));
    }

    public static bool RestoreExactState(
        MeshCombiner combiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer)
    {
        if (!TryGetSnapshot(combiner, out RestoreSnapshot snapshot)) return false;

        Mesh generatedMesh = destinationMeshFilter != null ? destinationMeshFilter.sharedMesh : null;
        Undo.RegisterFullObjectHierarchyUndo(combiner.gameObject, "Restore Mesh Combine Sources");
        ClearCombinedOutput(combiner, destinationMeshFilter, destinationMeshRenderer);

        int restoredGameObjects = 0;
        int missingGameObjects = 0;
        foreach (GameObjectState state in snapshot.gameObjects)
        {
            GameObject sourceGameObject = ResolveObject<GameObject>(state.instanceId, state.globalObjectId);
            if (sourceGameObject == null)
            {
                missingGameObjects++;
                continue;
            }

            if (sourceGameObject.activeSelf != state.activeSelf)
            {
                sourceGameObject.SetActive(state.activeSelf);
                restoredGameObjects++;
            }
        }

        int restoredRenderers = 0;
        int missingRenderers = 0;
        foreach (RendererState state in snapshot.renderers)
        {
            MeshRenderer sourceRenderer = ResolveObject<MeshRenderer>(state.instanceId, state.globalObjectId);
            if (sourceRenderer == null)
            {
                missingRenderers++;
                continue;
            }

            if (sourceRenderer.enabled != state.enabled)
            {
                sourceRenderer.enabled = state.enabled;
                restoredRenderers++;
            }
        }

        ClearRestoreSnapshot(combiner);
        ClearWorkflowMarkers(combiner);
        DestroyTransientCombinedMesh(generatedMesh);

        EditorUtility.SetDirty(combiner);
        if (destinationMeshFilter != null) EditorUtility.SetDirty(destinationMeshFilter);
        if (destinationMeshRenderer != null) EditorUtility.SetDirty(destinationMeshRenderer);

        Debug.Log(
            "Mesh Combiner: restored exact pre-combine state for \"" + combiner.name + "\". Changed " +
            restoredGameObjects + " GameObject active states and " + restoredRenderers +
            " MeshRenderer enabled states. Missing references: " +
            (missingGameObjects + missingRenderers) + ".",
            combiner);
        return true;
    }

    public static void ForceRecover(
        MeshCombiner combiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer)
    {
        Mesh generatedMesh = destinationMeshFilter != null ? destinationMeshFilter.sharedMesh : null;
        Undo.RegisterFullObjectHierarchyUndo(combiner.gameObject, "Force Recover Mesh Combine Sources");
        ClearCombinedOutput(combiner, destinationMeshFilter, destinationMeshRenderer);

        int activatedGameObjects = 0;
        int enabledRenderers = 0;
        MeshFilter[] sourceMeshFilters = combiner.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter sourceMeshFilter in sourceMeshFilters)
        {
            if (sourceMeshFilter == null || sourceMeshFilter == destinationMeshFilter) continue;

            GameObject sourceGameObject = sourceMeshFilter.gameObject;
            if (!sourceGameObject.activeSelf)
            {
                sourceGameObject.SetActive(true);
                activatedGameObjects++;
            }

            MeshRenderer sourceRenderer = sourceMeshFilter.GetComponent<MeshRenderer>();
            if (sourceRenderer != null && !sourceRenderer.enabled)
            {
                sourceRenderer.enabled = true;
                enabledRenderers++;
            }
        }

        ClearRestoreSnapshot(combiner);
        ClearWorkflowMarkers(combiner);
        DestroyTransientCombinedMesh(generatedMesh);

        EditorUtility.SetDirty(combiner);
        if (destinationMeshFilter != null) EditorUtility.SetDirty(destinationMeshFilter);
        if (destinationMeshRenderer != null) EditorUtility.SetDirty(destinationMeshRenderer);

        Debug.LogWarning(
            "Mesh Combiner: FORCE RECOVERY completed for \"" + combiner.name + "\". Activated " +
            activatedGameObjects + " child GameObjects and enabled " + enabledRenderers +
            " MeshRenderers. Because no exact snapshot was used, previously disabled helpers/variants may also be enabled.",
            combiner);
    }

    public static void DestroyTransientCombinedMesh(Mesh generatedMesh)
    {
        if (generatedMesh != null && !AssetDatabase.Contains(generatedMesh))
            Undo.DestroyObjectImmediate(generatedMesh);
    }

    private static RestoreSnapshot CaptureSnapshot(MeshCombiner combiner, MeshFilter destinationMeshFilter)
    {
        RestoreSnapshot snapshot = new RestoreSnapshot();
        HashSet<int> capturedGameObjects = new HashSet<int>();
        HashSet<int> capturedRenderers = new HashSet<int>();

        MeshFilter[] sourceMeshFilters = combiner.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter sourceMeshFilter in sourceMeshFilters)
        {
            if (sourceMeshFilter == null || sourceMeshFilter == destinationMeshFilter) continue;

            GameObject sourceGameObject = sourceMeshFilter.gameObject;
            int gameObjectId = sourceGameObject.GetInstanceID();
            if (capturedGameObjects.Add(gameObjectId))
            {
                snapshot.gameObjects.Add(new GameObjectState
                {
                    instanceId = gameObjectId,
                    globalObjectId = GetStableObjectId(sourceGameObject),
                    activeSelf = sourceGameObject.activeSelf
                });
            }

            MeshRenderer sourceRenderer = sourceMeshFilter.GetComponent<MeshRenderer>();
            if (sourceRenderer == null) continue;

            int rendererId = sourceRenderer.GetInstanceID();
            if (capturedRenderers.Add(rendererId))
            {
                snapshot.renderers.Add(new RendererState
                {
                    instanceId = rendererId,
                    globalObjectId = GetStableObjectId(sourceRenderer),
                    enabled = sourceRenderer.enabled
                });
            }
        }

        return snapshot;
    }

    private static bool TryGetSnapshot(MeshCombiner combiner, out RestoreSnapshot snapshot)
    {
        string json = EditorPrefs.GetString(GetRestoreStateKey(combiner), string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            snapshot = null;
            return false;
        }

        snapshot = JsonUtility.FromJson<RestoreSnapshot>(json);
        return snapshot != null;
    }

    private static void ClearCombinedOutput(
        MeshCombiner combiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer)
    {
        Mesh combinedMesh = destinationMeshFilter != null ? destinationMeshFilter.sharedMesh : null;
        if (destinationMeshFilter != null) destinationMeshFilter.sharedMesh = null;
        if (destinationMeshRenderer != null) destinationMeshRenderer.sharedMaterials = Array.Empty<Material>();

        MeshCollider meshCollider = combiner.GetComponent<MeshCollider>();
        if (meshCollider != null && (combinedMesh == null || meshCollider.sharedMesh == combinedMesh))
            meshCollider.sharedMesh = null;
    }

    private static string GetRestoreStateKey(MeshCombiner combiner)
    {
        return RestoreStateKeyPrefix + GetObjectScopedKey(combiner);
    }

    private static string GetCombinedStateKey(MeshCombiner combiner)
    {
        return CombinedStateKeyPrefix + GetObjectScopedKey(combiner);
    }

    private static string GetObjectScopedKey(MeshCombiner combiner)
    {
        string projectKey = Hash128.Compute(Application.dataPath).ToString();
        string objectKey = GetStableObjectId(combiner);
        if (string.IsNullOrEmpty(objectKey)) objectKey = "instance-" + combiner.GetInstanceID();
        return projectKey + "." + objectKey;
    }

    private static string GetStableObjectId(UnityEngine.Object target)
    {
        if (target == null) return string.Empty;
        GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(target);
        return id.Equals(default(GlobalObjectId)) ? string.Empty : id.ToString();
    }

    private static T ResolveObject<T>(int instanceId, string globalObjectId) where T : UnityEngine.Object
    {
        T currentSessionObject = EditorUtility.InstanceIDToObject(instanceId) as T;
        if (currentSessionObject != null) return currentSessionObject;

        if (string.IsNullOrEmpty(globalObjectId) ||
            !GlobalObjectId.TryParse(globalObjectId, out GlobalObjectId parsedId))
        {
            return null;
        }

        return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsedId) as T;
    }
}
