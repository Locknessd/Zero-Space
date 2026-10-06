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

        EditorGUILayout.LabelField("Pending stacks", gm.PendingEventCount.ToString());
        if (!string.IsNullOrEmpty(gm.QueueError))
            EditorGUILayout.HelpBox(gm.QueueError, MessageType.Error);
        EditorGUI.BeginDisabledGroup(!Application.isPlaying);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Q: Left Light"))
        {
            gm.DebugTriggerQ();
            // mark dirty so logs reflect any changes
            EditorUtility.SetDirty(gm);
        }
        if (GUILayout.Button("E: Right Heavy"))
        {
            gm.DebugTriggerE();
            EditorUtility.SetDirty(gm);
        }
        EditorGUILayout.EndHorizontal();
        if (GUILayout.Button("Reset Combat Queue")) gm.ResetCombatQueue();
        EditorGUI.EndDisabledGroup();
    }
}
#endif
