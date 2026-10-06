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
            var light = NewPackContactEffect(false);
            var heavy = NewPackContactEffect(true);
            var dust = BattleEffectPrefab("LandingDust", "Misc/CFXR Smoke Poof Circle Flat.prefab", .3f);
            var slash = BattleEffectPrefab("BladeSlash",
                "Assets/Hovl Studio/Sword slash VFX/Prefabs/Sword Slash 3.prefab", .3f);
            var swing = BattleEffectPrefab("LightSwing", "Texts/CFXR _WHOOSH_.prefab", .16f);
            var thrust = BattleEffectPrefab("ThrustSwing", "Texts/CFXR _WHOOSH_.prefab", .14f);
            var ground = NewPackGroundEffect();
            var text = BattleEffectPrefab("LandingText", "Texts/CFXR _SMASH_.prefab", .2f);
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
                player.lightSwing = swing;
                player.thrustSwing = thrust;
                player.groundImpact = ground;
                player.landingText = text;
                player.effectScale = 1;
                player.groundHeight = 0;
                player.maxInstances = 32;
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
                Directory.CreateDirectory("Temp/FrankRetarget");
                File.WriteAllText("Temp/FrankRetarget/battle-vfx-install.txt",
                    "Installed eight pooled battle effects on GameManager/Mankey/Pepe.\n" +
                    "White Mage light contact/golden floor burst, Archer heavy contact; POW/WHAM/WHOOSH/SMASH retained.\n" +
                    "Effects sample each authored cue pose and restore the displayed animation time.\n");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        static GameObject BattleEffectPrefab(string name, string sourcePath, float scale)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath.StartsWith("Assets/", StringComparison.Ordinal)
                ? sourcePath : CfxPrefabs + sourcePath);
            if (!source) throw new Exception("Missing battle effect: " + sourcePath);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                root.name = name;
                root.transform.localScale = Vector3.one * scale;
                if (name == "BladeSlash")
                {
                    // Hovl's sweep is authored in XZ. Face its full arc towards the battle camera.
                    root.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    foreach (var child in root.GetComponentsInChildren<Transform>(true))
                        if (child.name.IndexOf("distortion", StringComparison.OrdinalIgnoreCase) >= 0)
                            child.gameObject.SetActive(false);
                }
                foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = particles.main;
                    main.playOnAwake = false;
                    main.loop = false;
                    main.stopAction = ParticleSystemStopAction.None;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    // The windup WHOOSH must clear before the next hit in a rapid dagger combo.
                    if (name == "LightSwing" || name == "ThrustSwing") main.simulationSpeed = 2.5f;
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
            // The generated CFXR Ubershader uses incompatible particle helpers in Unity 6.
            // Its regular .shader counterpart renders the landing smoke correctly.
            if (shaderPath.EndsWith("/CFXR Particle Ubershader.cfxrshader", StringComparison.Ordinal))
                shaderPath = Path.ChangeExtension(shaderPath, ".shader");
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
            {
                AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            }
            if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new Exception("Missing or invalid VFX shader for " + original + ": " + shaderPath);
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
            material.DisableKeyword("SOFTPARTICLES_ON");
            if (material.HasProperty("_UseSP")) material.SetFloat("_UseSP", 0);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
