using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static partial class GameFlatKitSetup
{
    // Keep the source colors so applying the preset again cannot progressively darken a material.
    const string SourceTintTag = "ZeroSpaceFlatKitSourceTint";
    const string SourceShadeTag = "ZeroSpaceFlatKitSourceShade";
    const float SurfaceGain = .62f;
    const float ShadeGain = .56f;

    static void BalanceSurfaceBrightness(Material material)
    {
        var tint = SourceColor(material, SourceTintTag, "_BaseColor");
        var shade = SourceColor(material, SourceShadeTag, "_ColorDim");
        material.SetColor("_BaseColor", ScaleRgb(tint, SurfaceGain));
        material.SetColor("_ColorDim", ScaleRgb(shade, ShadeGain));
    }

    static Color SourceColor(Material material, string tag, string property)
    {
        string saved = material.GetTag(tag, false, "");
        if (!string.IsNullOrEmpty(saved)) return JsonUtility.FromJson<Color>(saved);
        var color = material.GetColor(property);
        material.SetOverrideTag(tag, JsonUtility.ToJson(color));
        return color;
    }

    static Color ScaleRgb(Color color, float gain)
    {
        return new Color(color.r * gain, color.g * gain, color.b * gain, color.a);
    }

    [MenuItem("Tools/Battle/Apply darker FlatKit tone")]
    public static void ApplyDarkerTone()
    {
        var report = new StringBuilder();
        foreach (string folder in new[] { "Assets/Rendering/BattleURP/Materials",
            "Assets/Materials/BattleFlatKit", "Assets/Materials/BattleWeapons", Root + "/Materials",
            "Assets/Characters/BattleRoster/Materials" })
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
                if (!material || !material.shader || material.shader.name != "FlatKit/Stylized Surface") continue;
                BalanceSurfaceBrightness(material);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                report.AppendLine(AssetDatabase.GetAssetPath(material));
            }
        }
        Directory.CreateDirectory(Review);
        File.WriteAllText(Review + "/DarkerTone.txt", "Surface RGB gain: 0.62; shaded RGB gain: 0.56.\n" +
            "Source colors and alpha retained; repeated application is stable.\n" + report);
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void DimGameFlatKit() => GameFlatKitSetup.ApplyDarkerTone();
    }
}
