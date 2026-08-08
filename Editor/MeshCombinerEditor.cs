using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MeshCombiner))]
public class MeshCombinerEditor : Editor
{
    private const string RestoreStateKeyPrefix = "Trafalgardi.MeshCombiner.RestoreState.";

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
        public bool activeSelf;
    }

    [Serializable]
    private sealed class RendererState
    {
        public int instanceId;
        public bool enabled;
    }

    private SerializedProperty _createMultiMaterialMesh;
    private SerializedProperty _combineInactiveChildren;
    private SerializedProperty _meshFiltersToSkip;
    private SerializedProperty _deactivateCombinedChildren;
    private SerializedProperty _deactivateCombinedChildrenMeshRenderers;
    private SerializedProperty _updateOrCreateMeshCollider;
    private SerializedProperty _generateUVMap;
    private SerializedProperty _destroyCombinedChildren;
    private SerializedProperty _folderPath;

    private void OnEnable()
    {
        _createMultiMaterialMesh = serializedObject.FindProperty("createMultiMaterialMesh");
        _combineInactiveChildren = serializedObject.FindProperty("combineInactiveChildren");
        _meshFiltersToSkip = serializedObject.FindProperty("meshFiltersToSkip");
        _deactivateCombinedChildren = serializedObject.FindProperty("deactivateCombinedChildren");
        _deactivateCombinedChildrenMeshRenderers = serializedObject.FindProperty("deactivateCombinedChildrenMeshRenderers");
        _updateOrCreateMeshCollider = serializedObject.FindProperty("updateOrCreateMeshCollider");
        _generateUVMap = serializedObject.FindProperty("generateUVMap");
        _destroyCombinedChildren = serializedObject.FindProperty("destroyCombinedChildren");
        _folderPath = serializedObject.FindProperty("folderPath");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        MeshCombiner meshCombiner = (MeshCombiner)target;
        MeshFilter meshFilter = meshCombiner.GetComponent<MeshFilter>();
        MeshRenderer meshRenderer = meshCombiner.GetComponent<MeshRenderer>();
        Mesh mesh = meshFilter.sharedMesh;

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField(
                "Script",
                MonoScript.FromMonoBehaviour(meshCombiner),
                typeof(MeshCombiner),
                false);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Combine", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            _createMultiMaterialMesh,
            new GUIContent(
                "Create Multi-Material Mesh",
                "Preserves different materials as submeshes. Disable only when all source submeshes use the same material."));
        EditorGUILayout.PropertyField(_combineInactiveChildren, new GUIContent("Combine Inactive Children"));
        EditorGUILayout.PropertyField(
            _meshFiltersToSkip,
            new GUIContent("Mesh Filters To Skip"),
            true);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            _deactivateCombinedChildren,
            new GUIContent("Deactivate Combined Children"));
        EditorGUILayout.PropertyField(
            _deactivateCombinedChildrenMeshRenderers,
            new GUIContent("Disable Child MeshRenderers"));
        EditorGUILayout.PropertyField(
            _updateOrCreateMeshCollider,
            new GUIContent(
                "Update/Create MeshCollider",
                "Assigns the final combined render mesh to a MeshCollider on this GameObject. " +
                "This fixes the upstream 'Missing collider' case, but does not combine custom primitive colliders."));
        EditorGUILayout.PropertyField(
            _generateUVMap,
            new GUIContent(
                "Generate Lightmap UV2",
                "Generates a fresh secondary UV set after combining. " +
                "The destination is also marked Contribute GI and Receive GI = Lightmaps in Edit Mode."));

        EditorGUILayout.PropertyField(
            _destroyCombinedChildren,
            new GUIContent(
                "Destroy Combined Children",
                "Destructive mode. Prefer deactivating the source hierarchy so the operation remains reversible."));

        if (_destroyCombinedChildren.boolValue)
        {
            _deactivateCombinedChildren.boolValue = false;
            _deactivateCombinedChildrenMeshRenderers.boolValue = false;
        }

        if (_destroyCombinedChildren.boolValue)
        {
            EditorGUILayout.HelpBox(
                "Destructive mode is enabled. Restore / Undo Combine cannot reconstruct destroyed child objects. " +
                "Use Unity Undo immediately, or keep source objects deactivated instead.",
                MessageType.Warning);
        }

        if (_updateOrCreateMeshCollider.boolValue &&
            !_deactivateCombinedChildren.boolValue &&
            _deactivateCombinedChildrenMeshRenderers.boolValue)
        {
            EditorGUILayout.HelpBox(
                "Child GameObjects stay active, so their existing Colliders also stay active. " +
                "Adding a combined MeshCollider can create duplicate collision. Disable/remove child Colliders yourself if that is not intended.",
                MessageType.Warning);
        }

        if (_generateUVMap.boolValue)
        {
            EditorGUILayout.HelpBox(
                "UV2 generation uses a 32-bit index buffer before unwrapping. Unity can split vertices while generating lightmap charts, " +
                "so this avoids the 65,535 vertex failure mode.",
                MessageType.Info);
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        bool hasRestoreSnapshot = HasRestoreSnapshot(meshCombiner);
        bool destructiveMode = meshCombiner.DestroyCombinedChildren;

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(hasRestoreSnapshot))
            {
                if (GUILayout.Button("Combine Meshes", GUILayout.Height(28)))
                {
                    RestoreSnapshot restoreSnapshot = CaptureRestoreSnapshot(meshCombiner, meshFilter);

                    Undo.RegisterFullObjectHierarchyUndo(meshCombiner.gameObject, "Combine Meshes");

                    if (meshCombiner.TryCombineMeshes(true))
                    {
                        if (!destructiveMode)
                        {
                            SaveRestoreSnapshot(meshCombiner, restoreSnapshot);
                        }
                        else
                        {
                            ClearRestoreSnapshot(meshCombiner);
                        }

                        EditorUtility.SetDirty(meshCombiner);
                        EditorUtility.SetDirty(meshFilter);
                        EditorUtility.SetDirty(meshRenderer);
                    }
                    else
                    {
                        ClearRestoreSnapshot(meshCombiner);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!hasRestoreSnapshot || destructiveMode))
            {
                if (GUILayout.Button("Restore / Undo Combine", GUILayout.Height(28)))
                {
                    RestoreSnapshot restoreSnapshot;
                    if (TryGetRestoreSnapshot(meshCombiner, out restoreSnapshot))
                    {
                        Mesh generatedMesh = meshFilter.sharedMesh;

                        Undo.RegisterFullObjectHierarchyUndo(meshCombiner.gameObject, "Restore Mesh Combine Sources");
                        RestoreExactState(meshCombiner, meshFilter, meshRenderer, restoreSnapshot);
                        ClearRestoreSnapshot(meshCombiner);

                        // Saved .asset meshes stay in the Project. Only temporary generated Mesh objects
                        // are removed so repeated Combine -> Restore tests do not accumulate orphan meshes.
                        if (generatedMesh != null && !AssetDatabase.Contains(generatedMesh))
                        {
                            Undo.DestroyObjectImmediate(generatedMesh);
                        }

                        EditorUtility.SetDirty(meshCombiner);
                        EditorUtility.SetDirty(meshFilter);
                        EditorUtility.SetDirty(meshRenderer);
                    }
                }
            }
        }

        if (hasRestoreSnapshot)
        {
            EditorGUILayout.HelpBox(
                "Restore will return child MeshFilter GameObjects and MeshRenderers to the exact active/enabled state captured before this combine. " +
                "Originally inactive helper/variant meshes will stay inactive.",
                MessageType.Info);
        }
        else if (meshFilter.sharedMesh != null)
        {
            EditorGUILayout.HelpBox(
                "This combined mesh has no exact restore snapshot (for example, it was created before this package update or the Editor session was restarted). " +
                "Use Unity Undo or reopen/reset the source hierarchy before the next comparison test.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Combined Mesh Asset", EditorStyles.boldLabel);

        serializedObject.Update();
        EditorGUILayout.PropertyField(
            _folderPath,
            new GUIContent(
                "Folder Path",
                "Path under Assets where the generated Mesh asset will be saved."));
        serializedObject.ApplyModifiedProperties();

        string folderPath = meshCombiner.FolderPath;
        bool isValidPath = IsValidPath(folderPath);
        mesh = meshFilter.sharedMesh;
        bool meshIsSaved = mesh != null && AssetDatabase.Contains(mesh);

        if (!isValidPath)
        {
            EditorGUILayout.HelpBox(
                "Folder path is invalid. Use a relative path under Assets, for example Generated/CombinedMeshes.",
                MessageType.Error);
        }

        using (new EditorGUI.DisabledScope(mesh == null || (!isValidPath && !meshIsSaved)))
        {
            string saveMeshButtonText = meshIsSaved
                ? "Show Saved Combined Mesh"
                : "Save Combined Mesh";

            if (GUILayout.Button(saveMeshButtonText))
            {
                meshCombiner.FolderPath = SaveCombinedMesh(mesh, folderPath);
                EditorUtility.SetDirty(meshCombiner);
            }
        }
    }

    private static RestoreSnapshot CaptureRestoreSnapshot(MeshCombiner meshCombiner, MeshFilter destinationMeshFilter)
    {
        RestoreSnapshot snapshot = new RestoreSnapshot();
        HashSet<int> capturedGameObjects = new HashSet<int>();
        HashSet<int> capturedRenderers = new HashSet<int>();

        MeshFilter[] sourceMeshFilters = meshCombiner.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter sourceMeshFilter in sourceMeshFilters)
        {
            if (sourceMeshFilter == null || sourceMeshFilter == destinationMeshFilter)
            {
                continue;
            }

            GameObject sourceGameObject = sourceMeshFilter.gameObject;
            int gameObjectId = sourceGameObject.GetInstanceID();
            if (capturedGameObjects.Add(gameObjectId))
            {
                snapshot.gameObjects.Add(new GameObjectState
                {
                    instanceId = gameObjectId,
                    activeSelf = sourceGameObject.activeSelf
                });
            }

            MeshRenderer sourceRenderer = sourceMeshFilter.GetComponent<MeshRenderer>();
            if (sourceRenderer == null)
            {
                continue;
            }

            int rendererId = sourceRenderer.GetInstanceID();
            if (capturedRenderers.Add(rendererId))
            {
                snapshot.renderers.Add(new RendererState
                {
                    instanceId = rendererId,
                    enabled = sourceRenderer.enabled
                });
            }
        }

        return snapshot;
    }

    private static void RestoreExactState(
        MeshCombiner meshCombiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer,
        RestoreSnapshot snapshot)
    {
        Mesh combinedMesh = destinationMeshFilter.sharedMesh;

        destinationMeshFilter.sharedMesh = null;
        destinationMeshRenderer.sharedMaterials = Array.Empty<Material>();

        MeshCollider meshCollider = meshCombiner.GetComponent<MeshCollider>();
        if (meshCollider != null && (combinedMesh == null || meshCollider.sharedMesh == combinedMesh))
        {
            meshCollider.sharedMesh = null;
        }

        int restoredGameObjects = 0;
        foreach (GameObjectState state in snapshot.gameObjects)
        {
            GameObject sourceGameObject = EditorUtility.InstanceIDToObject(state.instanceId) as GameObject;
            if (sourceGameObject == null || sourceGameObject.activeSelf == state.activeSelf)
            {
                continue;
            }

            sourceGameObject.SetActive(state.activeSelf);
            restoredGameObjects++;
        }

        int restoredRenderers = 0;
        foreach (RendererState state in snapshot.renderers)
        {
            MeshRenderer sourceRenderer = EditorUtility.InstanceIDToObject(state.instanceId) as MeshRenderer;
            if (sourceRenderer == null || sourceRenderer.enabled == state.enabled)
            {
                continue;
            }

            sourceRenderer.enabled = state.enabled;
            restoredRenderers++;
        }

        Debug.Log(
            "Mesh Combiner: restored exact pre-combine source state for \"" + meshCombiner.name + "\". " +
            "Restored " + restoredGameObjects + " GameObject active states and " + restoredRenderers + " MeshRenderer enabled states.",
            meshCombiner);
    }

    private static string GetRestoreStateKey(MeshCombiner meshCombiner)
    {
        return RestoreStateKeyPrefix + meshCombiner.GetInstanceID();
    }

    private static void SaveRestoreSnapshot(MeshCombiner meshCombiner, RestoreSnapshot snapshot)
    {
        SessionState.SetString(GetRestoreStateKey(meshCombiner), JsonUtility.ToJson(snapshot));
    }

    private static bool HasRestoreSnapshot(MeshCombiner meshCombiner)
    {
        return !string.IsNullOrEmpty(SessionState.GetString(GetRestoreStateKey(meshCombiner), string.Empty));
    }

    private static bool TryGetRestoreSnapshot(MeshCombiner meshCombiner, out RestoreSnapshot snapshot)
    {
        string json = SessionState.GetString(GetRestoreStateKey(meshCombiner), string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            snapshot = null;
            return false;
        }

        snapshot = JsonUtility.FromJson<RestoreSnapshot>(json);
        return snapshot != null;
    }

    private static void ClearRestoreSnapshot(MeshCombiner meshCombiner)
    {
        SessionState.EraseString(GetRestoreStateKey(meshCombiner));
    }

    private static bool IsValidPath(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return false;
        }

        string normalized = folderPath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("Assets/"))
        {
            return false;
        }

        const string pattern = "[:*?\"<>|]";
        return !Regex.IsMatch(normalized, pattern);
    }

    private static string SaveCombinedMesh(Mesh mesh, string folderPath)
    {
        if (mesh == null)
        {
            return folderPath;
        }

        if (AssetDatabase.Contains(mesh))
        {
            EditorGUIUtility.PingObject(mesh);
            return folderPath;
        }

        folderPath = folderPath.Replace('\\', '/').Trim('/');
        EnsureFolderExists(folderPath);

        string meshPath = AssetDatabase.GenerateUniqueAssetPath(
            "Assets/" + folderPath + "/" + mesh.name + ".asset");

        AssetDatabase.CreateAsset(mesh, meshPath);
        AssetDatabase.SaveAssets();

        EditorGUIUtility.PingObject(mesh);
        Debug.Log("Mesh Combiner: saved combined mesh to \"" + meshPath + "\".");

        string directory = System.IO.Path.GetDirectoryName(meshPath);
        if (string.IsNullOrEmpty(directory))
        {
            return folderPath;
        }

        directory = directory.Replace('\\', '/');
        return directory.StartsWith("Assets/")
            ? directory.Substring("Assets/".Length)
            : folderPath;
    }

    private static void EnsureFolderExists(string folderPath)
    {
        string currentPath = "Assets";

        foreach (string rawFolderName in folderPath.Split('/').Where(part => !string.IsNullOrWhiteSpace(part)))
        {
            string folderName = rawFolderName.Trim();
            string nextPath = currentPath + "/" + folderName;

            if (!AssetDatabase.IsValidFolder(nextPath))
            {
                AssetDatabase.CreateFolder(currentPath, folderName);
            }

            currentPath = nextPath;
        }
    }
}
