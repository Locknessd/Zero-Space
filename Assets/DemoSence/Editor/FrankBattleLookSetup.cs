using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string LookReview = "GeneratedAssets/BattleAudioVisualReview";
        const string LookMaterials = "Assets/Materials/BattleFlatKit";

        public static void SurveyBattleLook()
        {
            Directory.CreateDirectory(LookReview);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            try
            {
                var report = new StringBuilder();
                var renderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true));
                foreach (var role in renderers.GroupBy(r => r.GetComponentInParent<CharacterCombat>(true) ? "Fighter/weapon" : "Environment"))
                {
                    report.AppendLine(role.Key + ": " + role.Count() + " renderers");
                    foreach (var materials in role.SelectMany(r => r.sharedMaterials).Where(m => m).GroupBy(m => m))
                    {
                        var m = materials.Key;
                        report.AppendLine($"  {materials.Count()}x {m.name}; {m.shader.name}; color={(m.HasProperty("_Color") ? m.color.ToString() : "none")}; texture={m.mainTexture}; {AssetDatabase.GetAssetPath(m)}");
                    }
                }
                foreach (var light in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)))
                    report.AppendLine($"Light {light.name}: {light.type}, {light.intensity}, {light.color}, active={light.isActiveAndEnabled}");
                File.WriteAllText(LookReview + "/BeforeLook.txt", report.ToString());
                CaptureBattleLook(scene, LookReview + "/Before.png");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [MenuItem("Tools/Battle/Apply FlatKit battle look")]
        public static void InstallBattleFlatKit()
        {
            if (GraphicsSettings.currentRenderPipeline) throw new Exception("This FlatKit setup requires the project's Built-in render pipeline.");
            var surface = Shader.Find("FlatKit/Stylized Surface");
            var outlined = Shader.Find("FlatKit/Stylized Surface With Outline");
            if (!surface || !outlined || !surface.isSupported || !outlined.isSupported)
                throw new Exception("Import the Built-in FlatKit shaders first.");
            Directory.CreateDirectory(LookMaterials);
            AssetDatabase.Refresh();
            var scene = SceneManager.GetSceneByPath(SfxScene);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(SfxScene, OpenSceneMode.Additive);
            var previousActive = SceneManager.GetActiveScene();
            try
            {
                SceneManager.SetActiveScene(scene);
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var cache = new Dictionary<string, Material>();
                foreach (var renderer in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)))
                {
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                    if (renderer.GetComponentInParent<Canvas>(true)) continue;
                    var fighter = renderer.GetComponentInParent<CharacterCombat>(true);
                    var role = fighter == game.leftCombat ? "Mankey" : fighter == game.rightCombat ? "Pepe" : "Environment";
                    var materials = renderer.sharedMaterials;
                    bool touched = false;
                    for (int i = 0; i < materials.Length; i++)
                    {
                        var source = materials[i];
                        if (!source || source.renderQueue > 2449 || source.shader.name.Contains("Particle")) continue;
                        if (AssetDatabase.GetAssetPath(source).StartsWith(LookMaterials + "/", StringComparison.Ordinal))
                        {
                            if (!fighter)
                            {
                                string file = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source));
                                string originalGuid = file.Substring("Environment_".Length);
                                var original = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(originalGuid));
                                if (original && original.shader.name == "Custom/UnlitLightMapEmissive")
                                { materials[i] = original; touched = true; }
                            }
                            continue;
                        }
                        // This backdrop shader combines authored lightmaps and neon emissive maps.
                        if (!fighter && source.shader.name == "Custom/UnlitLightMapEmissive") continue;
                        // Keep emissive signs and lamps using their authored emission shader.
                        if (!fighter && source.HasProperty("_EmissionColor") && source.GetColor("_EmissionColor").maxColorComponent > .05f) continue;
                        string key = role + ":" + source.GetEntityId();
                        if (!cache.TryGetValue(key, out var material))
                        {
                            string identity = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                            if (string.IsNullOrEmpty(identity)) identity = source.GetEntityId().ToString();
                            string path = LookMaterials + "/" + role + "_" + identity + ".mat";
                            material = AssetDatabase.LoadAssetAtPath<Material>(path);
                            if (!material) { material = new Material(fighter ? outlined : surface); AssetDatabase.CreateAsset(material, path); }
                            material.name = role + " " + source.name + " FlatKit";
                            ConfigureBattleFlatKit(material, source, fighter, role == "Mankey");
                            cache.Add(key, material);
                        }
                        materials[i] = material;
                        touched = true;
                    }
                    if (!touched) continue;
                    Undo.RecordObject(renderer, "Apply FlatKit battle material");
                    renderer.sharedMaterials = materials;
                    EditorUtility.SetDirty(renderer);
                }
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.1f, .12f, .16f);
                foreach (var light in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)).Where(l => l.type == LightType.Directional))
                {
                    Undo.RecordObject(light, "Balance battle lighting");
                    light.color = new Color(1, .92f, .83f);
                    light.intensity = 1;
                    light.shadows = LightShadows.Soft;
                    light.shadowStrength = .7f;
                    EditorUtility.SetDirty(light);
                }
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save FlatKit BattleScene.");
                var styledRenderers = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
                    .Where(r => r.sharedMaterials.Any(m => m && m.shader.name.StartsWith("FlatKit/", StringComparison.Ordinal))).ToArray();
                int styledMaterials = styledRenderers.SelectMany(r => r.sharedMaterials).Where(m => m && m.shader.name.StartsWith("FlatKit/", StringComparison.Ordinal)).Distinct().Count();
                File.WriteAllText(LookReview + "/FlatKitInstall.txt", $"FlatKit Built-in: {styledMaterials} separate battle materials, {styledRenderers.Length} renderer bindings; textured cel shading, thin character/weapon outlines and team rim light. Authored neon/lightmapped building backdrops retained.\n");
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        static void ConfigureBattleFlatKit(Material material, Material source, bool fighter, bool left)
        {
            Color tint = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            material.SetColor("_Color", tint);
            material.SetColor("_ColorDim", tint * (fighter ? new Color(.58f, .66f, .8f, 1) : new Color(.78f, .83f, .93f, 1)));
            material.SetFloat("_CelPrimaryMode", 1);
            material.EnableKeyword("_CELPRIMARYMODE_SINGLE");
            material.EnableKeyword("_TEXTUREBLENDINGMODE_MULTIPLY");
            material.SetFloat("_SelfShadingSize", fighter ? .43f : .38f);
            material.SetFloat("_ShadowEdgeSize", fighter ? .055f : .16f);
            material.SetFloat("_Flatness", fighter ? 1 : .72f);
            material.SetFloat("_LightContribution", .15f);
            material.SetFloat("_UnityShadowMode", 1);
            material.EnableKeyword("_UNITYSHADOWMODE_MULTIPLY");
            material.SetFloat("_UnityShadowPower", fighter ? .4f : .25f);
            material.SetFloat("_TextureImpact", 1);
            foreach (string property in new[] { "_MainTex", "_BumpMap" })
            {
                if (!source.HasProperty(property) || !source.GetTexture(property)) continue;
                material.SetTexture(property, source.GetTexture(property));
                material.SetTextureScale(property, source.GetTextureScale(property));
                material.SetTextureOffset(property, source.GetTextureOffset(property));
            }
            if (fighter)
            {
                ConfigureCleanBattleCharacterMaterial(material, !left);
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        public static void PreviewBattleFlatKit()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            try
            {
                var materials = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
                    .SelectMany(r => r.sharedMaterials).Where(m => m && m.shader.name.StartsWith("FlatKit/", StringComparison.Ordinal)).Distinct().ToArray();
                if (materials.Length == 0) throw new Exception("No FlatKit materials in BattleScene.");
                foreach (var material in materials)
                    if (!material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader)) throw new Exception("Invalid FlatKit shader: " + material.name);
                CaptureBattleLook(scene, LookReview + "/FlatKitAfter.png");
                File.WriteAllText(LookReview + "/FlatKitValidation.txt", $"PASS {materials.Length} FlatKit materials use supported shaders; battle idle preview rendered.\n");
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void CaptureBattleLook(Scene scene, string path)
        {
            var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
            var target = new RenderTexture(1280, 720, 24);
            try
            {
                camera.scene = scene;
                camera.targetTexture = target;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                game.leftCombat.Initialize(); game.rightCombat.Initialize();
                game.roundManager.roundNumberText.text = "Turn 1";
                camera.GetComponent<FrankCinematicCamera>().Apply(0, true);
                Canvas.ForceUpdateCanvases();
                CaptureBattleCamera(camera, path);
            }
            finally { camera.targetTexture = null; Object.DestroyImmediate(target); }
        }
    }
}
