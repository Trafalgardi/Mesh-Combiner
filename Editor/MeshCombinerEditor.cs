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

    private SerializedProperty _createMultiMaterialMesh;
    private SerializedProperty _combineInactiveChildren;
    private SerializedProperty _meshFiltersToSkip;
    private SerializedProperty _removeExactOpposingFaces;
    private SerializedProperty _exactFacePositionTolerance;
    private SerializedProperty _deactivateCombinedChildren;
    private SerializedProperty _deactivateCombinedChildrenMeshRenderers;
    private SerializedProperty _updateOrCreateMeshCollider;
    private SerializedProperty _destroyCombinedChildren;
    private SerializedProperty _lightmapUvMode;
    private SerializedProperty _repackPaddingTexels;
    private SerializedProperty _repackPaddingReferenceResolution;
    private SerializedProperty _folderPath;

    private void OnEnable()
    {
        _createMultiMaterialMesh = serializedObject.FindProperty("createMultiMaterialMesh");
        _combineInactiveChildren = serializedObject.FindProperty("combineInactiveChildren");
        _meshFiltersToSkip = serializedObject.FindProperty("meshFiltersToSkip");
        _removeExactOpposingFaces = serializedObject.FindProperty("removeExactOpposingFaces");
        _exactFacePositionTolerance = serializedObject.FindProperty("exactFacePositionTolerance");
        _deactivateCombinedChildren = serializedObject.FindProperty("deactivateCombinedChildren");
        _deactivateCombinedChildrenMeshRenderers = serializedObject.FindProperty("deactivateCombinedChildrenMeshRenderers");
        _updateOrCreateMeshCollider = serializedObject.FindProperty("updateOrCreateMeshCollider");
        _destroyCombinedChildren = serializedObject.FindProperty("destroyCombinedChildren");
        _lightmapUvMode = serializedObject.FindProperty("lightmapUvMode");
        _repackPaddingTexels = serializedObject.FindProperty("repackPaddingTexels");
        _repackPaddingReferenceResolution = serializedObject.FindProperty("repackPaddingReferenceResolution");
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
            EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(meshCombiner), typeof(MeshCombiner), false);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Combine", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            _createMultiMaterialMesh,
            new GUIContent(
                "Create Multi-Material Mesh",
                "Preserves different materials as submeshes. Disable only when all source submeshes use the same material."));
        EditorGUILayout.PropertyField(_combineInactiveChildren, new GUIContent("Combine Inactive Children"));
        EditorGUILayout.PropertyField(_meshFiltersToSkip, new GUIContent("Mesh Filters To Skip"), true);
        EditorGUILayout.HelpBox(
            "Nested exact duplicates are filtered automatically: if a deeper child uses the same shared Mesh at the same world transform as an ancestor below this root, the deeper child is skipped. This is intended for generic helper/proxy copies and does not depend on object names.",
            MessageType.Info);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Geometry Cleanup", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            _removeExactOpposingFaces,
            new GUIContent(
                "Remove Exact Opposing Faces",
                "After combining, removes pairs of coincident triangles that use the same three positions but face in opposite directions. Useful for exact back-to-back internal faces between modular pieces."));

        if (_removeExactOpposingFaces.boolValue)
        {
            EditorGUILayout.PropertyField(
                _exactFacePositionTolerance,
                new GUIContent(
                    "Position Tolerance",
                    "Destination-local position tolerance used when matching the three triangle vertices. Start small; 0.0001 means 0.1 mm when one Unity unit is one meter."));
            if (_exactFacePositionTolerance.floatValue < 0.000001f)
                _exactFacePositionTolerance.floatValue = 0.000001f;

            EditorGUILayout.HelpBox(
                "Opt-in cleanup: any exact coincident triangle pair with opposite winding can be removed, including intentionally double-sided geometry. It removes triangle indices only; the vertex buffer is not compacted, so normals/tangents/UVs and other vertex attributes are left untouched.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Exact opposing-face cleanup is disabled by default because intentionally double-sided geometry can also contain coincident opposite-wound triangles.",
                MessageType.Info);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_deactivateCombinedChildren, new GUIContent("Deactivate Combined Children"));
        EditorGUILayout.PropertyField(_deactivateCombinedChildrenMeshRenderers, new GUIContent("Disable Child MeshRenderers"));
        EditorGUILayout.PropertyField(
            _updateOrCreateMeshCollider,
            new GUIContent(
                "Update/Create MeshCollider",
                "Assigns the final combined render mesh to a MeshCollider on this GameObject. This does not merge custom primitive colliders."));
        EditorGUILayout.PropertyField(
            _destroyCombinedChildren,
            new GUIContent(
                "Destroy Combined Children",
                "Destructive mode. Prefer deactivating the source hierarchy so recovery remains possible."));

        if (_destroyCombinedChildren.boolValue)
        {
            _deactivateCombinedChildren.boolValue = false;
            _deactivateCombinedChildrenMeshRenderers.boolValue = false;
            EditorGUILayout.HelpBox(
                "Destructive mode is enabled. Exact Restore and Force Recover cannot reconstruct destroyed child objects. Use Unity Undo immediately if needed.",
                MessageType.Warning);
        }

        if (_updateOrCreateMeshCollider.boolValue &&
            !_deactivateCombinedChildren.boolValue &&
            _deactivateCombinedChildrenMeshRenderers.boolValue)
        {
            EditorGUILayout.HelpBox(
                "Child GameObjects stay active, so their existing Colliders also stay active. Adding a combined MeshCollider can create duplicate collision.",
                MessageType.Warning);
        }

        EditorGUILayout.Space();
        DrawLightmapUvSettings();
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        DrawCombineAndRecovery(meshCombiner, meshFilter, meshRenderer);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Combined Mesh Asset", EditorStyles.boldLabel);
        serializedObject.Update();
        EditorGUILayout.PropertyField(
            _folderPath,
            new GUIContent("Folder Path", "Path under Assets where the generated Mesh asset will be saved."));
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
            string saveMeshButtonText = meshIsSaved ? "Show Saved Combined Mesh" : "Save Combined Mesh";
            if (GUILayout.Button(saveMeshButtonText))
            {
                meshCombiner.FolderPath = SaveCombinedMesh(mesh, folderPath);
                EditorUtility.SetDirty(meshCombiner);
            }
        }
    }

    private static void DrawCombineAndRecovery(
        MeshCombiner meshCombiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer)
    {
        bool hasRestoreSnapshot = HasRestoreSnapshot(meshCombiner);
        bool destructiveMode = meshCombiner.DestroyCombinedChildren;

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(hasRestoreSnapshot))
            {
                if (GUILayout.Button("Combine Meshes", GUILayout.Height(28)))
                {
                    RestoreSnapshot restoreSnapshot = CaptureRestoreSnapshot(meshCombiner, destinationMeshFilter);

                    // Persist before mutating the hierarchy. If the Editor crashes during/after Combine,
                    // this snapshot can still be recovered on the next Editor launch.
                    if (!destructiveMode)
                    {
                        SaveRestoreSnapshot(meshCombiner, restoreSnapshot);
                    }
                    else
                    {
                        ClearRestoreSnapshot(meshCombiner);
                    }

                    Undo.RegisterFullObjectHierarchyUndo(meshCombiner.gameObject, "Combine Meshes");

                    if (meshCombiner.TryCombineMeshes(true))
                    {
                        EditorUtility.SetDirty(meshCombiner);
                        EditorUtility.SetDirty(destinationMeshFilter);
                        EditorUtility.SetDirty(destinationMeshRenderer);
                    }
                    else
                    {
                        ClearRestoreSnapshot(meshCombiner);
                    }
                }
            }

            using (new EditorGUI.DisabledScope(!hasRestoreSnapshot || destructiveMode))
            {
                if (GUILayout.Button("Restore Exact State", GUILayout.Height(28)))
                {
                    if (TryGetRestoreSnapshot(meshCombiner, out RestoreSnapshot restoreSnapshot))
                    {
                        Mesh generatedMesh = destinationMeshFilter.sharedMesh;
                        Undo.RegisterFullObjectHierarchyUndo(meshCombiner.gameObject, "Restore Mesh Combine Sources");
                        RestoreExactState(meshCombiner, destinationMeshFilter, destinationMeshRenderer, restoreSnapshot);
                        ClearRestoreSnapshot(meshCombiner);
                        DestroyTransientCombinedMesh(generatedMesh);

                        EditorUtility.SetDirty(meshCombiner);
                        EditorUtility.SetDirty(destinationMeshFilter);
                        EditorUtility.SetDirty(destinationMeshRenderer);
                    }
                }
            }
        }

        using (new EditorGUI.DisabledScope(destructiveMode))
        {
            if (GUILayout.Button("Force Recover Sources (No Snapshot)", GUILayout.Height(24)))
            {
                Mesh generatedMesh = destinationMeshFilter.sharedMesh;
                Undo.RegisterFullObjectHierarchyUndo(meshCombiner.gameObject, "Force Recover Mesh Combine Sources");
                ForceRecoverWithoutSnapshot(meshCombiner, destinationMeshFilter, destinationMeshRenderer);
                ClearRestoreSnapshot(meshCombiner);
                DestroyTransientCombinedMesh(generatedMesh);

                EditorUtility.SetDirty(meshCombiner);
                EditorUtility.SetDirty(destinationMeshFilter);
                EditorUtility.SetDirty(destinationMeshRenderer);
            }
        }

        if (hasRestoreSnapshot)
        {
            EditorGUILayout.HelpBox(
                "A crash-persistent exact restore snapshot exists. It is stored with stable GlobalObjectId references and survives domain reloads and Editor restarts. Restore Exact State returns source GameObjects and MeshRenderers to the active/enabled state captured before Combine.",
                MessageType.Info);
        }
        else if (destinationMeshFilter.sharedMesh != null)
        {
            EditorGUILayout.HelpBox(
                "No exact snapshot is available. Force Recover Sources clears the combined output and enables all descendant MeshFilter GameObjects and MeshRenderers. This is intentionally aggressive and can re-enable helpers/variants that were disabled before Combine.",
                MessageType.Warning);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Force Recover Sources is an emergency fallback and does not require an exact snapshot. It is useful after an Editor crash or stale scene state, but it intentionally enables all descendant mesh sources.",
                MessageType.None);
        }
    }

    private void DrawLightmapUvSettings()
    {
        EditorGUILayout.LabelField("Lightmap UV", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(
            _lightmapUvMode,
            new GUIContent("Lightmap UV Mode", "Controls what happens to Mesh.uv2, which baked lightmaps use."));

        LightmapUvMode mode = (LightmapUvMode)_lightmapUvMode.enumValueIndex;
        switch (mode)
        {
            case LightmapUvMode.None:
                EditorGUILayout.HelpBox(
                    "No lightmap UV processing. Use this for non-lightmapped output or when UV2 will be handled elsewhere.",
                    MessageType.Info);
                break;

            case LightmapUvMode.PreserveSourceUv2:
                EditorGUILayout.HelpBox(
                    "Keeps source UV2 exactly as-is. Repeated modular meshes usually overlap after combine, so this mode is mainly diagnostic unless the sources were already packed into one shared UV space.",
                    MessageType.Warning);
                break;

            case LightmapUvMode.PreserveAndRepackSourceUv2:
                EditorGUILayout.IntSlider(
                    _repackPaddingTexels,
                    1,
                    16,
                    new GUIContent(
                        "Chart Padding (texels)",
                        "Padding reserved around every existing UV2 chart before packing. Increase this if UV Overlap shows red chart neighborhoods."));

                int[] resolutions = { 256, 512, 1024, 2048, 4096 };
                GUIContent[] resolutionOptions =
                {
                    new GUIContent("256"),
                    new GUIContent("512"),
                    new GUIContent("1024"),
                    new GUIContent("2048"),
                    new GUIContent("4096")
                };
                int currentResolution = _repackPaddingReferenceResolution.intValue;
                if (!resolutions.Contains(currentResolution))
                {
                    currentResolution = 512;
                }

                _repackPaddingReferenceResolution.intValue = EditorGUILayout.IntPopup(
                    new GUIContent(
                        "Padding Reference Size",
                        "Chart Padding is converted to normalized UV space using this resolution."),
                    currentResolution,
                    resolutionOptions,
                    resolutions);

                EditorGUILayout.HelpBox(
                    "Recommended experimental mode for modular static geometry with valid source UV2. Charts are packed by world-space surface density. Existing chart topology is preserved and no new vertices are created. Independent charts can have different checker phase/offset in Baked Lightmap visualization; equal checker size (texel density) is the important part.",
                    MessageType.Info);
                break;

            case LightmapUvMode.RegenerateUv2:
                EditorGUILayout.HelpBox(
                    "Rebuilds UV2 for the entire combined mesh with Unity's secondary UV unwrapper. This can split vertices and change baked seams. UInt16 is kept when the index-count upper bound proves the result cannot exceed 65,535 vertices.",
                    MessageType.Warning);
                break;
        }
    }

    private static RestoreSnapshot CaptureRestoreSnapshot(
        MeshCombiner meshCombiner,
        MeshFilter destinationMeshFilter)
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
                    globalObjectId = GetStableObjectId(sourceGameObject),
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
                    globalObjectId = GetStableObjectId(sourceRenderer),
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
        ClearCombinedOutput(meshCombiner, destinationMeshFilter, destinationMeshRenderer);

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

        Debug.Log(
            "Mesh Combiner: restored exact pre-combine state for \"" + meshCombiner.name + "\". Changed " +
            restoredGameObjects + " GameObject active states and " + restoredRenderers +
            " MeshRenderer enabled states. Missing references: " +
            (missingGameObjects + missingRenderers) + ".",
            meshCombiner);
    }

    private static void ForceRecoverWithoutSnapshot(
        MeshCombiner meshCombiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer)
    {
        ClearCombinedOutput(meshCombiner, destinationMeshFilter, destinationMeshRenderer);

        int activatedGameObjects = 0;
        int enabledRenderers = 0;
        MeshFilter[] sourceMeshFilters = meshCombiner.GetComponentsInChildren<MeshFilter>(true);

        foreach (MeshFilter sourceMeshFilter in sourceMeshFilters)
        {
            if (sourceMeshFilter == null || sourceMeshFilter == destinationMeshFilter)
            {
                continue;
            }

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

        Debug.LogWarning(
            "Mesh Combiner: FORCE RECOVERY completed for \"" + meshCombiner.name + "\". Activated " +
            activatedGameObjects + " child GameObjects and enabled " + enabledRenderers +
            " MeshRenderers. Because no exact snapshot was used, previously disabled helpers/variants may also be enabled.",
            meshCombiner);
    }

    private static void ClearCombinedOutput(
        MeshCombiner meshCombiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer)
    {
        Mesh combinedMesh = destinationMeshFilter.sharedMesh;
        destinationMeshFilter.sharedMesh = null;
        destinationMeshRenderer.sharedMaterials = Array.Empty<Material>();

        MeshCollider meshCollider = meshCombiner.GetComponent<MeshCollider>();
        if (meshCollider != null &&
            (combinedMesh == null || meshCollider.sharedMesh == combinedMesh))
        {
            meshCollider.sharedMesh = null;
        }
    }

    private static void DestroyTransientCombinedMesh(Mesh generatedMesh)
    {
        if (generatedMesh != null && !AssetDatabase.Contains(generatedMesh))
        {
            Undo.DestroyObjectImmediate(generatedMesh);
        }
    }

    private static string GetStableObjectId(UnityEngine.Object target)
    {
        if (target == null)
        {
            return string.Empty;
        }

        GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(target);
        return id.Equals(default(GlobalObjectId)) ? string.Empty : id.ToString();
    }

    private static T ResolveObject<T>(int instanceId, string globalObjectId) where T : UnityEngine.Object
    {
        T currentSessionObject = EditorUtility.InstanceIDToObject(instanceId) as T;
        if (currentSessionObject != null)
        {
            return currentSessionObject;
        }

        if (string.IsNullOrEmpty(globalObjectId) ||
            !GlobalObjectId.TryParse(globalObjectId, out GlobalObjectId parsedId))
        {
            return null;
        }

        return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsedId) as T;
    }

    private static string GetRestoreStateKey(MeshCombiner meshCombiner)
    {
        string projectKey = Hash128.Compute(Application.dataPath).ToString();
        string objectKey = GetStableObjectId(meshCombiner);
        if (string.IsNullOrEmpty(objectKey))
        {
            objectKey = "instance-" + meshCombiner.GetInstanceID();
        }

        return RestoreStateKeyPrefix + projectKey + "." + objectKey;
    }

    private static void SaveRestoreSnapshot(MeshCombiner meshCombiner, RestoreSnapshot snapshot)
    {
        EditorPrefs.SetString(GetRestoreStateKey(meshCombiner), JsonUtility.ToJson(snapshot));
    }

    private static bool HasRestoreSnapshot(MeshCombiner meshCombiner)
    {
        return !string.IsNullOrEmpty(EditorPrefs.GetString(GetRestoreStateKey(meshCombiner), string.Empty));
    }

    private static bool TryGetRestoreSnapshot(MeshCombiner meshCombiner, out RestoreSnapshot snapshot)
    {
        string json = EditorPrefs.GetString(GetRestoreStateKey(meshCombiner), string.Empty);
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
        EditorPrefs.DeleteKey(GetRestoreStateKey(meshCombiner));
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
