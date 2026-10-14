using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static partial class MemeArenaSetup
{
    static readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();

    static void PrepareAssets()
    {
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Meshes");
        AssetDatabase.Refresh();
        var importer = (TextureImporter)AssetImporter.GetAtPath(Root + "/Textures/MemeGraffitiAtlas.png");
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.None;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 2048;
        importer.textureCompression = TextureImporterCompression.Compressed;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.filterMode = FilterMode.Bilinear;
        importer.SaveAndReimport();
        materials.Clear();
    }

    static Material Surface(string name, Color color, bool unlit = false)
    {
        if (materials.TryGetValue(name, out var found)) return found;
        string path = Root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" :
                "Universal Render Pipeline/Lit")) { name = "Meme Plaza " + name };
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .16f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        materials.Add(name, material);
        return material;
    }

    static Material Poster(int index)
    {
        var material = Surface("Graffiti " + index, new Color(.82f, .82f, .82f), true);
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Textures/MemeGraffitiAtlas.png"));
        material.SetTextureScale("_BaseMap", Vector2.one * .5f);
        material.SetTextureOffset("_BaseMap", new Vector2(index % 2 * .5f, index < 2 ? .5f : 0));
        if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    static Material Sky()
    {
        string path = Root + "/Materials/Meme dusk sky.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find("FlatKit/GradientSkybox"));
            AssetDatabase.CreateAsset(material, path);
        }
        material.SetColor("_Color2", new Color(.15f, .19f, .32f));
        material.SetColor("_Color1", new Color(.6f, .43f, .38f));
        material.SetFloat("_Intensity", .85f);
        material.SetFloat("_Exponent", 1.35f);
        material.SetVector("_Direction", Vector3.up);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
        return material;
    }

    static GameObject Shape(string name, Transform parent, PrimitiveType type, Vector3 position,
        Vector3 scale, Material material, Quaternion? rotation = null, bool collider = false)
    {
        var node = GameObject.CreatePrimitive(type);
        node.name = name;
        node.transform.SetParent(parent, false);
        node.transform.localPosition = position;
        node.transform.localRotation = rotation ?? Quaternion.identity;
        node.transform.localScale = scale;
        var renderer = node.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.receiveShadows = true;
        renderer.shadowCastingMode = type == PrimitiveType.Quad ? ShadowCastingMode.Off : ShadowCastingMode.On;
        if (!collider) Object.DestroyImmediate(node.GetComponent<Collider>());
        node.isStatic = true;
        return node;
    }

    static void Box(string name, Transform parent, Vector3 position, Vector3 size, Material material,
        Quaternion? rotation = null, bool collider = false) =>
        Shape(name, parent, PrimitiveType.Cube, position, size, material, rotation, collider);

    static void Label(string text, Transform parent, Vector3 position, float size, Color color,
        Quaternion? rotation = null)
    {
        var node = new GameObject("Meme sign " + text);
        node.transform.SetParent(parent, false);
        node.transform.localPosition = position;
        node.transform.localRotation = rotation ?? Quaternion.Euler(0, 180, 0);
        var label = node.AddComponent<TextMeshPro>();
        label.font = TMP_Settings.defaultFontAsset;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(12, 2);
        label.enableWordWrapping = false;
        label.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        node.isStatic = true;
    }
}
