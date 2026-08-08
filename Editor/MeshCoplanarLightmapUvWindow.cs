using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class MeshCoplanarLightmapUvWindow : EditorWindow
{
    private GameObject _target;
    private float _positionTolerance = 0.0001f;
    private float _coplanarAngleDegrees = 1f;
    private int _paddingTexels = 2;
    private int _paddingReferenceResolution = 512;
    private string _lastResult;
    private MessageType _lastResultType = MessageType.None;

    [MenuItem("Tools/Mesh Combiner/Coplanar Stitched UV2...")]
    private static void Open()
    {
        MeshCoplanarLightmapUvWindow window = GetWindow<MeshCoplanarLightmapUvWindow>(true, "Coplanar Stitched UV2");
        window.minSize = new Vector2(430f, 360f);
        window._target = Selection.activeGameObject;
        window.Show();
    }

    private void OnSelectionChange()
    {
        _target = Selection.activeGameObject;
        Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Coplanar Stitched Lightmap UV2", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Experimental seam diagnostic/generator. It groups triangles that share the same geometric edge and are coplanar, then planar-projects each connected surface into one continuous UV2 chart. Geometry, UV0, normals, tangents and materials are not changed.",
            MessageType.Info);

        _target = (GameObject)EditorGUILayout.ObjectField("Target", _target, typeof(GameObject), true);
        _positionTolerance = Mathf.Max(0.000001f, EditorGUILayout.FloatField(
            new GUIContent("Edge Position Tolerance", "World-space tolerance used to decide whether two triangle edge endpoints are the same."),
            _positionTolerance));
        _coplanarAngleDegrees = Mathf.Clamp(EditorGUILayout.Slider(
            new GUIContent("Coplanar Angle", "Maximum normal-angle difference for triangles connected through the same geometric edge."),
            _coplanarAngleDegrees, 0.01f, 10f), 0.01f, 10f);
        _paddingTexels = EditorGUILayout.IntSlider(
            new GUIContent("Chart Padding (texels)"), _paddingTexels, 0, 16);

        int[] resolutions = { 256, 512, 1024, 2048, 4096 };
        string[] resolutionLabels = { "256", "512", "1024", "2048", "4096" };
        int resolutionIndex = Array.IndexOf(resolutions, _paddingReferenceResolution);
        if (resolutionIndex < 0) resolutionIndex = 1;
        resolutionIndex = EditorGUILayout.Popup("Padding Reference Size", resolutionIndex, resolutionLabels);
        _paddingReferenceResolution = resolutions[Mathf.Clamp(resolutionIndex, 0, resolutions.Length - 1)];

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Safety: if one vertex index is shared by multiple hard-angle generated charts, generation aborts instead of silently corrupting UV2. The current test mode requires triangle topology.",
            MessageType.Warning);

        bool canGenerate = _target != null &&
                           _target.TryGetComponent(out MeshFilter meshFilter) &&
                           meshFilter.sharedMesh != null;

        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            if (GUILayout.Button("Generate Coplanar-Stitched UV2", GUILayout.Height(32f)))
                Generate();
        }

        if (!canGenerate)
        {
            EditorGUILayout.HelpBox("Select the combined GameObject that owns the output MeshFilter.", MessageType.Info);
        }

        if (!string.IsNullOrEmpty(_lastResult))
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_lastResult, _lastResultType);
        }
    }

    private void Generate()
    {
        if (_target == null || !_target.TryGetComponent(out MeshFilter meshFilter) || meshFilter.sharedMesh == null)
        {
            _lastResult = "Target has no MeshFilter/shared mesh.";
            _lastResultType = MessageType.Error;
            return;
        }

        Mesh originalMesh = meshFilter.sharedMesh;
        Mesh workingMesh = Instantiate(originalMesh);
        workingMesh.name = originalMesh.name + "_CoplanarStitchedUV2";

        if (!MeshCoplanarLightmapUv.TryGenerate(
                workingMesh,
                _target.transform.localToWorldMatrix,
                _positionTolerance,
                _coplanarAngleDegrees,
                _paddingTexels,
                _paddingReferenceResolution,
                out MeshCoplanarLightmapUv.Result result,
                out string error))
        {
            DestroyImmediate(workingMesh);
            _lastResult = error;
            _lastResultType = MessageType.Error;
            Debug.LogError("Mesh Combiner: Coplanar Stitched UV2 failed for \"" + _target.name + "\": " + error, _target);
            return;
        }

        Undo.RecordObject(meshFilter, "Generate Coplanar Stitched UV2");
        meshFilter.sharedMesh = workingMesh;
        EditorUtility.SetDirty(meshFilter);

        if (_target.TryGetComponent(out MeshCollider meshCollider) && meshCollider.sharedMesh == originalMesh)
        {
            Undo.RecordObject(meshCollider, "Update Coplanar Stitched MeshCollider");
            meshCollider.sharedMesh = workingMesh;
            EditorUtility.SetDirty(meshCollider);
        }

        if (_target.TryGetComponent(out MeshRenderer meshRenderer))
        {
            Undo.RecordObject(meshRenderer, "Prepare Coplanar Stitched Lightmap Renderer");
            meshRenderer.receiveGI = ReceiveGI.Lightmaps;
            meshRenderer.stitchLightmapSeams = true;
            EditorUtility.SetDirty(meshRenderer);
        }

        StaticEditorFlags staticFlags = GameObjectUtility.GetStaticEditorFlags(_target) | StaticEditorFlags.ContributeGI;
        GameObjectUtility.SetStaticEditorFlags(_target, staticFlags);
        EditorUtility.SetDirty(_target);

        if (!AssetDatabase.Contains(originalMesh))
            Undo.DestroyObjectImmediate(originalMesh);

        _lastResult =
            "Generated " + result.chartCount + " coplanar UV2 charts from " + result.triangleCount +
            " triangles; stitched " + result.stitchedEdgeConnections + " shared-edge connections; assigned " +
            result.assignedVertexCount + " vertices; packing scale " + result.packingScale.ToString("0.###") +
            ". Geometry and render attributes were not modified.";
        _lastResultType = MessageType.Info;

        Debug.Log("<color=#00cc00><b>Mesh Combiner: Coplanar Stitched UV2 generated for \"" + _target.name +
                  "\": " + result.chartCount + " charts, " + result.stitchedEdgeConnections +
                  " stitched edge connections, " + result.assignedVertexCount + " assigned vertices, packing scale " +
                  result.packingScale.ToString("0.###") + ", tolerance " + result.positionTolerance.ToString("0.######") +
                  ", coplanar angle " + result.coplanarAngleDegrees.ToString("0.###") + " deg.</b></color>", _target);

        SceneView.RepaintAll();
    }
}
