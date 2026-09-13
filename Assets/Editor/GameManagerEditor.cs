#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GameManager))]
public class GameManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Debug Test", EditorStyles.boldLabel);

        var gm = target as GameManager;
        if (gm == null) return;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Trigger Q (Left attacks)"))
        {
            gm.DebugTriggerQ();
            // mark dirty so logs reflect any changes
            EditorUtility.SetDirty(gm);
        }
        if (GUILayout.Button("Trigger E (Right attacks)"))
        {
            gm.DebugTriggerE();
            EditorUtility.SetDirty(gm);
        }
        EditorGUILayout.EndHorizontal();
    }
}
#endif
