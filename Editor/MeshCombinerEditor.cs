using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MeshCombiner))]
public class MeshCombinerEditor : Editor
{
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
                "Destructive mode is enabled. The Restore / Undo Combine button cannot reconstruct child objects after they were destroyed. " +
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
        bool hasSourceMeshes = HasSourceMeshes(meshCombiner, meshFilter);
        bool hasRestorableState = HasRestorableState(meshCombiner, meshFilter, meshRenderer);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Combine Meshes", GUILayout.Height(28)))
            {
                Undo.RegisterFullObjectHierarchyUndo(meshCombiner.gameObject, "Combine Meshes");

                if (meshCombiner.TryCombineMeshes(true))
                {
                    EditorUtility.SetDirty(meshCombiner);
                    EditorUtility.SetDirty(meshFilter);
                    EditorUtility.SetDirty(meshRenderer);
                }
            }

            using (new EditorGUI.DisabledScope(!hasRestorableState || !hasSourceMeshes))
            {
                if (GUILayout.Button("Restore / Undo Combine", GUILayout.Height(28)))
                {
                    Mesh generatedMesh = meshFilter.sharedMesh;

                    Undo.RegisterFullObjectHierarchyUndo(meshCombiner.gameObject, "Restore Mesh Combine Sources");
                    meshCombiner.RestoreCombinedState(true);

                    // A transient generated Mesh is no longer referenced after restore. Remove it through
                    // the Undo system so rapid Combine -> Restore iteration does not accumulate orphan Mesh objects.
                    // Saved .asset meshes are intentionally kept in the Project.
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

        if (meshFilter.sharedMesh != null && !hasSourceMeshes)
        {
            EditorGUILayout.HelpBox(
                "No source child MeshFilters remain. Restore cannot reconstruct children destroyed by a destructive combine; use Unity Undo if it is still available.",
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

    private static bool HasSourceMeshes(MeshCombiner meshCombiner, MeshFilter destinationMeshFilter)
    {
        return meshCombiner.GetComponentsInChildren<MeshFilter>(true)
            .Any(sourceMeshFilter => sourceMeshFilter != null && sourceMeshFilter != destinationMeshFilter);
    }

    private static bool HasRestorableState(
        MeshCombiner meshCombiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer)
    {
        if (destinationMeshFilter.sharedMesh != null)
        {
            return true;
        }

        if (meshCombiner.GetComponentsInChildren<Transform>(true)
            .Any(child => child != null && child != meshCombiner.transform && !child.gameObject.activeSelf))
        {
            return true;
        }

        return meshCombiner.GetComponentsInChildren<MeshRenderer>(true)
            .Any(renderer => renderer != null && renderer != destinationMeshRenderer && !renderer.enabled);
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
