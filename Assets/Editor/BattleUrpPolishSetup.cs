using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static partial class BattleUrpPolishSetup
{
    const string Folder = "Assets/Rendering/BattleURP";
    const string ScenePath = "Assets/Scenes/BattleScene.unity";

    [MenuItem("Tools/Battle/Presentation/Apply URP art direction")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Apply authored changes outside Play Mode.");
        Directory.CreateDirectory(Folder + "/Materials");
        Directory.CreateDirectory(Folder + "/Effects");
        AssetDatabase.Refresh();
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
        var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true))
            .Single(c => c.CompareTag("MainCamera"));
        ConfigurePipeline();
        ConfigureMaterials(scene, game);
        ConfigureLighting(scene, game, camera);
        ConfigureVolume(scene, camera);
        ConfigureFeedback(scene, game, camera);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new IOException("BattleScene could not be saved.");
        File.WriteAllText(BattlePresentationWorkbench.Review + "/UrpInstall.txt",
            "Saved URP renderer, two quality assets, Volume, restored stage textures, character materials and BattleScene.\n");
    }

    static void ConfigurePipeline()
    {
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Folder + "/BattleRenderer.asset");
        if (!renderer)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(renderer, Folder + "/BattleRenderer.asset");
        }
        renderer.renderingMode = RenderingMode.Forward;
        var high = Pipeline("BattleHigh", renderer, 1, 4, 2048);
        var reduced = Pipeline("BattleReduced", renderer, .85f, 2, 1024);
        GraphicsSettings.defaultRenderPipeline = high;
        var quality = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0]);
        var tiers = quality.FindProperty("m_QualitySettings");
        for (int i = 0; i < tiers.arraySize; i++)
        {
            var tier = tiers.GetArrayElementAtIndex(i);
            tier.FindPropertyRelative("customRenderPipeline").objectReferenceValue = i == 0 ? reduced : high;
        }
        quality.ApplyModifiedPropertiesWithoutUndo();
        QualitySettings.renderPipeline = high;
        EditorUtility.SetDirty(renderer);
    }

    static UniversalRenderPipelineAsset Pipeline(string name, UniversalRendererData renderer,
        float scale, int samples, int shadows)
    {
        string path = Folder + "/" + name + ".asset";
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
        if (!asset)
        {
            asset = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(asset, path);
        }
        asset.renderScale = scale;
        asset.msaaSampleCount = samples;
        asset.supportsHDR = true;
        asset.supportsCameraDepthTexture = true;
        asset.supportsCameraOpaqueTexture = false;
        asset.mainLightShadowmapResolution = shadows;
        asset.maxAdditionalLightsCount = 4;
        var settings = new SerializedObject(asset);
        settings.FindProperty("m_MainLightRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
        settings.FindProperty("m_MainLightShadowsSupported").boolValue = true;
        settings.FindProperty("m_AdditionalLightsRenderingMode").intValue = (int)LightRenderingMode.PerPixel;
        settings.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
        settings.FindProperty("m_SoftShadowsSupported").boolValue = true;
        settings.ApplyModifiedPropertiesWithoutUndo();
        asset.shadowDistance = 32;
        asset.shadowCascadeCount = 2;
        asset.useSRPBatcher = true;
        EditorUtility.SetDirty(asset);
        return asset;
    }

    static void ConfigureLighting(Scene scene, GameManager game, Camera camera)
    {
        var rig = game.GetComponent<BattleLightingRig>();
        if (rig)
        {
            if (rig.key)
            {
                rig.key.color = new Color(1, .88f, .72f);
                rig.key.intensity = 1.05f;
                rig.key.transform.rotation = Quaternion.Euler(44, -32, 0);
                rig.key.shadows = LightShadows.Soft;
                rig.key.shadowBias = .035f;
                rig.key.shadowNormalBias = .12f;
                EditorUtility.SetDirty(rig.key);
            }
            if (rig.fill)
            {
                rig.fill.color = new Color(.48f, .68f, 1);
                rig.fill.intensity = .3f;
                rig.fill.shadows = LightShadows.None;
                EditorUtility.SetDirty(rig.fill);
            }
            if (rig.rim)
            {
                rig.rim.color = new Color(.58f, .75f, 1);
                rig.rim.intensity = .4f;
                rig.rim.shadows = LightShadows.None;
                EditorUtility.SetDirty(rig.rim);
            }
            rig.combatDimming = false;
            rig.impactIntensity = .65f;
            EditorUtility.SetDirty(rig);
        }
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.35f, .43f, .58f);
        RenderSettings.ambientEquatorColor = new Color(.24f, .29f, .38f);
        RenderSettings.ambientGroundColor = new Color(.14f, .12f, .16f);
        RenderSettings.fog = false;
        camera.allowHDR = true;
        camera.allowMSAA = true;
        camera.backgroundColor = new Color(.075f, .105f, .16f);
        EditorUtility.SetDirty(camera);
    }

    static void ConfigureVolume(Scene scene, Camera camera)
    {
        string path = Folder + "/BattleGrade.asset";
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (!profile)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        if (!profile.TryGet<Bloom>(out var bloom))
            bloom = profile.Add<Bloom>();
        bloom.threshold.Override(1.2f);
        bloom.intensity.Override(.12f);
        bloom.scatter.Override(.35f);
        if (!profile.TryGet<ColorAdjustments>(out var grade))
            grade = profile.Add<ColorAdjustments>();
        grade.postExposure.Override(0);
        grade.contrast.Override(5);
        grade.saturation.Override(-4);
        if (!profile.TryGet<Tonemapping>(out var tone))
            tone = profile.Add<Tonemapping>();
        tone.mode.Override(TonemappingMode.Neutral);
        foreach (var component in profile.components)
        {
            if (!AssetDatabase.Contains(component))
                AssetDatabase.AddObjectToAsset(component, profile);
            EditorUtility.SetDirty(component);
        }
        var volume = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Volume>(true))
            .FirstOrDefault(v => v.name == "Battle Color Grade");
        if (!volume)
        {
            var root = new GameObject("Battle Color Grade");
            SceneManager.MoveGameObjectToScene(root, scene);
            volume = root.AddComponent<Volume>();
        }
        volume.isGlobal = true;
        volume.priority = 10;
        volume.sharedProfile = profile;
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.None;
        EditorUtility.SetDirty(data);
        EditorUtility.SetDirty(volume);
        EditorUtility.SetDirty(profile);
    }
}
