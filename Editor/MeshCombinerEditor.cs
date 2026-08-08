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

    private MeshCombinerAnalysisReport _analysisReport;
    private bool _analysisDirty = true;

    private bool _sourcesExpanded = true;
    private bool _combineExpanded = true;
    private bool _lightmapExpanded = true;
    private bool _geometryExpanded;
    private bool _outputExpanded = true;
    private bool _actionsExpanded = true;
    private bool _assetExpanded;
    private bool _includedListExpanded;
    private bool _ignoredListExpanded = true;
    private bool _issuesExpanded = true;
    private bool _destructiveExpanded;

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
        _analysisDirty = true;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        MeshCombiner meshCombiner = (MeshCombiner)target;
        MeshFilter destinationMeshFilter = meshCombiner.GetComponent<MeshFilter>();
        MeshRenderer destinationMeshRenderer = meshCombiner.GetComponent<MeshRenderer>();

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(meshCombiner), typeof(MeshCombiner), false);
        }

        if (_analysisReport == null)
            RunAnalysis(meshCombiner, false);

        EditorGUI.BeginChangeCheck();
        DrawSourcesAndAnalyzer(meshCombiner);
        DrawCombineSettings();
        DrawLightmapUvSettings();
        DrawGeometryCleanupSettings();
        DrawOutputSettings();
        bool settingsChanged = EditorGUI.EndChangeCheck();

        if (serializedObject.ApplyModifiedProperties() || settingsChanged)
            _analysisDirty = true;

        DrawActions(meshCombiner, destinationMeshFilter, destinationMeshRenderer);
        DrawAssetSettings(meshCombiner, destinationMeshFilter);
    }

    private void DrawSourcesAndAnalyzer(MeshCombiner meshCombiner)
    {
        BeginSection(ref _sourcesExpanded, "Sources & Analyzer", "Controls which child MeshFilters are eligible and shows the exact preflight result before combining.");
        if (_sourcesExpanded)
        {
            EditorGUILayout.PropertyField(
                _combineInactiveChildren,
                new GUIContent(
                    "Combine Inactive Children",
                    "When enabled, inactive child GameObjects are eligible for combining. When disabled, MeshFilters outside Unity's active GetComponentsInChildren traversal are ignored and listed by the analyzer."));

            EditorGUILayout.PropertyField(
                _meshFiltersToSkip,
                new GUIContent(
                    "User Ignore List",
                    "Explicit per-component ignore list. MeshFilters placed here are never combined and appear in the Analyzer under User Ignore."),
                true);

            EditorGUILayout.Space(3f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(
                        "Analyze / Validate",
                        "Runs the same source-selection rules used by the combiner and builds a read-only preflight report without changing scene objects."),
                    GUILayout.Height(26)))
                {
                    RunAnalysis(meshCombiner, true);
                }

                using (new EditorGUI.DisabledScope(_analysisReport == null))
                {
                    if (GUILayout.Button(new GUIContent(
                            "Ping First Error",
                            "Selects the context object of the first analyzer error, when one is available."),
                        GUILayout.Width(110), GUILayout.Height(26)))
                    {
                        PingFirstAnalysisError();
                    }
                }
            }

            if (_analysisDirty)
            {
                EditorGUILayout.HelpBox(
                    "Analyzer result is stale because settings or hierarchy state changed. Run Analyze / Validate again. Combine Meshes always reruns validation automatically before modifying the hierarchy.",
                    MessageType.Warning);
            }

            DrawAnalysisReport();
        }
        EndSection();
    }

    private void DrawCombineSettings()
    {
        BeginSection(ref _combineExpanded, "Combine", "Core mesh/material output behavior.");
        if (_combineExpanded)
        {
            EditorGUILayout.PropertyField(
                _createMultiMaterialMesh,
                new GUIContent(
                    "Create Multi-Material Mesh",
                    "Enable to preserve different source Material references as separate output submeshes. Disable only when every included source submesh uses the same Material reference."));

            EditorGUILayout.HelpBox(
                _createMultiMaterialMesh.boolValue
                    ? "Different shared Material references are grouped into separate output submeshes. This reduces renderer count, but each material/submesh can still require its own draw submission."
                    : "Single Material mode validates that all included source submeshes use the same Material reference before Combine.",
                MessageType.Info);
        }
        EndSection();
    }

    private void DrawLightmapUvSettings()
    {
        BeginSection(ref _lightmapExpanded, "Lightmap UV2", "Controls what happens to Mesh.uv2 on the generated mesh.");
        if (_lightmapExpanded)
        {
            LightmapUvMode currentMode = (LightmapUvMode)_lightmapUvMode.enumValueIndex;
            LightmapUvMode newMode = (LightmapUvMode)EditorGUILayout.EnumPopup(
                new GUIContent(
                    "Lightmap UV Mode",
                    "Select the UV2 strategy used by baked lightmaps. Settings that do not apply to the selected mode are hidden."),
                currentMode);
            if (newMode != currentMode)
                _lightmapUvMode.enumValueIndex = (int)newMode;

            LightmapUvMode mode = (LightmapUvMode)_lightmapUvMode.enumValueIndex;
            bool showPackingSettings =
                mode == LightmapUvMode.PreserveAndRepackSourceUv2 ||
                mode == LightmapUvMode.CoplanarStitchedUv2;

            if (showPackingSettings)
            {
                EditorGUILayout.IntSlider(
                    _repackPaddingTexels,
                    1,
                    16,
                    new GUIContent(
                        "Chart Padding (texels)",
                        "Padding reserved around UV2 charts during packing. Higher values reduce cross-chart filtering risk but consume more atlas space."));

                DrawReferenceResolutionPopup();
            }

            switch (mode)
            {
                case LightmapUvMode.None:
                    EditorGUILayout.HelpBox(
                        "No UV2 processing. Use this when the output is not baked or another tool will build lightmap UVs later.",
                        MessageType.Info);
                    break;

                case LightmapUvMode.PreserveSourceUv2:
                    EditorGUILayout.HelpBox(
                        "Copies source UV2 as-is. Reused modular meshes commonly overlap in the same 0..1 UV space, so this is mainly useful when the sources were already authored into one shared non-overlapping atlas.",
                        MessageType.Warning);
                    break;

                case LightmapUvMode.PreserveAndRepackSourceUv2:
                    EditorGUILayout.HelpBox(
                        "Keeps existing source UV2 chart topology and repacks the charts by world-space surface density. No new UV seams are created, but disconnected modular charts stay disconnected and can still bake with visible joins.",
                        MessageType.Info);
                    break;

                case LightmapUvMode.RegenerateUv2:
                    EditorGUILayout.HelpBox(
                        "Rebuilds UV2 for the entire combined mesh using Unity's secondary UV unwrapper. Source UV2 is not required. Unity may split vertices while creating chart seams.",
                        MessageType.Warning);
                    break;

                case LightmapUvMode.CoplanarStitchedUv2:
                    EditorGUILayout.HelpBox(
                        "Recommended for modular static architecture. The combiner joins exact and partial/T-junction coplanar boundaries into continuous UV2 charts using validated defaults (0.0001 position tolerance, 1 degree coplanar angle). Geometry, UV0, normals, tangents and materials are not changed.",
                        MessageType.Info);
                    EditorGUILayout.LabelField(
                        new GUIContent(
                            "Advanced tuning",
                            "Use the separate advanced tool only when a specific asset needs non-default edge tolerance or coplanar angle values."),
                        "Tools > Mesh Combiner > Coplanar Stitched UV2...");
                    break;
            }
        }
        EndSection();
    }

    private void DrawReferenceResolutionPopup()
    {
        int[] resolutions = { 256, 512, 1024, 2048, 4096 };
        string[] labels = { "256", "512", "1024", "2048", "4096" };
        int current = _repackPaddingReferenceResolution.intValue;
        if (!resolutions.Contains(current)) current = 512;

        int selected = Array.IndexOf(resolutions, current);
        selected = EditorGUILayout.Popup(
            new GUIContent(
                "Padding Reference Size",
                "Reference lightmap resolution used to convert Chart Padding from texels into normalized UV space."),
            selected,
            labels);
        _repackPaddingReferenceResolution.intValue = resolutions[Mathf.Clamp(selected, 0, resolutions.Length - 1)];
    }

    private void DrawGeometryCleanupSettings()
    {
        BeginSection(ref _geometryExpanded, "Geometry Cleanup", "Optional conservative removal of exact back-to-back internal faces.");
        if (_geometryExpanded)
        {
            EditorGUILayout.PropertyField(
                _removeExactOpposingFaces,
                new GUIContent(
                    "Remove Exact Opposing Faces",
                    "Removes pairs of coincident triangles that use the same three positions but opposite winding. This is conservative exact matching, not a Boolean union."));

            if (_removeExactOpposingFaces.boolValue)
            {
                EditorGUILayout.PropertyField(
                    _exactFacePositionTolerance,
                    new GUIContent(
                        "Position Tolerance",
                        "Destination-local position tolerance used when matching exact opposing triangle vertices. 0.0001 equals 0.1 mm when one Unity unit is one meter."));
                if (_exactFacePositionTolerance.floatValue < 0.000001f)
                    _exactFacePositionTolerance.floatValue = 0.000001f;

                EditorGUILayout.HelpBox(
                    "Only exact coincident opposite-wound triangle pairs are removed. Partially overlapping polygons, different triangulations and general Boolean cleanup are intentionally not handled here.",
                    MessageType.Warning);
            }
        }
        EndSection();
    }

    private void DrawOutputSettings()
    {
        BeginSection(ref _outputExpanded, "Output", "Controls what happens to source objects after a successful combine and whether a destination MeshCollider is generated.");
        if (_outputExpanded)
        {
            EditorGUILayout.PropertyField(
                _deactivateCombinedChildren,
                new GUIContent(
                    "Deactivate Combined Children",
                    "After a successful combine, disables the included source GameObjects. This is the safest normal workflow because exact recovery can restore their previous active state."));

            if (!_deactivateCombinedChildren.boolValue)
            {
                EditorGUILayout.PropertyField(
                    _deactivateCombinedChildrenMeshRenderers,
                    new GUIContent(
                        "Disable Child MeshRenderers",
                        "Keeps source GameObjects active but disables their MeshRenderer components. Existing child Colliders and scripts remain active."));
            }

            EditorGUILayout.PropertyField(
                _updateOrCreateMeshCollider,
                new GUIContent(
                    "Update/Create MeshCollider",
                    "Creates or updates a MeshCollider on the combiner root and assigns the final combined render mesh. Primitive/custom child Colliders are not merged or removed."));

            if (_updateOrCreateMeshCollider.boolValue &&
                !_deactivateCombinedChildren.boolValue &&
                _deactivateCombinedChildrenMeshRenderers.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Child GameObjects remain active, so their existing Colliders also remain active. A generated combined MeshCollider can therefore duplicate collision unless you manage child Colliders separately.",
                    MessageType.Warning);
            }

            _destructiveExpanded = EditorGUILayout.Foldout(
                _destructiveExpanded,
                new GUIContent(
                    "Destructive source handling",
                    "Advanced option for permanently deleting included source GameObjects after combining."),
                true);

            if (_destructiveExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(
                    _destroyCombinedChildren,
                    new GUIContent(
                        "Destroy Combined Children",
                        "Permanently destroys included source GameObjects after combine. Exact Restore and Force Recover cannot reconstruct destroyed hierarchy objects."));
                EditorGUI.indentLevel--;

                if (_destroyCombinedChildren.boolValue)
                {
                    _deactivateCombinedChildren.boolValue = false;
                    _deactivateCombinedChildrenMeshRenderers.boolValue = false;
                    EditorGUILayout.HelpBox(
                        "Destructive mode is enabled. Recovery snapshots cannot recreate deleted source objects. Use this only when the generated output is already verified and saved.",
                        MessageType.Error);
                }
            }
        }
        EndSection();
    }

    private void DrawActions(
        MeshCombiner meshCombiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer)
    {
        BeginSection(ref _actionsExpanded, "Actions & Recovery", "Combine, restore and emergency recovery operations.");
        if (_actionsExpanded)
        {
            bool hasRestoreSnapshot = HasRestoreSnapshot(meshCombiner);
            bool destructiveMode = meshCombiner.DestroyCombinedChildren;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(hasRestoreSnapshot))
                {
                    if (GUILayout.Button(new GUIContent(
                            "Combine Meshes",
                            "Runs Analyze / Validate first. If there are no blocking analyzer errors, combines the eligible source set and stores a crash-persistent restore snapshot unless destructive mode is enabled."),
                        GUILayout.Height(30)))
                    {
                        TryAnalyzeAndCombine(meshCombiner, destinationMeshFilter, destinationMeshRenderer);
                    }
                }

                using (new EditorGUI.DisabledScope(!hasRestoreSnapshot || destructiveMode))
                {
                    if (GUILayout.Button(new GUIContent(
                            "Restore Exact State",
                            "Restores the exact pre-combine GameObject active states and MeshRenderer enabled states recorded in the crash-persistent snapshot."),
                        GUILayout.Height(30)))
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
                            _analysisDirty = true;
                        }
                    }
                }
            }

            using (new EditorGUI.DisabledScope(destructiveMode))
            {
                if (GUILayout.Button(new GUIContent(
                        "Force Recover Sources (No Snapshot)",
                        "Emergency fallback. Clears combined output and aggressively re-enables descendant source GameObjects and MeshRenderers even when no valid restore snapshot exists."),
                    GUILayout.Height(24)))
                {
                    Mesh generatedMesh = destinationMeshFilter.sharedMesh;
                    Undo.RegisterFullObjectHierarchyUndo(meshCombiner.gameObject, "Force Recover Mesh Combine Sources");
                    ForceRecoverWithoutSnapshot(meshCombiner, destinationMeshFilter, destinationMeshRenderer);
                    ClearRestoreSnapshot(meshCombiner);
                    DestroyTransientCombinedMesh(generatedMesh);

                    EditorUtility.SetDirty(meshCombiner);
                    EditorUtility.SetDirty(destinationMeshFilter);
                    EditorUtility.SetDirty(destinationMeshRenderer);
                    _analysisDirty = true;
                }
            }

            if (hasRestoreSnapshot)
            {
                EditorGUILayout.HelpBox(
                    "Crash-persistent exact restore snapshot is available. It uses GlobalObjectId references and survives domain reloads and Editor restarts while the source scene objects still exist.",
                    MessageType.Info);
            }
            else if (destinationMeshFilter.sharedMesh != null)
            {
                EditorGUILayout.HelpBox(
                    "Combined output exists but no exact restore snapshot is available. Force Recover is intentionally aggressive and can re-enable helpers/variants that were disabled before Combine.",
                    MessageType.Warning);
            }
        }
        EndSection();
    }

    private void TryAnalyzeAndCombine(
        MeshCombiner meshCombiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer)
    {
        serializedObject.ApplyModifiedProperties();
        RunAnalysis(meshCombiner, true);

        if (_analysisReport != null && _analysisReport.HasErrors)
        {
            int errorCount = _analysisReport.Issues.Count(issue => issue.Severity == MeshCombinerIssueSeverity.Error);
            Debug.LogError(
                "Mesh Combiner: analyzer blocked Combine for \"" + meshCombiner.name + "\" because " +
                errorCount + " blocking error(s) were found. Review Sources & Analyzer in the Inspector.",
                meshCombiner);
            return;
        }

        bool destructiveMode = meshCombiner.DestroyCombinedChildren;
        RestoreSnapshot restoreSnapshot = CaptureRestoreSnapshot(meshCombiner, destinationMeshFilter);

        if (!destructiveMode)
            SaveRestoreSnapshot(meshCombiner, restoreSnapshot);
        else
            ClearRestoreSnapshot(meshCombiner);

        Undo.RegisterFullObjectHierarchyUndo(meshCombiner.gameObject, "Combine Meshes");

        if (meshCombiner.TryCombineMeshes(true))
        {
            EditorUtility.SetDirty(meshCombiner);
            EditorUtility.SetDirty(destinationMeshFilter);
            EditorUtility.SetDirty(destinationMeshRenderer);
            _analysisDirty = true;
        }
        else
        {
            ClearRestoreSnapshot(meshCombiner);
        }
    }

    private void DrawAssetSettings(MeshCombiner meshCombiner, MeshFilter destinationMeshFilter)
    {
        BeginSection(ref _assetExpanded, "Combined Mesh Asset", "Optional persistence of the generated transient Mesh as an Asset under Assets/.");
        if (_assetExpanded)
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(
                _folderPath,
                new GUIContent(
                    "Folder Path",
                    "Relative folder under Assets where Save Combined Mesh creates the generated .asset file. Do not include the Assets/ prefix."));
            serializedObject.ApplyModifiedProperties();

            string folderPath = meshCombiner.FolderPath;
            bool isValidPath = IsValidPath(folderPath);
            Mesh mesh = destinationMeshFilter.sharedMesh;
            bool meshIsSaved = mesh != null && AssetDatabase.Contains(mesh);

            if (!isValidPath)
            {
                EditorGUILayout.HelpBox(
                    "Folder path is invalid. Use a relative path under Assets, for example Generated/CombinedMeshes.",
                    MessageType.Error);
            }

            using (new EditorGUI.DisabledScope(mesh == null || (!isValidPath && !meshIsSaved)))
            {
                string buttonText = meshIsSaved ? "Show Saved Combined Mesh" : "Save Combined Mesh";
                if (GUILayout.Button(new GUIContent(
                        buttonText,
                        meshIsSaved
                            ? "Pings the already saved Mesh asset in the Project window."
                            : "Creates a persistent .asset for the currently generated combined Mesh.")))
                {
                    meshCombiner.FolderPath = SaveCombinedMesh(mesh, folderPath);
                    EditorUtility.SetDirty(meshCombiner);
                }
            }
        }
        EndSection();
    }

    private void RunAnalysis(MeshCombiner meshCombiner, bool logSummary)
    {
        _analysisReport = MeshCombinerAnalyzer.Analyze(
            meshCombiner,
            GetUserIgnoredMeshFilters(),
            _combineInactiveChildren.boolValue,
            _createMultiMaterialMesh.boolValue,
            (LightmapUvMode)_lightmapUvMode.enumValueIndex);
        _analysisDirty = false;

        if (!logSummary || _analysisReport == null)
            return;

        int errorCount = _analysisReport.Issues.Count(issue => issue.Severity == MeshCombinerIssueSeverity.Error);
        int warningCount = _analysisReport.Issues.Count(issue => issue.Severity == MeshCombinerIssueSeverity.Warning);
        Debug.Log(
            "Mesh Combiner Analyzer: \"" + meshCombiner.name + "\" -> " +
            _analysisReport.Included.Count + " included, " + _analysisReport.Ignored.Count + " ignored, " +
            errorCount + " errors, " + warningCount + " warnings. Estimated output: " +
            _analysisReport.OutputSubmeshEstimate + " submesh/material draws, " +
            _analysisReport.ExpectedIndexFormat + ".",
            meshCombiner);
    }

    private IReadOnlyCollection<MeshFilter> GetUserIgnoredMeshFilters()
    {
        List<MeshFilter> result = new List<MeshFilter>();
        if (_meshFiltersToSkip == null || !_meshFiltersToSkip.isArray)
            return result;

        for (int i = 0; i < _meshFiltersToSkip.arraySize; i++)
        {
            MeshFilter meshFilter = _meshFiltersToSkip.GetArrayElementAtIndex(i).objectReferenceValue as MeshFilter;
            if (meshFilter != null)
                result.Add(meshFilter);
        }

        return result;
    }

    private void DrawAnalysisReport()
    {
        if (_analysisReport == null)
        {
            EditorGUILayout.HelpBox("No analyzer report is available yet.", MessageType.None);
            return;
        }

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Preflight Summary", EditorStyles.boldLabel);
            DrawMetric("Discovered MeshFilters", _analysisReport.TotalDiscoveredMeshFilters.ToString("N0"));
            DrawMetric("Included / Ignored", _analysisReport.Included.Count.ToString("N0") + " / " + _analysisReport.Ignored.Count.ToString("N0"));
            DrawMetric("Renderers", _analysisReport.SourceRendererCount.ToString("N0") + " -> " + (_analysisReport.Included.Count > 0 ? "1" : "0"));
            DrawMetric("Submesh/material draws (estimate)", _analysisReport.SourceSubmeshCount.ToString("N0") + " -> " + _analysisReport.OutputSubmeshEstimate.ToString("N0"));
            DrawMetric("Unique materials", _analysisReport.UniqueMaterialCount.ToString("N0"));
            DrawMetric("Source vertices", _analysisReport.SourceVertexCount.ToString("N0"));
            DrawMetric("Source triangles", _analysisReport.SourceTriangleCount.ToString("N0"));
            DrawMetric("Expected index format", _analysisReport.ExpectedIndexFormat.ToString());
        }

        _issuesExpanded = EditorGUILayout.Foldout(
            _issuesExpanded,
            "Issues & Notes (" + _analysisReport.Issues.Count + ")",
            true);
        if (_issuesExpanded)
        {
            foreach (MeshCombinerAnalysisIssue issue in _analysisReport.Issues)
            {
                MessageType messageType = MessageType.None;
                switch (issue.Severity)
                {
                    case MeshCombinerIssueSeverity.Info:
                        messageType = MessageType.Info;
                        break;
                    case MeshCombinerIssueSeverity.Warning:
                        messageType = MessageType.Warning;
                        break;
                    case MeshCombinerIssueSeverity.Error:
                        messageType = MessageType.Error;
                        break;
                }

                EditorGUILayout.HelpBox(issue.Message, messageType);
                if (issue.Context != null)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField("Context", issue.Context, issue.Context.GetType(), true);
                        if (GUILayout.Button("Ping", GUILayout.Width(48)))
                            EditorGUIUtility.PingObject(issue.Context);
                    }
                }
            }
        }

        _ignoredListExpanded = EditorGUILayout.Foldout(
            _ignoredListExpanded,
            "Ignored Sources (" + _analysisReport.Ignored.Count + ")",
            true);
        if (_ignoredListExpanded)
            DrawIgnoredSources();

        _includedListExpanded = EditorGUILayout.Foldout(
            _includedListExpanded,
            "Included Sources (" + _analysisReport.Included.Count + ")",
            true);
        if (_includedListExpanded)
        {
            foreach (MeshFilter meshFilter in _analysisReport.Included)
            {
                if (meshFilter == null) continue;
                EditorGUILayout.ObjectField(meshFilter, typeof(MeshFilter), true);
            }
        }
    }

    private void DrawIgnoredSources()
    {
        foreach (MeshCombinerIgnoreReason reason in Enum.GetValues(typeof(MeshCombinerIgnoreReason)))
        {
            List<MeshCombinerAnalysisEntry> entries = _analysisReport.Ignored
                .Where(entry => entry.Reason == reason)
                .ToList();
            if (entries.Count == 0)
                continue;

            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField(GetIgnoreReasonLabel(reason) + " (" + entries.Count + ")", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (MeshCombinerAnalysisEntry entry in entries)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    if (entry.MeshFilter != null)
                        EditorGUILayout.ObjectField(entry.MeshFilter, typeof(MeshFilter), true);
                    EditorGUILayout.LabelField(entry.Details, EditorStyles.wordWrappedMiniLabel);
                }
            }
            EditorGUI.indentLevel--;
        }
    }

    private static string GetIgnoreReasonLabel(MeshCombinerIgnoreReason reason)
    {
        switch (reason)
        {
            case MeshCombinerIgnoreReason.UserIgnore:
                return "User Ignore";
            case MeshCombinerIgnoreReason.InactiveHierarchy:
                return "Inactive Hierarchy";
            case MeshCombinerIgnoreReason.MissingMesh:
                return "Missing Mesh";
            case MeshCombinerIgnoreReason.MissingMeshRenderer:
                return "Missing MeshRenderer";
            case MeshCombinerIgnoreReason.DisabledMeshRenderer:
                return "Disabled MeshRenderer";
            case MeshCombinerIgnoreReason.NestedExactDuplicate:
                return "Nested Exact Duplicate";
            default:
                return reason.ToString();
        }
    }

    private static void DrawMetric(string label, string value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label, GUILayout.MinWidth(180));
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel, GUILayout.MaxWidth(150));
        }
    }

    private void PingFirstAnalysisError()
    {
        if (_analysisReport == null)
            return;

        MeshCombinerAnalysisIssue issue = _analysisReport.Issues
            .FirstOrDefault(item => item.Severity == MeshCombinerIssueSeverity.Error && item.Context != null);
        if (issue != null)
        {
            Selection.activeObject = issue.Context;
            EditorGUIUtility.PingObject(issue.Context);
        }
    }

    private static void BeginSection(ref bool expanded, string title, string tooltip)
    {
        EditorGUILayout.Space(3f);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        expanded = EditorGUILayout.Foldout(expanded, new GUIContent(title, tooltip), true, EditorStyles.foldoutHeader);
    }

    private static void EndSection()
    {
        EditorGUILayout.EndVertical();
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
                continue;

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
                continue;

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
                continue;

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
        if (meshCollider != null && (combinedMesh == null || meshCollider.sharedMesh == combinedMesh))
            meshCollider.sharedMesh = null;
    }

    private static void DestroyTransientCombinedMesh(Mesh generatedMesh)
    {
        if (generatedMesh != null && !AssetDatabase.Contains(generatedMesh))
            Undo.DestroyObjectImmediate(generatedMesh);
    }

    private static string GetStableObjectId(UnityEngine.Object target)
    {
        if (target == null)
            return string.Empty;

        GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(target);
        return id.Equals(default(GlobalObjectId)) ? string.Empty : id.ToString();
    }

    private static T ResolveObject<T>(int instanceId, string globalObjectId) where T : UnityEngine.Object
    {
        T currentSessionObject = EditorUtility.InstanceIDToObject(instanceId) as T;
        if (currentSessionObject != null)
            return currentSessionObject;

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
            objectKey = "instance-" + meshCombiner.GetInstanceID();

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
            return false;

        string normalized = folderPath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("Assets/"))
            return false;

        const string pattern = "[:*?\"<>|]";
        return !Regex.IsMatch(normalized, pattern);
    }

    private static string SaveCombinedMesh(Mesh mesh, string folderPath)
    {
        if (mesh == null)
            return folderPath;

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
            return folderPath;

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
                AssetDatabase.CreateFolder(currentPath, folderName);

            currentPath = nextPath;
        }
    }
}
