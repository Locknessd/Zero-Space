using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static partial class GameFlatKitSetup
{
    [MenuItem("Tools/Battle/Validate FlatKit URP integration")]
    public static void Validate()
    {
        var report = new StringBuilder();
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
        try
        {
            var roots = scene.GetRootGameObjects();
            var game = roots.SelectMany(root => root.GetComponentsInChildren<GameManager>(true)).Single();
            foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
            {
                var surfaces = fighter.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material && material.shader.name == "FlatKit/Stylized Surface").Distinct().ToArray();
                if (surfaces.Length == 0) throw new InvalidOperationException("Missing FlatKit fighter: " + fighter.name);
                foreach (var material in surfaces) CheckSurface(material);
                report.AppendLine("PASS " + fighter.name + ": " + surfaces.Length + " FlatKit URP surface materials.");
            }
            var materials = roots.SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
                .SelectMany(renderer => renderer.sharedMaterials).Where(material => material).Distinct().ToArray();
            foreach (var material in materials)
                if (material.shader.name.StartsWith("FlatKit/", StringComparison.Ordinal)) CheckSurface(material);
            report.AppendLine("PASS material shader support; alpha, VFX and stage lightmap shaders remain available.");
            var feedback = roots.SelectMany(root => root.GetComponentsInChildren<BattleImpactFeedback>(true))
                .SingleOrDefault();
            if (!feedback) throw new InvalidOperationException("Battle impact feedback is missing.");
            CheckSurfaceFlash(feedback, game.leftCombat, report);
            CheckSurfaceFlash(feedback, game.rightCombat, report);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
        foreach (string path in new[] { "Assets/PepeShowcase/Characters/Pepe.prefab",
            "Assets/PepeShowcase/Characters/Mankey.prefab" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            int count = 0;
            foreach (var material in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials).Where(material => material).Distinct())
            {
                if (material.shader.name != "FlatKit/Stylized Surface") continue;
                CheckSurface(material);
                count++;
            }
            if (count == 0) throw new InvalidOperationException("FlatKit character prefab is missing: " + path);
            report.AppendLine("PASS showcase prefab: " + path + "; " + count + " native FlatKit materials.");
        }
        Directory.CreateDirectory(Review);
        File.WriteAllText(Review + "/Validation.txt", report.ToString());
    }

    static void CheckSurface(Material material)
    {
        if (!material.shader || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
            throw new InvalidOperationException("Unsupported FlatKit shader: " + material.name);
        if (!material.HasProperty("_BaseMap") ||
            material.GetTag("RenderPipeline", false) != "UniversalPipeline")
            throw new InvalidOperationException("Built-in FlatKit shader entered URP: " + material.name);
        if (material.GetFloat("_TextureImpact") != 1)
            throw new InvalidOperationException("Texture identity was lost: " + material.name);
    }

    static void CheckSurfaceFlash(BattleImpactFeedback feedback, CharacterCombat fighter, StringBuilder report)
    {
        var renderer = fighter.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(item =>
            item.sharedMaterials.Any(material => material && material.shader.name == "FlatKit/Stylized Surface"));
        int index = Array.FindIndex(renderer.sharedMaterials,
            material => material && material.shader.name == "FlatKit/Stylized Surface");
        var before = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(before, index);
        var probe = new MaterialPropertyBlock();
        int marker = Shader.PropertyToID("_FlatKitRestoreProbe");
        probe.SetFloat(marker, 17);
        renderer.SetPropertyBlock(probe, index);
        var type = typeof(BattleImpactFeedback);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        try
        {
            type.GetMethod("HighlightDefender", flags).Invoke(feedback, new object[] { fighter });
            renderer.GetPropertyBlock(probe, index);
            var baseColor = renderer.sharedMaterials[index].GetColor("_BaseColor");
            if (!probe.HasColor("_BaseColor") || probe.GetFloat(marker) != 17 ||
                Vector4.Distance(baseColor, probe.GetColor("_BaseColor")) < .01f)
                throw new InvalidOperationException("Contact highlight stopped working on FlatKit.");
            type.GetMethod("AdvanceSurfaceHighlight", flags).Invoke(feedback, new object[] { 1f });
            renderer.GetPropertyBlock(probe, index);
            if (probe.GetFloat(marker) != 17 || probe.HasColor("_BaseColor"))
                throw new InvalidOperationException("Contact highlight did not restore prior material properties.");
            report.AppendLine("PASS " + fighter.name + ": FlatKit flash, timeout and property-block restoration.");
        }
        finally
        {
            type.GetMethod("ClearSurfaceHighlights", flags).Invoke(feedback, null);
            renderer.SetPropertyBlock(before, index);
        }
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void CaptureGameFlatKitAfter()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            try
            {
                CaptureBattleLook(scene, GameFlatKitSetup.Review + "/BattleAfter.png");
                var game = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<GameManager>(true)).Single();
                var feedback = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<BattleImpactFeedback>(true)).Single();
                typeof(BattleImpactFeedback).GetMethod("HighlightDefender",
                    BindingFlags.NonPublic | BindingFlags.Instance).Invoke(feedback, new object[] { game.rightCombat });
                CaptureBattleLook(scene, GameFlatKitSetup.Review + "/BattleHitFlash.png");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
            GameFlatKitSetup.Validate();
        }
    }
}
