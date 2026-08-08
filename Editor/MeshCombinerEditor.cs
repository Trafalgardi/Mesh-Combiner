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
                "Destructive mode is enabled. Undo is supported in the Editor, but keeping source objects deactivated is safer for production workflows.",
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
        if (GUILayout.Button("Combine Meshes", GUILayout.Height(28)))
        {
            Undo.RegisterFullObjectHierarchyUndo(meshCombiner.gameObject, "Combine Meshes");

            if (meshCombiner.TryCombineMeshes(true))
            {
                EditorUtility.SetDirty(meshCombiner);
                EditorUtility.SetDirty(meshFilter);
                EditorUtility.SetDirty(meshCombiner.GetComponent<MeshRenderer>());
            }
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
