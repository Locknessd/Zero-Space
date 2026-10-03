using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CartoonFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string VfxFolder = "Assets/Vfx/Battle";
        const string CfxPrefabs = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/";

        [MenuItem("Tools/Battle/Install battle VFX")]
        public static void InstallBattleVfx()
        {
            Directory.CreateDirectory(VfxFolder);
            Directory.CreateDirectory(VfxFolder + "/Materials");
            AssetDatabase.Refresh();
            var light = BattleEffectPrefab("LightHit", "Impacts/CFXR Hit C 3D.prefab", .28f);
            var heavy = BattleEffectPrefab("HeavyHit", "Impacts/CFXR Hit D 3D (Yellow).prefab", .24f);
            var dust = BattleEffectPrefab("LandingDust", "Misc/CFXR Smoke Poof Circle Flat.prefab", .3f);
            var slash = BattleEffectPrefab("BladeSlash", "Impacts/CFXR Slash (Blue).prefab", .4f);
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene("Assets/Scenes/BattleScene.unity", OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                if (!game.battleSfx || !game.battleSfx.bank) throw new Exception("Battle cue timeline is missing.");
                var player = game.GetComponent<BattleVfxPlayer>();
                if (!player) player = Undo.AddComponent<BattleVfxPlayer>(game.gameObject);
                Undo.RecordObjects(new Object[] { game, player, game.leftCombat, game.rightCombat }, "Install battle VFX");
                player.timeline = game.battleSfx.bank;
                player.lightHit = light;
                player.heavyHit = heavy;
                player.landingDust = dust;
                player.bladeSlash = slash;
                player.effectScale = 1;
                player.groundHeight = 0;
                game.battleVfx = player;
                foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                {
                    fighter.battleVfx = player;
                    if (fighter.hitEffect)
                    {
                        // Retain the original fallback, but suppress its child systems at scene startup.
                        Undo.RecordObject(fighter.hitEffect.gameObject, "Disable legacy startup burst");
                        fighter.hitEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        fighter.hitEffect.gameObject.SetActive(false);
                    }
                    EditorUtility.SetDirty(fighter);
                }
                EditorUtility.SetDirty(game);
                EditorUtility.SetDirty(player);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save BattleScene.");
                AssetDatabase.SaveAssets();
                File.WriteAllText("Temp/FrankRetarget/battle-vfx-install.txt",
                    "Installed four battle prefab variants and shared VFX bindings on GameManager/Mankey/Pepe.\n" +
                    "Hit/slash/landing effects use the existing source-pair cue times. Legacy startup bursts disabled.\n");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        static GameObject BattleEffectPrefab(string name, string sourcePath, float scale)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(CfxPrefabs + sourcePath);
            if (!source) throw new Exception("Missing battle effect: " + sourcePath);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                root.name = name;
                root.transform.localScale = Vector3.one * scale;
                foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = particles.main;
                    main.playOnAwake = false;
                    main.loop = false;
                    main.stopAction = ParticleSystemStopAction.None;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                }
                foreach (var effect in root.GetComponentsInChildren<CFXR_Effect>(true))
                {
                    effect.clearBehavior = CFXR_Effect.ClearBehavior.None;
                    if (effect.cameraShake != null) effect.cameraShake.enabled = false;
                    effect.animatedLights = Array.Empty<CFXR_Effect.AnimatedLight>();
                }
                foreach (var light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
                foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(BattleEffectMaterial).ToArray();
                return PrefabUtility.SaveAsPrefabAsset(root, VfxFolder + "/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Material BattleEffectMaterial(Material source)
        {
            // ParticleSystemRenderer can expose an unused, empty trail-material slot.
            if (!source) return null;
            string original = AssetDatabase.GetAssetPath(source);
            string shaderGuid = Regex.Match(File.ReadAllText(original), @"m_Shader:.*?guid: ([a-f0-9]+)").Groups[1].Value;
            string shaderPath = AssetDatabase.GUIDToAssetPath(shaderGuid);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (!shader || !shader.isSupported)
            {
                AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            }
            if (!shader) throw new Exception("Missing imported VFX shader for " + original);
            string path = VfxFolder + "/Materials/" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(source);
                AssetDatabase.CreateAsset(material, path);
            }
            else material.CopyPropertiesFromMaterial(source);
            material.shader = shader;
            // The small combat bursts need to remain visible without a camera depth texture.
            material.DisableKeyword("_FADING_ON");
            if (material.HasProperty("_UseSP")) material.SetFloat("_UseSP", 0);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
