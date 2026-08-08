using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[CustomEditor(typeof(MeshCombiner))]
public class MeshCombinerEditor : Editor
{
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
    private bool _analysisDirty;

    private bool _sourcesExpanded = true;
    private bool _combineExpanded = true;
    private bool _lightmapExpanded = true;
    private bool _geometryExpanded;
    private bool _outputExpanded;
    private bool _assetExpanded;
    private bool _includedListExpanded;
    private bool _ignoredListExpanded;
    private bool _issuesExpanded;
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
        _analysisReport = null;
        _analysisDirty = false;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        MeshCombiner meshCombiner = (MeshCombiner)target;
        MeshFilter destinationMeshFilter = meshCombiner.GetComponent<MeshFilter>();
        MeshRenderer destinationMeshRenderer = meshCombiner.GetComponent<MeshRenderer>();
        MeshCombinerWorkflowState workflowState = MeshCombinerEditorState.GetWorkflowState(meshCombiner, destinationMeshFilter);

        DrawHeader(meshCombiner, destinationMeshFilter, destinationMeshRenderer, workflowState);

        EditorGUI.BeginChangeCheck();
        DrawSources(meshCombiner, workflowState);
        DrawCombineSettings(workflowState);
        DrawLightmapUvSettings(workflowState);
        DrawGeometryCleanupSettings(workflowState);
        DrawOutputSettings(workflowState);
        bool settingsChanged = EditorGUI.EndChangeCheck();

        if (serializedObject.ApplyModifiedProperties() || settingsChanged)
            _analysisDirty = _analysisReport != null;

        DrawActions(meshCombiner, destinationMeshFilter, destinationMeshRenderer, workflowState);
        DrawAssetSettings(meshCombiner, destinationMeshFilter, workflowState);
    }

    private void DrawHeader(
        MeshCombiner meshCombiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer,
        MeshCombinerWorkflowState state)
    {
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour(meshCombiner), typeof(MeshCombiner), false);
        }

        EditorGUILayout.Space(4f);
        switch (state)
        {
            case MeshCombinerWorkflowState.Ready:
                EditorGUILayout.HelpBox(
                    "READY — Configure the source set, optionally run Analyze / Validate, then Combine Meshes.",
                    MessageType.None);
                break;

            case MeshCombinerWorkflowState.Combined:
                DrawCombinedStatus(destinationMeshFilter, destinationMeshRenderer, true);
                break;

            case MeshCombinerWorkflowState.OutputWithoutSnapshot:
                DrawCombinedStatus(destinationMeshFilter, destinationMeshRenderer, false);
                EditorGUILayout.HelpBox(
                    "Combined output exists, but there is no exact restore snapshot. Force Recover is available, but it can re-enable helpers or variants that were disabled before Combine.",
                    MessageType.Warning);
                break;

            case MeshCombinerWorkflowState.InterruptedRecoverable:
                EditorGUILayout.HelpBox(
                    "RECOVERABLE — A pre-combine snapshot exists, but no combined output is assigned. The previous combine was likely interrupted. Use Restore Exact State before continuing.",
                    MessageType.Warning);
                break;
        }
    }

    private static void DrawCombinedStatus(
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer,
        bool exactRestoreAvailable)
    {
        Mesh mesh = destinationMeshFilter != null ? destinationMeshFilter.sharedMesh : null;
        bool baked = MeshCombinerEditorState.HasBakedLightmap(destinationMeshRenderer, out int lightmapIndex);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField(baked ? "COMBINED • BAKED" : "COMBINED", EditorStyles.boldLabel);
            if (mesh != null)
            {
                DrawMetric("Mesh", mesh.name);
                DrawMetric("Vertices", mesh.vertexCount.ToString("N0"));
                DrawMetric("Triangles", CountTriangles(mesh).ToString("N0"));
                DrawMetric("Submeshes", mesh.subMeshCount.ToString("N0"));
                DrawMetric("Index format", mesh.indexFormat.ToString());
            }

            DrawMetric("Materials", destinationMeshRenderer != null ? destinationMeshRenderer.sharedMaterials.Length.ToString("N0") : "0");
            DrawMetric("Baked lightmap", baked ? "Assigned (index " + lightmapIndex + ")" : "Not assigned");
            DrawMetric("Exact restore", exactRestoreAvailable ? "Available" : "Unavailable");
        }
    }

    private void DrawSources(MeshCombiner meshCombiner, MeshCombinerWorkflowState state)
    {
        BeginSection(ref _sourcesExpanded, "Sources", "Source filtering, explicit ignores and preflight analysis.");
        if (_sourcesExpanded)
        {
            bool ready = state == MeshCombinerWorkflowState.Ready;
            using (new EditorGUI.DisabledScope(!ready))
            {
                EditorGUILayout.PropertyField(
                    _combineInactiveChildren,
                    new GUIContent(
                        "Combine Inactive Children",
                        "Includes MeshFilters under inactive child GameObjects. When disabled, inactive hierarchy branches are ignored and can be inspected in the Analyzer report."));

                EditorGUILayout.PropertyField(
                    _meshFiltersToSkip,
                    new GUIContent(
                        "User Ignore List",
                        "Explicit source exclusions. These are separate from automatic ignore rules and are reported as User Ignore by the Analyzer."),
                    true);
            }

            if (!ready)
            {
                EditorGUILayout.HelpBox(
                    state == MeshCombinerWorkflowState.InterruptedRecoverable
                        ? "Source analysis is paused until the interrupted state is restored."
                        : "Source analysis is paused while combined output is active. Restore sources before changing or re-analyzing the source set.",
                    MessageType.None);
                return;
            }

            EditorGUILayout.Space(3f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent(
                        _analysisReport == null ? "Analyze / Validate" : "Refresh Analysis",
                        "Non-destructive preflight. Shows exactly which sources will be included or ignored and checks blocking material/UV/topology conditions."),
                    GUILayout.Height(26f)))
                {
                    RunAnalysis(meshCombiner, true);
                }

                bool hasContextError = _analysisReport != null && _analysisReport.Issues.Any(
                    issue => issue.Severity == MeshCombinerIssueSeverity.Error && issue.Context != null);
                using (new EditorGUI.DisabledScope(!hasContextError))
                {
                    if (GUILayout.Button(new GUIContent("Ping Error", "Selects the context object of the first blocking Analyzer error."), GUILayout.Width(92f), GUILayout.Height(26f)))
                        PingFirstAnalysisError();
                }
            }

            if (_analysisReport != null)
            {
                if (_analysisDirty)
                    EditorGUILayout.LabelField("Analysis is stale — refresh before relying on the report.", EditorStyles.miniLabel);

                DrawAnalysisReport();
            }
            else
            {
                EditorGUILayout.LabelField("Analyzer is optional; Combine Meshes always runs the same validation automatically.", EditorStyles.miniLabel);
            }
        }
        EndSection();
    }

    private void DrawCombineSettings(MeshCombinerWorkflowState state)
    {
        BeginSection(ref _combineExpanded, "Combine", "Material/submesh behavior for the generated mesh.");
        if (_combineExpanded)
        {
            using (new EditorGUI.DisabledScope(state != MeshCombinerWorkflowState.Ready))
            {
                EditorGUILayout.PropertyField(
                    _createMultiMaterialMesh,
                    new GUIContent(
                        "Create Multi-Material Mesh",
                        "Preserves different shared Material references as separate output submeshes. Turn this off only when every included source submesh uses the same Material reference."));
            }

            EditorGUILayout.LabelField(
                _createMultiMaterialMesh.boolValue
                    ? "Different materials remain separate submeshes/draw submissions."
                    : "Single-material mode blocks Combine if included sources use different material references.",
                EditorStyles.miniLabel);
        }
        EndSection();
    }

    private void DrawLightmapUvSettings(MeshCombinerWorkflowState state)
    {
        BeginSection(ref _lightmapExpanded, "Lightmap UV2", "Controls Mesh.uv2 generation on the combined mesh.");
        if (_lightmapExpanded)
        {
            using (new EditorGUI.DisabledScope(state != MeshCombinerWorkflowState.Ready))
            {
                LightmapUvMode currentMode = (LightmapUvMode)_lightmapUvMode.enumValueIndex;
                LightmapUvMode newMode = (LightmapUvMode)EditorGUILayout.EnumPopup(
                    new GUIContent(
                        "Lightmap UV Mode",
                        "Select the UV2 strategy for baked lighting. Only settings relevant to the active strategy are shown."),
                    currentMode);
                if (newMode != currentMode)
                    _lightmapUvMode.enumValueIndex = (int)newMode;

                LightmapUvMode mode = (LightmapUvMode)_lightmapUvMode.enumValueIndex;
                bool showPackingSettings = mode == LightmapUvMode.PreserveAndRepackSourceUv2 || mode == LightmapUvMode.CoplanarStitchedUv2;
                if (showPackingSettings)
                {
                    EditorGUILayout.IntSlider(
                        _repackPaddingTexels,
                        1,
                        16,
                        new GUIContent(
                            "Chart Padding (texels)",
                            "Padding around generated UV2 charts. More padding reduces cross-chart filtering risk but consumes more atlas space."));
                    DrawReferenceResolutionPopup();
                }

                DrawUvModeDescription(mode);
            }
        }
        EndSection();
    }

    private static void DrawUvModeDescription(LightmapUvMode mode)
    {
        switch (mode)
        {
            case LightmapUvMode.None:
                EditorGUILayout.LabelField("Leaves UV2 untouched.", EditorStyles.miniLabel);
                break;
            case LightmapUvMode.PreserveSourceUv2:
                EditorGUILayout.LabelField("Copies authored UV2 unchanged; reused modular UV2 can overlap after combine.", EditorStyles.miniLabel);
                break;
            case LightmapUvMode.PreserveAndRepackSourceUv2:
                EditorGUILayout.LabelField("Reuses authored charts and repacks them without changing chart topology.", EditorStyles.miniLabel);
                break;
            case LightmapUvMode.RegenerateUv2:
                EditorGUILayout.LabelField("Runs Unity secondary UV unwrap on the final mesh; Unity may split vertices.", EditorStyles.miniLabel);
                break;
            case LightmapUvMode.CoplanarStitchedUv2:
                EditorGUILayout.HelpBox(
                    "Recommended for modular static architecture. Exact and partial/T-junction coplanar boundaries become continuous UV2 charts. Render geometry, UV0, normals, tangents and materials are unchanged.",
                    MessageType.Info);
                EditorGUILayout.LabelField("Advanced tuning: Tools > Mesh Combiner > Coplanar Stitched UV2...", EditorStyles.miniLabel);
                break;
        }
    }

    private void DrawReferenceResolutionPopup()
    {
        int[] resolutions = { 256, 512, 1024, 2048, 4096 };
        string[] labels = { "256", "512", "1024", "2048", "4096" };
        int current = _repackPaddingReferenceResolution.intValue;
        if (!resolutions.Contains(current)) current = 512;

        int selected = Mathf.Max(0, Array.IndexOf(resolutions, current));
        selected = EditorGUILayout.Popup(
            new GUIContent(
                "Padding Reference Size",
                "Reference atlas resolution used to convert Chart Padding from texels to normalized UV space."),
            selected,
            labels);
        _repackPaddingReferenceResolution.intValue = resolutions[Mathf.Clamp(selected, 0, resolutions.Length - 1)];
    }

    private void DrawGeometryCleanupSettings(MeshCombinerWorkflowState state)
    {
        BeginSection(ref _geometryExpanded, "Geometry Cleanup", "Optional conservative geometry cleanup. Disabled by default.");
        if (_geometryExpanded)
        {
            using (new EditorGUI.DisabledScope(state != MeshCombinerWorkflowState.Ready))
            {
                EditorGUILayout.PropertyField(
                    _removeExactOpposingFaces,
                    new GUIContent(
                        "Remove Exact Opposing Faces",
                        "Removes only exact coincident opposite-wound triangle pairs. This is not a Boolean union and does not remove partially overlapping polygons."));

                if (_removeExactOpposingFaces.boolValue)
                {
                    EditorGUILayout.PropertyField(
                        _exactFacePositionTolerance,
                        new GUIContent(
                            "Position Tolerance",
                            "Destination-local position tolerance for exact opposing triangle matching. 0.0001 equals 0.1 mm when one Unity unit is one meter."));
                    if (_exactFacePositionTolerance.floatValue < 0.000001f)
                        _exactFacePositionTolerance.floatValue = 0.000001f;
                }
            }
        }
        EndSection();
    }

    private void DrawOutputSettings(MeshCombinerWorkflowState state)
    {
        BeginSection(ref _outputExpanded, "Output", "Source handling and optional collider output.");
        if (_outputExpanded)
        {
            using (new EditorGUI.DisabledScope(state != MeshCombinerWorkflowState.Ready))
            {
                EditorGUILayout.PropertyField(
                    _deactivateCombinedChildren,
                    new GUIContent(
                        "Deactivate Combined Children",
                        "Disables included source GameObjects after a successful combine. Exact Restore remembers their previous active state."));

                if (!_deactivateCombinedChildren.boolValue)
                {
                    EditorGUILayout.PropertyField(
                        _deactivateCombinedChildrenMeshRenderers,
                        new GUIContent(
                            "Disable Child MeshRenderers",
                            "Keeps included GameObjects active but disables their MeshRenderer components. Child scripts and Colliders remain active."));
                }

                EditorGUILayout.PropertyField(
                    _updateOrCreateMeshCollider,
                    new GUIContent(
                        "Update/Create MeshCollider",
                        "Creates or updates a MeshCollider on the combiner root and assigns the final combined mesh. Existing child Colliders are not removed."));

                _destructiveExpanded = EditorGUILayout.Foldout(
                    _destructiveExpanded,
                    new GUIContent("Destructive source handling", "Advanced option for permanently deleting included source GameObjects."),
                    true);
                if (_destructiveExpanded)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(
                        _destroyCombinedChildren,
                        new GUIContent(
                            "Destroy Combined Children",
                            "Permanently deletes included source GameObjects. Exact Restore and Force Recover cannot recreate destroyed hierarchy objects."));
                    EditorGUI.indentLevel--;

                    if (_destroyCombinedChildren.boolValue)
                    {
                        _deactivateCombinedChildren.boolValue = false;
                        _deactivateCombinedChildrenMeshRenderers.boolValue = false;
                        EditorGUILayout.HelpBox("Destructive mode cannot be recovered from a snapshot.", MessageType.Error);
                    }
                }
            }
        }
        EndSection();
    }

    private void DrawActions(
        MeshCombiner meshCombiner,
        MeshFilter destinationMeshFilter,
        MeshRenderer destinationMeshRenderer,
        MeshCombinerWorkflowState state)
    {
        EditorGUILayout.Space(8f);
        bool destructive = meshCombiner.DestroyCombinedChildren;
        bool exactRestoreAvailable = MeshCombinerEditorState.HasRestoreSnapshot(meshCombiner);

        switch (state)
        {
            case MeshCombinerWorkflowState.Ready:
                if (GUILayout.Button(new GUIContent(
                        "Combine Meshes",
                        "Runs the Analyzer automatically, blocks on validation errors, saves a crash-persistent restore snapshot, then combines the eligible sources."),
                    GUILayout.Height(34f)))
                {
                    TryAnalyzeAndCombine(meshCombiner, destinationMeshFilter, destinationMeshRenderer);
                }
                break;

            case MeshCombinerWorkflowState.Combined:
                using (new EditorGUI.DisabledScope(destructive || !exactRestoreAvailable))
                {
                    if (GUILayout.Button(new GUIContent("Restore Exact State", "Restores the exact pre-combine GameObject active and MeshRenderer enabled states."), GUILayout.Height(34f)))
                    {
                        if (MeshCombinerEditorState.RestoreExactState(meshCombiner, destinationMeshFilter, destinationMeshRenderer))
                            ResetAnalysisAfterRecovery();
                    }
                }
                break;

            case MeshCombinerWorkflowState.InterruptedRecoverable:
                using (new EditorGUI.DisabledScope(destructive || !exactRestoreAvailable))
                {
                    if (GUILayout.Button(new GUIContent("Restore Interrupted State", "Uses the crash-persistent snapshot captured before the interrupted combine."), GUILayout.Height(34f)))
                    {
                        if (MeshCombinerEditorState.RestoreExactState(meshCombiner, destinationMeshFilter, destinationMeshRenderer))
                            ResetAnalysisAfterRecovery();
                    }
                }
                break;

            case MeshCombinerWorkflowState.OutputWithoutSnapshot:
                if (GUILayout.Button(new GUIContent(
                        "Force Recover Sources",
                        "Emergency recovery without a snapshot. Clears output and aggressively activates descendant MeshFilter GameObjects and MeshRenderers."),
                    GUILayout.Height(34f)))
                {
                    MeshCombinerEditorState.ForceRecover(meshCombiner, destinationMeshFilter, destinationMeshRenderer);
                    ResetAnalysisAfterRecovery();
                }
                break;
        }

        if ((state == MeshCombinerWorkflowState.Combined || state == MeshCombinerWorkflowState.InterruptedRecoverable) && !destructive)
        {
            EditorGUILayout.Space(2f);
            if (GUILayout.Button(new GUIContent(
                    "Force Recover Sources (No Snapshot)",
                    "Emergency fallback. Clears combined output and re-enables descendant source objects/renderers without respecting the exact pre-combine disabled state."),
                GUILayout.Height(22f)))
            {
                MeshCombinerEditorState.ForceRecover(meshCombiner, destinationMeshFilter, destinationMeshRenderer);
                ResetAnalysisAfterRecovery();
            }
        }
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
                errorCount + " blocking error(s) were found. Review Sources in the Inspector.",
                meshCombiner);
            return;
        }

        bool destructiveMode = meshCombiner.DestroyCombinedChildren;
        if (!destructiveMode)
            MeshCombinerEditorState.CaptureAndSaveSnapshot(meshCombiner, destinationMeshFilter);
        else
            MeshCombinerEditorState.ClearRestoreSnapshot(meshCombiner);

        Undo.RegisterFullObjectHierarchyUndo(meshCombiner.gameObject, "Combine Meshes");
        if (meshCombiner.TryCombineMeshes(true))
        {
            EditorUtility.SetDirty(meshCombiner);
            EditorUtility.SetDirty(destinationMeshFilter);
            EditorUtility.SetDirty(destinationMeshRenderer);
            _analysisReport = null;
            _analysisDirty = false;
        }
        else
        {
            MeshCombinerEditorState.ClearRestoreSnapshot(meshCombiner);
        }
    }

    private void DrawAssetSettings(
        MeshCombiner meshCombiner,
        MeshFilter destinationMeshFilter,
        MeshCombinerWorkflowState state)
    {
        if (state != MeshCombinerWorkflowState.Combined && state != MeshCombinerWorkflowState.OutputWithoutSnapshot)
            return;

        BeginSection(ref _assetExpanded, "Combined Mesh Asset", "Optionally save the generated transient Mesh as a persistent Asset under Assets/.");
        if (_assetExpanded)
        {
            EditorGUILayout.PropertyField(
                _folderPath,
                new GUIContent(
                    "Folder Path",
                    "Relative folder under Assets where Save Combined Mesh creates the .asset file. Do not include the Assets/ prefix."));
            serializedObject.ApplyModifiedProperties();

            string folderPath = meshCombiner.FolderPath;
            bool isValidPath = IsValidPath(folderPath);
            Mesh mesh = destinationMeshFilter != null ? destinationMeshFilter.sharedMesh : null;
            bool meshIsSaved = mesh != null && AssetDatabase.Contains(mesh);

            if (!isValidPath && !meshIsSaved)
                EditorGUILayout.HelpBox("Use a relative path under Assets, for example Generated/CombinedMeshes.", MessageType.Error);

            using (new EditorGUI.DisabledScope(mesh == null || (!isValidPath && !meshIsSaved)))
            {
                string buttonText = meshIsSaved ? "Show Saved Mesh" : "Save Combined Mesh";
                if (GUILayout.Button(buttonText))
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

        if (!logSummary || _analysisReport == null) return;

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
        if (_meshFiltersToSkip == null || !_meshFiltersToSkip.isArray) return result;

        for (int i = 0; i < _meshFiltersToSkip.arraySize; i++)
        {
            MeshFilter meshFilter = _meshFiltersToSkip.GetArrayElementAtIndex(i).objectReferenceValue as MeshFilter;
            if (meshFilter != null) result.Add(meshFilter);
        }
        return result;
    }

    private void DrawAnalysisReport()
    {
        if (_analysisReport == null) return;

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Preflight", EditorStyles.boldLabel);
            DrawMetric("Discovered", _analysisReport.TotalDiscoveredMeshFilters.ToString("N0"));
            DrawMetric("Included", _analysisReport.Included.Count.ToString("N0"));
            DrawMetric("Ignored", _analysisReport.Ignored.Count.ToString("N0"));
            DrawMetric("Renderers", _analysisReport.SourceRendererCount.ToString("N0") + " → " + (_analysisReport.Included.Count > 0 ? "1" : "0"));
            DrawMetric("Draws (estimate)", _analysisReport.SourceSubmeshCount.ToString("N0") + " → " + _analysisReport.OutputSubmeshEstimate.ToString("N0"));
            DrawMetric("Vertices", _analysisReport.SourceVertexCount.ToString("N0"));
            DrawMetric("Triangles", _analysisReport.SourceTriangleCount.ToString("N0"));
            DrawMetric("Index format", _analysisReport.ExpectedIndexFormat.ToString());
        }

        int errorCount = _analysisReport.Issues.Count(issue => issue.Severity == MeshCombinerIssueSeverity.Error);
        int warningCount = _analysisReport.Issues.Count(issue => issue.Severity == MeshCombinerIssueSeverity.Warning);
        int infoCount = _analysisReport.Issues.Count(issue => issue.Severity == MeshCombinerIssueSeverity.Info);

        _issuesExpanded = EditorGUILayout.Foldout(
            _issuesExpanded,
            "Issues " + FormatCountTriplet(errorCount, warningCount, infoCount),
            true);
        if (_issuesExpanded)
        {
            foreach (MeshCombinerAnalysisIssue issue in _analysisReport.Issues)
            {
                EditorGUILayout.HelpBox(issue.Message, ToMessageType(issue.Severity));
                if (issue.Context != null)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField(issue.Context, issue.Context.GetType(), true);
                        if (GUILayout.Button("Ping", GUILayout.Width(48f))) EditorGUIUtility.PingObject(issue.Context);
                    }
                }
            }
        }

        _ignoredListExpanded = EditorGUILayout.Foldout(_ignoredListExpanded, "Ignored Sources (" + _analysisReport.Ignored.Count + ")", true);
        if (_ignoredListExpanded) DrawIgnoredSources();

        _includedListExpanded = EditorGUILayout.Foldout(_includedListExpanded, "Included Sources (" + _analysisReport.Included.Count + ")", true);
        if (_includedListExpanded)
        {
            foreach (MeshFilter meshFilter in _analysisReport.Included)
                if (meshFilter != null) EditorGUILayout.ObjectField(meshFilter, typeof(MeshFilter), true);
        }
    }

    private void DrawIgnoredSources()
    {
        foreach (MeshCombinerIgnoreReason reason in Enum.GetValues(typeof(MeshCombinerIgnoreReason)))
        {
            List<MeshCombinerAnalysisEntry> entries = _analysisReport.Ignored.Where(entry => entry.Reason == reason).ToList();
            if (entries.Count == 0) continue;

            EditorGUILayout.LabelField(GetIgnoreReasonLabel(reason) + " (" + entries.Count + ")", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            foreach (MeshCombinerAnalysisEntry entry in entries)
            {
                if (entry.MeshFilter != null) EditorGUILayout.ObjectField(entry.MeshFilter, typeof(MeshFilter), true);
                EditorGUILayout.LabelField(entry.Details, EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(2f);
        }
    }

    private static string GetIgnoreReasonLabel(MeshCombinerIgnoreReason reason)
    {
        switch (reason)
        {
            case MeshCombinerIgnoreReason.UserIgnore: return "User Ignore";
            case MeshCombinerIgnoreReason.InactiveHierarchy: return "Inactive Hierarchy";
            case MeshCombinerIgnoreReason.MissingMesh: return "Missing Mesh";
            case MeshCombinerIgnoreReason.MissingMeshRenderer: return "Missing MeshRenderer";
            case MeshCombinerIgnoreReason.DisabledMeshRenderer: return "Disabled MeshRenderer";
            case MeshCombinerIgnoreReason.NestedExactDuplicate: return "Nested Exact Duplicate";
            default: return reason.ToString();
        }
    }

    private static MessageType ToMessageType(MeshCombinerIssueSeverity severity)
    {
        switch (severity)
        {
            case MeshCombinerIssueSeverity.Error: return MessageType.Error;
            case MeshCombinerIssueSeverity.Warning: return MessageType.Warning;
            case MeshCombinerIssueSeverity.Info: return MessageType.Info;
            default: return MessageType.None;
        }
    }

    private static string FormatCountTriplet(int errors, int warnings, int infos)
    {
        return "(" + errors + " errors, " + warnings + " warnings, " + infos + " info)";
    }

    private void PingFirstAnalysisError()
    {
        if (_analysisReport == null) return;
        MeshCombinerAnalysisIssue issue = _analysisReport.Issues.FirstOrDefault(item => item.Severity == MeshCombinerIssueSeverity.Error && item.Context != null);
        if (issue != null)
        {
            Selection.activeObject = issue.Context;
            EditorGUIUtility.PingObject(issue.Context);
        }
    }

    private void ResetAnalysisAfterRecovery()
    {
        _analysisReport = null;
        _analysisDirty = false;
        Repaint();
    }

    private static void BeginSection(ref bool expanded, string title, string tooltip)
    {
        EditorGUILayout.Space(4f);
        expanded = EditorGUILayout.BeginFoldoutHeaderGroup(expanded, new GUIContent(title, tooltip));
        if (expanded) EditorGUI.indentLevel++;
    }

    private static void EndSection()
    {
        if (EditorGUI.indentLevel > 0) EditorGUI.indentLevel--;
        EditorGUILayout.EndFoldoutHeaderGroup();
    }

    private static void DrawMetric(string label, string value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(label);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(value, EditorStyles.boldLabel, GUILayout.MaxWidth(190f));
        }
    }

    private static long CountTriangles(Mesh mesh)
    {
        if (mesh == null) return 0;
        long triangles = 0;
        for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
        {
            if (mesh.GetTopology(subMeshIndex) == MeshTopology.Triangles)
                triangles += (long)mesh.GetIndexCount(subMeshIndex) / 3L;
        }
        return triangles;
    }

    private static bool IsValidPath(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return false;
        string normalized = folderPath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("Assets/")) return false;
        const string pattern = "[:*?\"<>|]";
        return !Regex.IsMatch(normalized, pattern);
    }

    private static string SaveCombinedMesh(Mesh mesh, string folderPath)
    {
        if (mesh == null) return folderPath;
        if (AssetDatabase.Contains(mesh))
        {
            EditorGUIUtility.PingObject(mesh);
            return folderPath;
        }

        folderPath = folderPath.Replace('\\', '/').Trim('/');
        EnsureFolderExists(folderPath);
        string meshPath = AssetDatabase.GenerateUniqueAssetPath("Assets/" + folderPath + "/" + mesh.name + ".asset");
        AssetDatabase.CreateAsset(mesh, meshPath);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(mesh);
        Debug.Log("Mesh Combiner: saved combined mesh to \"" + meshPath + "\".");

        string directory = System.IO.Path.GetDirectoryName(meshPath);
        if (string.IsNullOrEmpty(directory)) return folderPath;
        directory = directory.Replace('\\', '/');
        return directory.StartsWith("Assets/") ? directory.Substring("Assets/".Length) : folderPath;
    }

    private static void EnsureFolderExists(string folderPath)
    {
        string currentPath = "Assets";
        foreach (string rawFolderName in folderPath.Split('/').Where(part => !string.IsNullOrWhiteSpace(part)))
        {
            string folderName = rawFolderName.Trim();
            string nextPath = currentPath + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(nextPath)) AssetDatabase.CreateFolder(currentPath, folderName);
            currentPath = nextPath;
        }
    }
}
