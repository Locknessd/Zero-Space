using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FrankRetarget;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZeroSpace.Showcase;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseSetup
    {
        internal const string Root = "Assets/PepeShowcase";
        internal const string ScenePath = "Assets/Scenes/PepeAttackShowcase.unity";
        internal const string Output = "GeneratedAssets/PepeShowcase";

        [MenuItem("Tools/Pepe Showcase/Create attack showcase scene")]
        public static void Build()
        {
            Directory.CreateDirectory(Output);
            Directory.CreateDirectory(Root + "/Characters");
            Directory.CreateDirectory(Root + "/Materials");
            AssetDatabase.Refresh();
            var previous = SceneManager.GetActiveScene();
            var source = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var fighters = source.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<CharacterCombat>(true)).ToArray();
                var pepe = fighters.Single(f => f.name == "Pepe");
                var mankey = fighters.Single(f => f.name == "Mankey");
                var bank = pepe.battleSfx.bank;
                var library = AssetDatabase.LoadAssetAtPath<PepeShowcaseLibrary>(Root + "/ShowcaseLibrary.asset");
                if (!library)
                {
                    library = ScriptableObject.CreateInstance<PepeShowcaseLibrary>();
                    AssetDatabase.CreateAsset(library, Root + "/ShowcaseLibrary.asset");
                }
                library.pepePrefab = MakeCharacter(pepe, scene);
                library.mankeyPrefab = MakeCharacter(mankey, scene);
                ConfigureAttacks(library, pepe, mankey);
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssetIfDirty(library);
                var stage = new GameObject("Pepe Attack Showcase");
                var showcase = stage.AddComponent<PepeAttackShowcase>();
                showcase.library = library;
                var body = (GameObject)PrefabUtility.InstantiatePrefab(library.pepePrefab, scene);
                showcase.character = body.GetComponentInChildren<Animator>(true);
                showcase.presentation = stage.AddComponent<PepeShowcasePresentation>();
                showcase.presentation.muzzlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/DemoSence/InsaneCombos/GunMuzzleFlash.prefab");
                BuildStage(scene, showcase);
                BuildTitles(showcase);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new IOException("Could not save the new showcase scene.");
                File.WriteAllText(Output + "/Setup.txt",
                    "One visible character, default Pepe; Mankey and custom Humanoid replacement supported.\n" +
                    "Five complete attacker animations; no receiver, hit reaction, battle UI or combat manager.\n" +
                    "Reference: https://www.youtube.com/watch?v=e-hIZrAqgWc\n" +
                    "Pale modular room, checker floor, cyan fill, low moving camera, clean showcase titles.\n" +
                    "BattleScene and original animation/audio assets are preserved.\n");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                EditorSceneManager.ClosePreviewScene(source);
                SceneManager.SetActiveScene(previous);
            }
            InstallWeaponVfx();
        }

        static GameObject MakeCharacter(CharacterCombat source, Scene scene)
        {
            var clone = Object.Instantiate(source.gameObject);
            SceneManager.MoveGameObjectToScene(clone, scene);
            clone.name = source.name;
            foreach (var manager in clone.GetComponentsInChildren<TrumpWeaponManager>(true))
                foreach (var field in typeof(TrumpWeaponManager).GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (field.FieldType == typeof(GameObject) && field.Name.EndsWith("Set"))
                    {
                        var weapon = (GameObject)field.GetValue(manager);
                        if (weapon && weapon != clone && weapon.transform.IsChildOf(clone.transform))
                            Object.DestroyImmediate(weapon);
                    }
            foreach (var component in clone.GetComponentsInChildren<Component>(true))
                if (component && !(component is Transform) && !(component is Animator) &&
                    !(component is Renderer) && !(component is MeshFilter))
                    Object.DestroyImmediate(component);
            var animator = clone.GetComponentInChildren<Animator>(true);
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
            animator.enabled = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = true;
                if (renderer is SkinnedMeshRenderer skin) skin.updateWhenOffscreen = true;
            }
            var result = PrefabUtility.SaveAsPrefabAsset(clone, Root + "/Characters/" + source.name + ".prefab");
            Object.DestroyImmediate(clone);
            return result;
        }
    }

    public static partial class FrankRetargetBuilder
    {
        public static void CreatePepeAttackShowcase() => PepeShowcaseSetup.Build();
        public static void ValidatePepeAttackShowcase() => PepeShowcaseCapture.Validate();
        public static void RecordPepeAttackShowcase() => PepeShowcaseCapture.Begin();
        public static void CheckPepeShowcaseInPlayMode() => PepeShowcasePlayCheck.Begin();
        public static void InstallPepeShowcaseWeaponVfx() => PepeShowcaseSetup.InstallWeaponVfx();
        public static void ValidatePepeShowcaseWeaponVfx() => PepeShowcaseCapture.ValidateWeaponVfx();
    }
}
