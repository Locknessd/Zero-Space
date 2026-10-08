using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    /// <summary>Runs existing lifecycle suites against a saved isolated BattleScene copy.</summary>
    [InitializeOnLoad]
    public static class CombatExpansionPlaySession
    {
        const string Key = "CombatExpansion.PlaySession";
        const string StatusPath = "GeneratedAssets/CombatExpansion/PlaySession.txt";
        static double nextCheck;

        static CombatExpansionPlaySession()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Changed;
        }

        public static void BeginGrapples() => Begin("Grapples", "GrapplePlayMode.txt");
        public static void BeginThrows() => Begin("Throws", "ThrowPlayMode.txt");

        static void Begin(string suite, string report)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetInt(Key + ".phase", 0) != 0)
                throw new InvalidOperationException("Start a single isolated lifecycle suite in Edit Mode.");
            PreserveDirtyScenes();
            string copy = "Assets/Editor/CombatExpansionPlay_" + Guid.NewGuid().ToString("N") + ".unity";
            SessionState.SetString(Key + ".copy", copy);
            SessionState.SetString(Key + ".previous", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetString(Key + ".suite", suite);
            SessionState.SetString(Key + ".report", CombatExpansionInventory.Output + "/" + report);
            SessionState.SetFloat(Key + ".began", (float)EditorApplication.timeSinceStartup);
            SessionState.SetInt(Key + ".phase", 1);
            File.WriteAllText(StatusPath, "RUNNING isolated " + suite + " lifecycle suite.\n");
            try
            {
                File.Copy(CombatExpansionInventory.Battle, copy);
                AssetDatabase.ImportAsset(copy, ImportAssetOptions.ForceSynchronousImport);
                var active = SceneManager.GetActiveScene();
                var scene = EditorSceneManager.OpenScene(copy, OpenSceneMode.Additive);
                try
                {
                    foreach (var socket in scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<WebSocketManager>(true)))
                        socket.gameObject.SetActive(false);
                    foreach (var game in scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<GameManager>(true)))
                        game.enableLocalInputTesting = false;
                    if (!EditorSceneManager.SaveScene(scene))
                        throw new IOException("Could not save isolated lifecycle scene.");
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                    if (active.IsValid() && active.isLoaded)
                        SceneManager.SetActiveScene(active);
                }
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(copy);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error)
            {
                File.AppendAllText(StatusPath, "FAIL setup: " + error + "\n");
                Restore();
                throw;
            }
        }

        static void PreserveDirtyScenes()
        {
            string folder = CombatExpansionInventory.Output + "/UnsavedScenes/" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty)
                    continue;
                Directory.CreateDirectory(folder);
                if (!EditorSceneManager.SaveScene(scene, folder + "/" + i + "_" + scene.name + ".unity", true))
                    throw new IOException("Could not preserve unsaved scene " + scene.name);
            }
        }

        static void Changed(PlayModeStateChange state)
        {
            int phase = SessionState.GetInt(Key + ".phase", 0);
            if (phase == 0)
                return;
            if (state == PlayModeStateChange.EnteredPlayMode)
                SessionState.SetInt(Key + ".phase", 2);
            if (state == PlayModeStateChange.ExitingPlayMode && phase < 4)
                File.AppendAllText(StatusPath, "FAIL interrupted before suite completion.\n");
            if (state == PlayModeStateChange.EnteredEditMode)
                Restore();
        }

        static void Tick()
        {
            int phase = SessionState.GetInt(Key + ".phase", 0);
            if (phase == 0 || phase == 4 || !EditorApplication.isPlaying || EditorApplication.isCompiling ||
                EditorApplication.timeSinceStartup < nextCheck)
                return;
            nextCheck = EditorApplication.timeSinceStartup + .2;
            try
            {
                if (EditorApplication.timeSinceStartup - SessionState.GetFloat(Key + ".began", 0) > 650)
                    throw new TimeoutException("Isolated lifecycle suite exceeded 650 seconds.");
                if (phase == 2)
                {
                    string copy = SessionState.GetString(Key + ".copy", "");
                    if (SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().path != copy)
                        throw new InvalidOperationException("Lifecycle scene isolation failed.");
                    var game = Object.FindAnyObjectByType<GameManager>();
                    if (!game || !game.leftCombat || !game.rightCombat ||
                        !game.leftCombat.IsIdleAndSettled || !game.rightCombat.IsIdleAndSettled)
                        return;
                    string suite = SessionState.GetString(Key + ".suite", "");
                    if (suite == "Grapples")
                        CombatExpansionGrapplePlayCheck.Begin();
                    else if (suite == "Throws")
                        CombatExpansionThrowPlayCheck.Begin();
                    else
                        throw new InvalidOperationException("Unknown lifecycle suite: " + suite);
                    SessionState.SetInt(Key + ".phase", 3);
                }
                else if (phase == 3)
                {
                    string report = File.ReadAllText(SessionState.GetString(Key + ".report", ""));
                    if (report.Contains("\nPASS all ") || report.Contains("\nFAIL"))
                        Finish(report.Contains("\nFAIL") ? "FAIL suite; inspect its report."
                            : "PASS suite; inspect its report for case scope.");
                }
            }
            catch (Exception error)
            {
                Finish("FAIL launcher: " + error);
            }
        }

        static void Finish(string message)
        {
            File.AppendAllText(StatusPath, message + "\n");
            SessionState.SetInt(Key + ".phase", 4);
            EditorApplication.isPlaying = false;
        }

        static void Restore()
        {
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                SessionState.GetString(Key + ".previous", ""));
            string copy = SessionState.GetString(Key + ".copy", "");
            if (!string.IsNullOrEmpty(copy) && File.Exists(copy))
                AssetDatabase.DeleteAsset(copy);
            SessionState.SetInt(Key + ".phase", 0);
            foreach (string field in new[] { ".copy", ".previous", ".suite", ".report" })
                SessionState.EraseString(Key + field);
            File.AppendAllText(StatusPath, "Restored previous play scene and removed temporary copy.\n");
        }
    }
}
