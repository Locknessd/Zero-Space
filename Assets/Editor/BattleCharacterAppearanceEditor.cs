using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BattleCharacterAppearance))]
public sealed class BattleCharacterAppearanceEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("catalog"));
        var appearance = (BattleCharacterAppearance)target;
        if (!appearance.catalog)
        {
            serializedObject.ApplyModifiedProperties();
            return;
        }
        var entries = appearance.catalog.characters;
        string[] names = entries.Select(e => e.displayName).ToArray();
        var property = serializedObject.FindProperty("startupCharacterId");
        string id = string.IsNullOrEmpty(property.stringValue) ? appearance.initialCharacterId : property.stringValue;
        int index = Math.Max(0, Array.FindIndex(entries, e => e.id == id));
        int selected = EditorGUILayout.Popup("Startup character", index, names);
        if (selected != index)
            property.stringValue = entries[selected].id;
        serializedObject.ApplyModifiedProperties();
        EditorGUILayout.LabelField("Current character", appearance.DisplayName ?? appearance.CurrentCharacterId);
        if (Application.isPlaying && GUILayout.Button("Apply selection"))
        {
            var game = GameManager.Instance;
            if (game)
                game.RequestCharacter(game.leftCombat.gameObject == appearance.gameObject
                    ? PlayerUI.Side.Left : PlayerUI.Side.Right, entries[selected].id);
        }
    }
}
