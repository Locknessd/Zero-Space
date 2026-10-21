using MemeArena;
using UnityEditor;
using UnityEngine;

namespace MemeArenaEditor
{
    public sealed class MemeColosseumGenerator : EditorWindow
    {
        MemeArenaSettings settings;
        GameObject lastPrefab;
        UnityEditor.Editor settingsEditor;
        Vector2 scroll;

        [MenuItem("Tools/Meme Arena/Generate Test Arena")]
        public static void Open() => GetWindow<MemeColosseumGenerator>("Meme Colosseum");

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("MEME COLOSSEUM · UNITY 6 URP", EditorStyles.boldLabel);
            settings = (MemeArenaSettings)EditorGUILayout.ObjectField("Settings", settings,
                typeof(MemeArenaSettings), false);
            if (!settings && GUILayout.Button("Create editable settings"))
            {
                System.IO.Directory.CreateDirectory(MemeArenaBuild.Generated);
                AssetDatabase.Refresh();
                settings = CreateInstance<MemeArenaSettings>();
                AssetDatabase.CreateAsset(settings, AssetDatabase.GenerateUniqueAssetPath(
                    MemeArenaBuild.Generated + "/StageSettings.asset"));
            }
            if (settings)
            {
                UnityEditor.Editor.CreateCachedEditor(settings, null, ref settingsEditor);
                settingsEditor.OnInspectorGUI();
            }
            EditorGUILayout.HelpBox("Generate saves a new version. User art references are copied; " +
                "existing scenes and prefabs are preserved.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Generate Arena — new prefab", GUILayout.Height(34)))
                {
                    lastPrefab = MemeArenaBuild.Generate(settings);
                    Selection.activeObject = lastPrefab;
                }
            if (lastPrefab && GUILayout.Button("Select generated prefab")) Selection.activeObject = lastPrefab;
            if (lastPrefab && GUILayout.Button("Regenerate Arena — new version"))
                lastPrefab = MemeArenaBuild.Generate(lastPrefab.GetComponent<MemeArenaStage>().settings);
            if (lastPrefab && GUILayout.Button("Populate Meme Crowd — new version"))
                lastPrefab = MemeArenaBuild.Generate(lastPrefab.GetComponent<MemeArenaStage>().settings);
            if (lastPrefab && GUILayout.Button("Validate Generated Stage")) MemeArenaValidation.ValidatePrefab(lastPrefab);
            if (GUILayout.Button("Preview Crowd Reactions (selected scene stage)"))
            {
                var selected = Selection.activeGameObject ? Selection.activeGameObject.GetComponent<MemeArenaStage>() : null;
                if (selected) MemeArenaCrowdPreview.Begin(selected);
            }
            EditorGUILayout.EndScrollView();
        }

        void OnDisable()
        {
            if (settingsEditor) DestroyImmediate(settingsEditor);
        }
    }
}
