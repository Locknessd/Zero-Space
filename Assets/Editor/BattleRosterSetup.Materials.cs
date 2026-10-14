using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static partial class BattleRosterSetup
{
    static void ApplyModelMaterials(GameObject model, string id)
    {
        string folder = "Assets/Models/" + id + "/";
        var json = JObject.Parse(File.ReadAllText(folder + "materials.json"));
        var definitions = (JArray)json["materials"];
        var shader = Shader.Find("FlatKit/Stylized Surface");
        if (!shader || !shader.isSupported)
            throw new InvalidOperationException("FlatKit URP shader is unavailable.");
        var template = AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials/BattleFlatKit" })
            .Select(g => AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(g)))
            .First(m => m && m.shader == shader);
        var materials = new Material[definitions.Count];
        for (int i = 0; i < materials.Length; i++)
        {
            var definition = definitions[i];
            string path = Root + "/Materials/" + id + "_" + i + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(template) { name = id + " " + definition.Value<string>("name") };
                AssetDatabase.CreateAsset(material, path);
            }
            var color = definition["baseColorFactor"].Values<float>().ToArray();
            material.SetOverrideTag("ZeroSpaceFlatKitSourceTint", JsonUtility.ToJson(
                new Color(color[0], color[1], color[2], color[3])));
            material.SetOverrideTag("ZeroSpaceFlatKitSourceShade", JsonUtility.ToJson(new Color(.65f, .7f, .76f)));
            material.SetColor("_BaseColor", new Color(color[0] * .62f, color[1] * .62f, color[2] * .62f, color[3]));
            material.SetColor("_ColorDim", new Color(.36f, .39f, .43f));
            material.SetTexture("_BaseMap", LoadModelTexture(folder, definition.Value<string>("baseColor"), false));
            var normal = LoadModelTexture(folder, definition.Value<string>("normal"), true);
            material.SetTexture("_BumpMap", normal);
            if (normal)
                material.EnableKeyword("_NORMALMAP");
            else
                material.DisableKeyword("_NORMALMAP");
            material.SetFloat("_Cull", 0);
            material.SetColor("_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            materials[i] = material;
        }
        foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skin.sharedMaterials.Length != materials.Length)
                throw new InvalidOperationException("Unexpected material slots: " + id);
            skin.sharedMaterials = materials;
        }
    }

    static Texture2D LoadModelTexture(string folder, string relative, bool normal)
    {
        if (string.IsNullOrEmpty(relative))
            return null;
        string path = folder + relative;
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool changed = importer.textureType != (normal ? TextureImporterType.NormalMap : TextureImporterType.Default) ||
            importer.maxTextureSize > 2048 || importer.isReadable || !importer.mipmapEnabled;
        if (changed)
        {
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.maxTextureSize = Math.Min(2048, importer.maxTextureSize);
            importer.isReadable = false;
            importer.mipmapEnabled = true;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
