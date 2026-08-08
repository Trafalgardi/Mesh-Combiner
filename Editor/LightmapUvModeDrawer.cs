using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(LightmapUvMode))]
public sealed class LightmapUvModeDrawer : PropertyDrawer
{
    private const float HelpBoxHeight = 54f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if ((LightmapUvMode)property.intValue != LightmapUvMode.CoplanarStitchedUv2)
            return EditorGUIUtility.singleLineHeight;

        return EditorGUIUtility.singleLineHeight * 3f +
               EditorGUIUtility.standardVerticalSpacing * 3f +
               HelpBoxHeight;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        float line = EditorGUIUtility.singleLineHeight;
        float spacing = EditorGUIUtility.standardVerticalSpacing;
        Rect row = new Rect(position.x, position.y, position.width, line);

        LightmapUvMode mode = (LightmapUvMode)property.intValue;
        EditorGUI.BeginChangeCheck();
        mode = (LightmapUvMode)EditorGUI.EnumPopup(row, label, mode);
        if (EditorGUI.EndChangeCheck())
            property.intValue = (int)mode;

        if (mode == LightmapUvMode.CoplanarStitchedUv2)
        {
            SerializedProperty paddingTexels = property.serializedObject.FindProperty("repackPaddingTexels");
            SerializedProperty referenceResolution = property.serializedObject.FindProperty("repackPaddingReferenceResolution");

            row.y += line + spacing;
            if (paddingTexels != null)
            {
                paddingTexels.intValue = EditorGUI.IntSlider(
                    row,
                    new GUIContent(
                        "Chart Padding (texels)",
                        "Padding between generated coplanar lightmap charts."),
                    paddingTexels.intValue,
                    1,
                    16);
            }

            row.y += line + spacing;
            if (referenceResolution != null)
            {
                int[] values = { 256, 512, 1024, 2048, 4096 };
                GUIContent[] labels =
                {
                    new GUIContent("256"),
                    new GUIContent("512"),
                    new GUIContent("1024"),
                    new GUIContent("2048"),
                    new GUIContent("4096")
                };

                int current = referenceResolution.intValue;
                bool supported = false;
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] == current)
                    {
                        supported = true;
                        break;
                    }
                }
                if (!supported) current = 512;

                referenceResolution.intValue = EditorGUI.IntPopup(
                    row,
                    new GUIContent(
                        "Padding Reference Size",
                        "Chart padding is converted to normalized UV space using this reference resolution."),
                    current,
                    labels,
                    values);
            }

            row.y += line + spacing;
            row.height = HelpBoxHeight;
            EditorGUI.HelpBox(
                row,
                "Coplanar Stitched UV2 runs automatically during Combine. It joins exact and T-junction/partial coplanar boundaries into continuous lightmap charts. Standard mode uses the validated defaults: 0.0001 world-space edge tolerance and 1 degree coplanar angle. Use Tools > Mesh Combiner > Coplanar Stitched UV2 for advanced manual tuning.",
                MessageType.Info);
        }

        EditorGUI.EndProperty();
    }
}
