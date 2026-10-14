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

public static partial class GameFlatKitSetup
{
    const string Root = "Assets/Rendering/FlatKitGame";
    internal const string Review = "GeneratedAssets/FlatKitIntegration";
    static readonly Dictionary<Material, Material> Copies = new Dictionary<Material, Material>();

    [MenuItem("Tools/Battle/Apply FlatKit URP shaders")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Apply FlatKit in Edit mode.");
        var shader = Shader.Find("FlatKit/Stylized Surface");
        if (!GraphicsSettings.currentRenderPipeline || !shader || !shader.isSupported ||
            ShaderUtil.ShaderHasError(shader) || shader.FindPropertyIndex("_BaseMap") < 0)
            throw new InvalidOperationException("Import the FlatKit URP shader package first.");
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Review);
        AssetDatabase.Refresh();
        Copies.Clear();
        var report = new StringBuilder();
        foreach (string folder in new[] { "Assets/Rendering/BattleURP/Materials",
            "Assets/Materials/BattleFlatKit", "Assets/Materials/BattleWeapons" })
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                string name = material.shader.name;
                bool legacy = name.StartsWith("FlatKit/", StringComparison.Ordinal);
                bool toon = name == "Battle/URP/Toon Surface";
                bool prop = folder.Contains("BattleURP") && material.name.Contains("Environment") &&
                    material.name.Contains("FlatKit") && name == "Universal Render Pipeline/Lit" &&
                    (!material.HasProperty("_EmissionColor") || material.GetColor("_EmissionColor").maxColorComponent == 0);
                if (!legacy && !toon && !prop) continue;
                Configure(material, shader);
                report.AppendLine("Material: " + AssetDatabase.GetAssetPath(material));
            }
        }
        foreach (string path in new[] { "Assets/PepeShowcase/Characters/Pepe.prefab",
            "Assets/PepeShowcase/Characters/Mankey.prefab" })
        {
            var prefab = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Assign(prefab.GetComponentsInChildren<Renderer>(true), shader);
                PrefabUtility.SaveAsPrefabAsset(prefab, path);
                report.AppendLine("Character prefab: " + path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }
        }
        StyleShowcase(shader, report);
        File.WriteAllText(Review + "/Install.txt", report.ToString());
        Validate();
    }

    static void Assign(IEnumerable<Renderer> renderers, Shader shader)
    {
        foreach (var renderer in renderers)
        {
            if (renderer is ParticleSystemRenderer || renderer is TrailRenderer ||
                renderer is LineRenderer || renderer is SpriteRenderer) continue;
            var materials = renderer.sharedMaterials;
            bool changed = false;
            for (int index = 0; index < materials.Length; index++)
            {
                var source = materials[index];
                if (!source || source.renderQueue >= 3000) continue;
                if (source.shader == shader) continue;
                if (!Copies.TryGetValue(source, out var copy))
                {
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long id);
                    string path = Root + "/Materials/" + guid + "_" + id + ".mat";
                    copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (!copy)
                    {
                        copy = new Material(source) { name = source.name + " FlatKit URP" };
                        AssetDatabase.CreateAsset(copy, path);
                    }
                    Configure(copy, shader);
                    Copies.Add(source, copy);
                }
                materials[index] = copy;
                changed = true;
            }
            if (!changed) continue;
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            EditorUtility.SetDirty(renderer);
        }
    }

    static void StyleShowcase(Shader shader, StringBuilder report)
    {
        const string path = "Assets/Scenes/BOMPepeTrailer.unity";
        var previous = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var showcase = root.GetComponent<ZeroSpace.Showcase.PepeAttackShowcase>();
                if (showcase) Assign(showcase.character.GetComponentsInChildren<Renderer>(true), shader);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (opened && !EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save BOM shader setup.");
            report.AppendLine("BOM character bindings updated; room, camera, VFX and timing retained.");
        }
        finally
        {
            if (opened) EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ApplyGameFlatKitUrp() => GameFlatKitSetup.Apply();
        public static void ValidateGameFlatKitUrp() => GameFlatKitSetup.Validate();

        public static void SnapshotGameFlatKit()
        {
            Directory.CreateDirectory(GameFlatKitSetup.Review);
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            try
            {
                CaptureBattleLook(scene, GameFlatKitSetup.Review + "/BattleBefore.png");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
