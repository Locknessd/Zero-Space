using System.IO;
using MemeArena;
using UnityEditor;
using UnityEngine;

namespace MemeArenaEditor
{
    [CustomEditor(typeof(MemeArenaStage))]
    public sealed class MemeArenaStageEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var stage = (MemeArenaStage)target;
            EditorGUILayout.HelpBox("Regeneration creates a new prefab version and preserves this version. " +
                "Configure sizes, art and density on the settings asset.", MessageType.Info);
            if (GUILayout.Button("Select / edit stage settings")) Selection.activeObject = stage.settings;
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Regenerate Arena — new version"))
                    Selection.activeObject = MemeArenaBuild.Generate(stage.settings);
                if (GUILayout.Button("Populate Meme Crowd — new version"))
                    Selection.activeObject = MemeArenaBuild.Generate(stage.settings);
            }
            if (GUILayout.Button("Preview Crowd Reactions")) MemeArenaCrowdPreview.Begin(stage);
            if (GUILayout.Button("Validate Generated Stage")) MemeArenaValidation.ValidatePrefab(stage.gameObject);
        }

        [MenuItem("Tools/Meme Arena/Validate Generated Stage")]
        public static void ValidateLatest()
        {
            string path = File.ReadAllText(MemeArenaBuild.Root + "/Documentation/LatestGenerated.txt").Trim();
            MemeArenaValidation.ValidatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path));
        }

        [MenuItem("Tools/Meme Arena/Create playable battle copy")]
        public static void BattleCopy() => MemeArenaIntegration.CreateBattleVariant();
    }

    [InitializeOnLoad]
    public static class MemeArenaCrowdPreview
    {
        static MemeCrowdReactionManager manager;
        static double start;
        static int previousMode;

        static MemeArenaCrowdPreview() => EditorApplication.update += Tick;

        public static void Begin(MemeArenaStage stage)
        {
            if (EditorUtility.IsPersistent(stage))
            {
                Debug.Log("Open the generated battle scene, select its stage instance, then preview the crowd.");
                return;
            }
            manager = stage.GetComponentInChildren<MemeCrowdReactionManager>();
            manager.RebuildCache();
            start = EditorApplication.timeSinceStartup;
            previousMode = -1;
        }

        static void Tick()
        {
            if (!manager || Application.isPlaying) return;
            double elapsed = EditorApplication.timeSinceStartup - start;
            if (elapsed > 12)
            {
                foreach (var fan in manager.GetComponentsInChildren<MemeCrowdFan>()) fan.RestorePose();
                manager.ResetReactions();
                manager = null;
                return;
            }
            float now = (float)EditorApplication.timeSinceStartup;
            int mode = Mathf.Min(5, (int)elapsed / 2);
            if (mode != previousMode)
            {
                previousMode = mode;
                foreach (var group in manager.GetComponentsInChildren<MemeCrowdGroup>())
                    group.React((MemeCrowdReaction)mode, 1.5f, 1.8f, now);
            }
            manager.Tick(now);
            SceneView.RepaintAll();
        }
    }
}
