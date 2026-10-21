using System;
using System.IO;
using System.Linq;
using MemeArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MemeArenaEditor
{
    public static class MemeArenaIntegration
    {
        public static string LatestScene => File.Exists(MemeArenaBuild.Root + "/Documentation/LatestBattleScene.txt")
            ? File.ReadAllText(MemeArenaBuild.Root + "/Documentation/LatestBattleScene.txt").Trim() : "";

        public static void CreateBattleVariant()
        {
            var prefab = MemeArenaBuild.Generate();
            var stage = prefab.GetComponent<MemeArenaStage>();
            string path = AssetDatabase.GenerateUniqueAssetPath(stage.generatedAssetFolder + "/MemeColosseum_Battle.unity");
            if (!AssetDatabase.CopyAsset("Assets/Scenes/BattleScene.unity", path))
                throw new IOException("Could not create the battle scene copy.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                foreach (var root in scene.GetRootGameObjects())
                    if (root.name == "BOM Meme Plaza" || root.name == "Directional Light" ||
                        root.name == "Global Volume" || root.name == "Battle Color Grade") root.SetActive(false);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var adapter = instance.GetComponent<MemeArenaCombatAdapter>();
                adapter.game = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<GameManager>())
                    .Single();
                adapter.ui = adapter.game.uiManager;
                adapter.battleVfx = adapter.game.battleVfx;
                var view = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>())
                    .Single(camera => camera.CompareTag("MainCamera"));
                adapter.crowd.view = view;
                adapter.crowd.celebration.view = view;
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.38f, .4f, .47f);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = .008f;
                RenderSettings.fogColor = new Color(.55f, .49f, .43f);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save battle variant.");
                File.WriteAllText(MemeArenaBuild.Root + "/Documentation/LatestBattleScene.txt", path);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            MemeArenaValidation.ValidatePrefab(prefab);
        }
    }
}
