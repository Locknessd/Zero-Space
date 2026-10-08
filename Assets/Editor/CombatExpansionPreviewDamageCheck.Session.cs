using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionPreviewDamageCheck
    {
        [MenuItem("Tools/Battle/Validation/Preview damage")]
        public static void Begin()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool(Key + ".restore", false),
                "Begin once in Edit Mode; the validator owns its isolated Play Mode session.");
            Require(!File.Exists(Copy), "Temporary preview validation scene already exists: " + Copy);
            PreserveDirtyScenes();
            ResetFields();
            report.AppendLine("RUNNING: isolated saved BattleScene; both avatars, HOLD/ESC normal/lethal requests, " +
                "light normal/lethal regression, Stop and Exit during damage, saved HUD and authoritative HP.");
            Write();
            SessionState.SetString(Key + ".previous",
                AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key + ".restore", true);
            try
            {
                File.Copy("Assets/Scenes/BattleScene.unity", Copy);
                AssetDatabase.ImportAsset(Copy);
                var activeScene = SceneManager.GetActiveScene();
                var scene = EditorSceneManager.OpenScene(Copy, OpenSceneMode.Additive);
                try
                {
                    foreach (var socket in scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<WebSocketManager>(true)))
                        socket.gameObject.SetActive(false);
                    foreach (var manager in scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<GameManager>(true)))
                        manager.enableLocalInputTesting = false;
                    Require(EditorSceneManager.SaveScene(scene), "Could not save the isolated validation scene.");
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                    if (activeScene.IsValid() && activeScene.isLoaded)
                        SceneManager.SetActiveScene(activeScene);
                }
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Copy);
                SessionState.SetBool(Key + ".run", true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception error)
            {
                report.AppendLine("FAIL setup: " + error);
                Write();
                RestoreEditor();
                throw;
            }
        }

        static void PreserveDirtyScenes()
        {
            string folder = "GeneratedAssets/CombatExpansion/UnsavedScenes/" +
                DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isDirty)
                    continue;
                Directory.CreateDirectory(folder);
                string path = folder + "/" + i + "_" + scene.name + ".unity";
                Require(EditorSceneManager.SaveScene(scene, path, true),
                    "Could not preserve unsaved scene " + scene.name);
            }
        }

        static void PlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key + ".restore", false))
                return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                ResetFields();
                report.Append(File.ReadAllText(ReportPath));
            }
            else if (state == PlayModeStateChange.ExitingPlayMode && SessionState.GetBool(Key + ".run", false))
                Finish("Play Mode exited before validation completed.");
            else if (state == PlayModeStateChange.EnteredEditMode)
                RestoreEditor();
        }

        static void BeforeReload()
        {
            if (EditorApplication.isPlaying && SessionState.GetBool(Key + ".run", false))
                Finish("Assembly reload interrupted validation.");
        }

        static void Finish(string error)
        {
            SessionState.SetBool(Key + ".run", false);
            activeCase = false;
            try
            {
                RestoreState();
            }
            catch (Exception cleanup)
            {
                error = (error ?? "") + " Cleanup: " + cleanup;
            }
            report.AppendLine(error == null ? $"PASS all {TotalCases} preview damage cases."
                : $"FAIL case={step + 1} action={Action}: {error}");
            Write();
            EditorApplication.isPlaying = false;
        }

        static void RestoreEditor()
        {
            SessionState.SetBool(Key + ".run", false);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                SessionState.GetString(Key + ".previous", ""));
            if (File.Exists(Copy))
                AssetDatabase.DeleteAsset(Copy);
            SessionState.SetBool(Key + ".restore", false);
            SessionState.EraseString(Key + ".previous");
            ResetFields();
        }

        static void ResetFields()
        {
            game = null;
            source = target = null;
            ui = null;
            feedback = null;
            pair = null;
            move = null;
            step = 0;
            lastFrame = -1;
            activeCase = false;
            saved = false;
            began = caseBegan = settledAt = 0;
            failure = null;
            report.Clear();
        }
    }
}
