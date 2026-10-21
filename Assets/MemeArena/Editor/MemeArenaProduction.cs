using System;
using System.IO;
using System.Linq;
using System.Text;
using MemeArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MemeArenaEditor
{
    // Explicitly invoked after the user approves applying the reviewed stage to production.
    public static class MemeArenaProduction
    {
        public const string BattlePath = "Assets/Scenes/BattleScene.unity";
        const string Review = MemeArenaBuild.Root + "/Documentation/Review";

        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Apply the stage from Edit mode.");
            string prefabPath = File.ReadAllText(MemeArenaBuild.Root + "/Documentation/LatestGenerated.txt").Trim();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            MemeArenaValidation.ValidatePrefab(prefab);
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(BattlePath);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!alreadyOpen) scene = EditorSceneManager.OpenScene(BattlePath, OpenSceneMode.Additive);
            try
            {
                var roots = scene.GetRootGameObjects();
                var game = roots.SelectMany(root => root.GetComponentsInChildren<GameManager>(true)).Single();
                var view = roots.SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                    .Single(camera => camera.CompareTag("MainCamera"));
                if (!game.leftCombat || !game.rightCombat || !game.uiManager || !game.battleVfx)
                    throw new InvalidOperationException("Battle scene gameplay references must be valid before integration.");
                string gameplayBefore = GameplaySnapshot(scene);
                string guidBefore = AssetDatabase.AssetPathToGUID(BattlePath);
                Directory.CreateDirectory(Review + "/Backups");
                AssetDatabase.Refresh();
                string backup = AssetDatabase.GenerateUniqueAssetPath(Review + "/Backups/BattleScene_beforeColosseum_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity");
                if (!EditorSceneManager.SaveScene(scene, backup, true))
                    throw new IOException("Could not preserve the current battle scene before applying the stage.");

                SceneManager.SetActiveScene(scene);
                foreach (var root in roots)
                    if (root.name == "BOM Meme Plaza" || root.name == "Directional Light" ||
                        root.name == "Global Volume" || root.name == "Battle Color Grade") root.SetActive(false);

                var stages = roots.SelectMany(root => root.GetComponentsInChildren<MemeArenaStage>(true)).ToArray();
                var existing = stages.FirstOrDefault(stage =>
                    PrefabUtility.GetCorrespondingObjectFromSource(stage.gameObject) == prefab);
                foreach (var stage in stages)
                {
                    stage.gameObject.SetActive(stage == existing);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(stage.gameObject);
                }
                var instance = existing ? existing.gameObject : (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.SetActive(true);
                var adapter = instance.GetComponent<MemeArenaCombatAdapter>();
                adapter.game = game;
                adapter.ui = game.uiManager;
                adapter.battleVfx = game.battleVfx;
                adapter.crowd.view = view;
                adapter.crowd.celebration.view = view;
                PrefabUtility.RecordPrefabInstancePropertyModifications(adapter);
                PrefabUtility.RecordPrefabInstancePropertyModifications(adapter.crowd);
                PrefabUtility.RecordPrefabInstancePropertyModifications(adapter.crowd.celebration);
                var fracture = instance.GetComponent<MemeArenaFloorFracture>();
                if (fracture)
                {
                    fracture.battleVfx = game.battleVfx;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(fracture);
                }
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.38f, .4f, .47f);
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = .008f;
                RenderSettings.fogColor = new Color(.55f, .49f, .43f);
                if (GameplaySnapshot(scene) != gameplayBefore)
                    throw new InvalidOperationException("Gameplay or camera data changed unexpectedly; backup: " + backup);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the production battle scene.");
                if (AssetDatabase.AssetPathToGUID(BattlePath) != guidBefore)
                    throw new InvalidOperationException("The battle scene GUID changed unexpectedly.");
                ValidateScene(scene, prefab);
                var report = new StringBuilder();
                report.AppendLine("PASS applied reviewed Meme Colosseum to " + BattlePath);
                report.AppendLine("Prefab: " + prefabPath);
                report.AppendLine("Backup including any unsaved scene changes: " + backup);
                report.AppendLine("PASS original gameplay components, fighter/camera transforms and scene GUID preserved.");
                report.AppendLine("PASS one active stage; old map and old lighting disabled; adapter references assigned.");
                report.AppendLine("Build settings unchanged; LoadingScene already loads this same BattleScene.");
                File.WriteAllText(Review + "/ProductionIntegration.txt", report.ToString());
                Debug.Log(report.ToString());
                Selection.activeGameObject = instance;
            }
            finally
            {
                if (!alreadyOpen) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        static string GameplaySnapshot(Scene scene)
        {
            var result = new StringBuilder();
            foreach (var component in scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Component>(true)))
            {
                if (!(component is GameManager) && !(component is CharacterCombat) && !(component is Camera) &&
                    !(component is MortalKombatCamera) && !(component is MemeBattleUI) && !(component is RoundManager))
                    continue;
                result.AppendLine(EditorJsonUtility.ToJson(component));
                result.AppendLine(EditorJsonUtility.ToJson(component.transform));
            }
            return result.ToString();
        }

        static void ValidateScene(Scene scene, GameObject prefab)
        {
            var roots = scene.GetRootGameObjects();
            var stage = roots.SelectMany(root => root.GetComponentsInChildren<MemeArenaStage>())
                .Single(value => value.gameObject.activeInHierarchy);
            var adapter = stage.GetComponent<MemeArenaCombatAdapter>();
            if (PrefabUtility.GetCorrespondingObjectFromSource(stage.gameObject) != prefab || !adapter.game ||
                !adapter.ui || !adapter.battleVfx || !adapter.crowd.view || !adapter.crowd.celebration.view)
                throw new InvalidOperationException("The production stage or combat references are invalid.");
            if (stage.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled))
                throw new InvalidOperationException("The stage added an enabled collider.");
            if (roots.Any(root => root.activeSelf && (root.name == "BOM Meme Plaza" ||
                root.name == "Directional Light" || root.name == "Global Volume" || root.name == "Battle Color Grade")))
                throw new InvalidOperationException("The old arena or lighting is still active.");
        }
    }
}
