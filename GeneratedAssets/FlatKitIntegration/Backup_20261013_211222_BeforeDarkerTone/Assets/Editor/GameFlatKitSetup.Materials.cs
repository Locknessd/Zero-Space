using UnityEditor;
using UnityEngine;

public static partial class GameFlatKitSetup
{
    static void Configure(Material material, Shader shader)
    {
        var saved = new SerializedObject(material);
        var texture = SavedTexture(saved, "_MainTex", out var scale, out var offset);
        if (!texture) texture = SavedTexture(saved, "_BaseMap", out scale, out offset);
        var normal = SavedTexture(saved, "_BumpMap", out _, out _);
        var emissionMap = SavedTexture(saved, "_EmissionMap", out _, out _);
        var tint = SavedColor(saved, "_Color", SavedColor(saved, "_BaseColor", Color.white));
        var shadow = SavedColor(saved, "_ColorDim", new Color(.65f, .7f, .76f));
        var emission = SavedColor(saved, "_EmissionColor", Color.black);
        float cull = material.HasProperty("_Cull") ? material.GetFloat("_Cull") : 2;
        bool alpha = material.HasProperty("_AlphaClip") && material.GetFloat("_AlphaClip") > .5f;
        float cutoff = material.HasProperty("_Cutoff") ? material.GetFloat("_Cutoff") : .5f;
        material.shader = shader;
        material.shaderKeywords = System.Array.Empty<string>();
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", scale);
        material.SetTextureOffset("_BaseMap", offset);
        material.SetColor("_BaseColor", tint);
        material.SetColor("_ColorDim", shadow);
        material.SetFloat("_CelPrimaryMode", 1);
        material.EnableKeyword("_CELPRIMARYMODE_SINGLE");
        material.SetFloat("_SelfShadingSize", .32f);
        material.SetFloat("_ShadowEdgeSize", .12f);
        material.SetFloat("_Flatness", .95f);
        material.SetFloat("_TextureImpact", 1);
        material.SetFloat("_TextureBlendingMode", 0);
        material.EnableKeyword("_TEXTUREBLENDINGMODE_MULTIPLY");
        material.SetFloat("_LightContribution", .3f);
        material.SetFloat("_LightFalloffSize", .35f);
        material.SetFloat("_UnityShadowMode", 1);
        material.SetFloat("_UnityShadowPower", .22f);
        material.SetFloat("_UnityShadowSharpness", 1);
        material.EnableKeyword("_UNITYSHADOWMODE_MULTIPLY");
        material.SetFloat("_OutlineEnabled", 0);
        material.SetFloat("_RimEnabled", 0);
        material.SetFloat("_SpecularEnabled", 0);
        material.SetFloat("_GradientEnabled", 0);
        material.SetFloat("_VertexColorsEnabled", 0);
        material.SetFloat("_Surface", 0);
        material.SetFloat("_Blend", 0);
        material.SetFloat("_SrcBlend", 1);
        material.SetFloat("_DstBlend", 0);
        material.SetFloat("_ZWrite", 1);
        material.SetFloat("_Cull", cull);
        material.SetFloat("_AlphaClip", alpha ? 1 : 0);
        material.SetFloat("_Cutoff", cutoff);
        if (alpha) material.EnableKeyword("_ALPHATEST_ON");
        material.SetTexture("_BumpMap", normal);
        if (normal) material.EnableKeyword("_NORMALMAP");
        material.SetTexture("_EmissionMap", emissionMap);
        material.SetColor("_EmissionColor", emission);
        if (emission.maxColorComponent > 0) material.EnableKeyword("_EMISSION");
        material.enableInstancing = true;
        material.renderQueue = alpha ? 2450 : 2000;
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssetIfDirty(material);
    }

    static Texture SavedTexture(SerializedObject material, string name, out Vector2 scale, out Vector2 offset)
    {
        scale = Vector2.one;
        offset = Vector2.zero;
        var textures = material.FindProperty("m_SavedProperties.m_TexEnvs");
        for (int index = 0; index < textures.arraySize; index++)
        {
            var entry = textures.GetArrayElementAtIndex(index);
            if (entry.FindPropertyRelative("first").stringValue != name) continue;
            var value = entry.FindPropertyRelative("second");
            scale = value.FindPropertyRelative("m_Scale").vector2Value;
            offset = value.FindPropertyRelative("m_Offset").vector2Value;
            return value.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
        }
        return null;
    }

    static Color SavedColor(SerializedObject material, string name, Color fallback)
    {
        var colors = material.FindProperty("m_SavedProperties.m_Colors");
        for (int index = 0; index < colors.arraySize; index++)
        {
            var entry = colors.GetArrayElementAtIndex(index);
            if (entry.FindPropertyRelative("first").stringValue == name)
                return entry.FindPropertyRelative("second").colorValue;
        }
        return fallback;
    }
}
