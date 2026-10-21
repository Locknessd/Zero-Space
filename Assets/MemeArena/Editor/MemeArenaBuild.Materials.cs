using UnityEditor;
using UnityEngine;

namespace MemeArenaEditor
{
    public static partial class MemeArenaBuild
    {
        static void MakeMaterials()
        {
            outline = null;
            if (settings.enableOutlines && settings.quality != MemeArena.MemeArenaQuality.WebGL)
            {
                outline = new Material(Shader.Find("MemeArena/Ink Outline")) { name = "Restrained ink outline" };
                outline.SetFloat("_Width", settings.outlineWidth);
                AssetDatabase.CreateAsset(outline, folder + "/Materials/Ink Outline.mat");
            }
            stone = settings.stoneMaterial ? settings.stoneMaterial :
                Material("Honey limestone", new Color(.61f, .49f, .35f), "Stone.png");
            sand = settings.floorMaterial ? settings.floorMaterial :
                Material("Quiet sand floor", new Color(.58f, .46f, .34f), "Sand.png");
            gold = settings.goldMaterial ? settings.goldMaterial :
                Material("Emperor gold", new Color(.91f, .57f, .13f));
            red = settings.bannerMaterial ? settings.bannerMaterial :
                Material("Imperial red", new Color(.51f, .035f, .075f));
            dark = Material("Ink and shadow", new Color(.075f, .065f, .09f));
            cream = Material("Carved ivory", new Color(.78f, .68f, .49f));
            settings.stoneMaterial = stone;
            settings.floorMaterial = sand;
            settings.goldMaterial = gold;
            settings.bannerMaterial = red;
            if (!settings.skyTexture) settings.skyTexture = LoadTexture("Sky.png");
            if (!settings.distantCrowdTexture) settings.distantCrowdTexture = LoadTexture("DistantCrowd.png");
            EditorUtility.SetDirty(settings);
        }

        static Texture2D LoadTexture(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Art/" + name);

        static Material Material(string name, Color color, string texture = null, bool unlit = false)
        {
            var shader = Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "MemeArena/Comic Stone");
            var material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .12f);
            if (texture != null) material.SetTexture("_BaseMap", LoadTexture(texture));
            AssetDatabase.CreateAsset(material, folder + "/Materials/" + name + ".mat");
            return material;
        }

        static void BuildBackdrop(Transform parent)
        {
            var sky = Material("Arena Sky", Color.white, null, true);
            sky.SetTexture("_BaseMap", settings.skyTexture);
            sky.SetFloat("_Cull", 1);
            sky.SetTextureScale("_BaseMap", new Vector2(3, 1));
            var item = Shape(parent, "Scenic sky dome — replaceable placeholder", PrimitiveType.Sphere,
                Vector3.zero, Vector3.one * 120, sky);
            item.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
