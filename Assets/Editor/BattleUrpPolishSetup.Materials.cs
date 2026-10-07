using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class BattleUrpPolishSetup
{
    static readonly Dictionary<Material, Material> AdaptedMaterials = new Dictionary<Material, Material>();

    static void ConfigureMaterials(Scene scene, GameManager game)
    {
        AdaptedMaterials.Clear();
        foreach (var root in scene.GetRootGameObjects())
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is SpriteRenderer || renderer.GetComponentInParent<Canvas>(true))
                continue;
            AdaptRenderer(renderer);
        }
        // These are already project-owned battle variants, not imported pack materials.
        foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials/BattleWeapons" }))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            material.shader = Shader.Find("Battle/URP/Toon Surface");
            material.SetColor("_ShadowTint", new Color(.3f, .39f, .53f));
            material.SetFloat("_SpecularStrength", material.name.Contains("Steel") ? .4f : .15f);
            material.SetFloat("_Smoothness", .7f);
            material.SetFloat("_RimStrength", .09f);
            EditorUtility.SetDirty(material);
        }
        AdaptEffectReferences(game.battleVfx);
    }

    static void AdaptRenderer(Renderer renderer)
    {
        var slots = renderer.sharedMaterials;
        bool changed = false;
        for (int i = 0; i < slots.Length; i++)
        {
            if (!slots[i])
                continue;
            var adapted = AdaptMaterial(slots[i]);
            changed |= adapted != slots[i];
            slots[i] = adapted;
        }
        if (!changed)
            return;
        Undo.RecordObject(renderer, "Apply battle URP materials");
        renderer.sharedMaterials = slots;
        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        EditorUtility.SetDirty(renderer);
    }

    static Material OriginalStageMaterial(Material source)
    {
        if (source.name.StartsWith("Mat_Foliage", StringComparison.Ordinal))
            return AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/BeatEmUp_GameTemplate3D/Materials/Mat_Foliage.mat") ?? source;
        const string prefix = "Environment_";
        string file = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(source));
        if (file.StartsWith(prefix, StringComparison.Ordinal))
        {
            string guid = file.Substring(prefix.Length);
            return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid)) ?? source;
        }
        // Existing project-owned variants retain the descriptive original name.
        const string label = "Environment ";
        int end = source.name.IndexOf(" FlatKit", StringComparison.Ordinal);
        if (!source.name.StartsWith(label, StringComparison.Ordinal) || end < label.Length)
            return source;
        string name = source.name.Substring(label.Length, end - label.Length);
        return AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/BeatEmUp_GameTemplate3D/Materials/" + name + ".mat") ?? source;
    }

    static Material AdaptMaterial(Material source)
    {
        string sourcePath = AssetDatabase.GetAssetPath(source);
        var original = OriginalStageMaterial(source);
        bool stage = original != source;
        bool owned = sourcePath.StartsWith(Folder + "/", StringComparison.Ordinal);
        if (owned && !stage)
            return source;
        if (AdaptedMaterials.TryGetValue(source, out var cached))
            return cached;
        string shaderName = source.shader.name;
        // The existing procedural ink/ribbon shaders are unlit vertex-color passes,
        // supported by URP. Preserve their authored shape and color behavior.
        if (!stage && shaderName.StartsWith("Battle/", StringComparison.Ordinal) &&
            shaderName != "Battle/Muted Lightmapped Backdrop")
            return source;
        if (!stage && shaderName.StartsWith("Universal Render Pipeline", StringComparison.Ordinal))
            return source;
        bool floor = original.name == "Mat_Road";
        bool backdrop = shaderName == "Battle/Muted Lightmapped Backdrop";
        bool foliage = original.name == "Mat_Foliage";
        bool particle = shaderName.Contains("Particles") || shaderName.Contains("Particle") ||
            shaderName.Contains("Additive") || shaderName.StartsWith("Hovl/", StringComparison.Ordinal);
        bool stageLit = floor || stage || foliage;
        string targetName = stageLit ? "Universal Render Pipeline/Lit" :
            backdrop ? "Battle/URP/Lightmapped Backdrop" :
            particle ? "Battle/URP/Particle" : "Battle/URP/Toon Surface";
        var shader = Shader.Find(targetName);
        if (!shader)
            throw new InvalidOperationException("Required shader missing: " + targetName);
        string guid = AssetDatabase.AssetPathToGUID(sourcePath);
        string path = owned ? sourcePath :
            Folder + "/Materials/" + source.name + "_" + guid.Substring(0, 8) + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(source);
            material.name = source.name;
            AssetDatabase.CreateAsset(material, path);
        }
        material.shader = shader;
        material.shaderKeywords = Array.Empty<string>();
        var texture = SavedTexture(original, "_MainTex", out var scale, out var offset);
        var tint = source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
        if (stage || foliage)
            tint = Color.white;
        if (texture)
        {
            string slot = stageLit ? "_BaseMap" : "_MainTex";
            material.SetTexture(slot, texture);
            material.SetTextureScale(slot, scale);
            material.SetTextureOffset(slot, offset);
        }
        if (stageLit)
        {
            material.SetColor("_BaseColor", floor ? new Color(.58f, .66f, .75f) :
                foliage ? new Color(.5f, .6f, .52f) : new Color(.65f, .7f, .76f));
            material.SetFloat("_Smoothness", floor ? .22f : .15f);
            material.SetFloat("_Metallic", floor ? .08f : 0);
            if (foliage)
            {
                material.SetFloat("_AlphaClip", 1);
                material.SetFloat("_Cutoff", .35f);
                material.SetFloat("_Cull", 0);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = 2450;
            }
            var normal = SavedTexture(original, "_BumpMap", out _, out _);
            if (normal)
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", .5f);
                material.EnableKeyword("_NORMALMAP");
            }
        }
        else if (particle)
        {
            var color = source.HasProperty("_TintColor") ? source.GetColor("_TintColor") : tint;
            material.SetColor("_TintColor", color);
            bool additive = shaderName.IndexOf("add", StringComparison.OrdinalIgnoreCase) >= 0;
            material.SetFloat("_DstBlend", additive ? 1 : 10);
            material.SetFloat("_Intensity", 1);
            material.renderQueue = Mathf.Max(3000, source.renderQueue);
        }
        else if (!backdrop)
        {
            tint.a = 1;
            material.SetColor("_Color", tint);
            material.SetColor("_ShadowTint", stage ? new Color(.46f, .52f, .64f) : new Color(.5f, .58f, .69f));
            material.SetFloat("_RimStrength", stage ? .015f : .075f);
            material.SetFloat("_SpecularStrength", stage ? .04f : .035f);
            material.SetFloat("_Ambient", .28f);
            material.SetFloat("_BandSoftness", .2f);
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", tint);
            if (foliage)
            {
                material.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/BeatEmUp_GameTemplate3D/Textures/Foliage.tga"));
                material.SetTexture("_BaseMap", material.GetTexture("_MainTex"));
                material.SetFloat("_Cutoff", .35f);
                material.SetFloat("_Cull", 0);
                material.EnableKeyword("_ALPHATEST_ON");
            }
        }
        EditorUtility.SetDirty(material);
        AdaptedMaterials.Add(source, material);
        return material;
    }

    static Texture SavedTexture(Material source, string name, out Vector2 scale, out Vector2 offset)
    {
        scale = Vector2.one;
        offset = Vector2.zero;
        var saved = new SerializedObject(source).FindProperty("m_SavedProperties.m_TexEnvs");
        for (int i = 0; i < saved.arraySize; i++)
        {
            var entry = saved.GetArrayElementAtIndex(i);
            if (entry.FindPropertyRelative("first").stringValue != name)
                continue;
            var value = entry.FindPropertyRelative("second");
            scale = value.FindPropertyRelative("m_Scale").vector2Value;
            offset = value.FindPropertyRelative("m_Offset").vector2Value;
            return value.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
        }
        return null;
    }

    static void AdaptEffectReferences(BattleVfxPlayer vfx)
    {
        if (!vfx)
            return;
        var serialized = new SerializedObject(vfx);
        var property = serialized.GetIterator();
        var prefabs = new Dictionary<GameObject, GameObject>();
        while (property.Next(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference)
                continue;
            var source = property.objectReferenceValue as GameObject;
            if (!source || !AssetDatabase.Contains(source))
                continue;
            string path = AssetDatabase.GetAssetPath(source);
            if (!path.EndsWith(".prefab", StringComparison.Ordinal) || path.StartsWith(Folder, StringComparison.Ordinal))
                continue;
            if (!prefabs.TryGetValue(source, out var adapted))
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                        AdaptRenderer(renderer);
                    string suffix = AssetDatabase.AssetPathToGUID(path).Substring(0, 8);
                    adapted = PrefabUtility.SaveAsPrefabAsset(root,
                        Folder + "/Effects/" + source.name + "_" + suffix + ".prefab");
                    prefabs.Add(source, adapted);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            property.objectReferenceValue = adapted;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
