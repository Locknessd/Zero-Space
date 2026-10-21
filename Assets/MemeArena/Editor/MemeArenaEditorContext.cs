using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MemeArenaEditor
{
    public static class MemeArenaEditorContext
    {
        public static void VerifySavedCopy(string beforePath)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Scenes/LoadingScene.unity" || !File.Exists(beforePath)) return;
            string afterPath = AssetDatabase.GenerateUniqueAssetPath(
                "Assets/MemeArena/Documentation/Review/LoadingScene_afterPlay.unity");
            if (!EditorSceneManager.SaveScene(scene, afterPath, true))
                throw new IOException("Could not inspect the preserved editor scene.");
            bool same = File.ReadAllText(beforePath).Replace("\r\n", "\n") ==
                File.ReadAllText(afterPath).Replace("\r\n", "\n");
            if (same) EditorSceneManager.MarkSceneDirty(scene);
            string report = (same ? "PASS" : "REVIEW") + " editor scene compared with pre-Play unsaved copy.\n" +
                "Before: " + beforePath + "\nAfter: " + afterPath + "\n";
            File.WriteAllText("Assets/MemeArena/Documentation/Review/EditorScenePreservation.txt", report);
            Debug.Log(report);
        }
    }
}
