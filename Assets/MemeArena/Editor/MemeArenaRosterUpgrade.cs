using System;
using System.IO;
using System.Linq;
using MemeArena;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MemeArenaEditor
{
    public static class MemeArenaRosterUpgrade
    {
        const string AtlasPath = "Assets/MemeArena/Art/MemeCrowd2026.png";
        static readonly string[] Names = { "Pepe", "Doge", "Cheems", "Popcat", "Chill Guy", "Capybara",
            "Pibble", "Maxwell", "Tung Tung Tung Sahur", "Tralalero Tralala", "Ballerina Cappuccina",
            "Bombardiro Crocodilo", "Chimpanzini Bananini", "Wojak", "Smurf Cat", "Big Floppa" };

        // Art has organic spacing. These measured source-pixel rectangles preserve ears, hats and paws.
        // Coordinates use the image's top-left; Sprite.Create converts them to Unity's bottom-left.
        static readonly Rect[] SourceRects =
        {
            new Rect(15, 26, 294, 277), new Rect(335, 13, 283, 290),
            new Rect(632, 45, 289, 258), new Rect(944, 15, 284, 288),
            new Rect(41, 301, 252, 315), new Rect(321, 332, 301, 287),
            new Rect(644, 319, 301, 297), new Rect(960, 325, 275, 292),
            new Rect(32, 609, 273, 291), new Rect(315, 630, 306, 280),
            new Rect(644, 614, 285, 293), new Rect(915, 634, 333, 247),
            new Rect(13, 901, 321, 331), new Rect(338, 905, 294, 326),
            new Rect(644, 908, 285, 322), new Rect(956, 884, 286, 348)
        };

        public static void UpgradeBattle()
        {
            var previousPath = File.ReadAllText(MemeArenaBuild.Root + "/Documentation/LatestGenerated.txt").Trim();
            var previous = AssetDatabase.LoadAssetAtPath<GameObject>(previousPath).GetComponent<MemeArenaStage>();
            var importer = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
            string directory = AssetDatabase.GenerateUniqueAssetPath(MemeArenaBuild.Generated +
                "/MemeRoster_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(directory);
            AssetDatabase.Refresh();
            var roster = Object.Instantiate(previous.settings.crowdSettings);
            roster.name = "Recognizable meme spectators";
            roster.spectators = Names.Select((name, index) =>
            {
                var source = SourceRects[index];
                var rect = new Rect(source.x * atlas.width / 1254f,
                    (1254 - source.yMax) * atlas.height / 1254f,
                    source.width * atlas.width / 1254f, source.height * atlas.height / 1254f);
                var sprite = Sprite.Create(atlas, rect, new Vector2(.5f, 0), 256, 0, SpriteMeshType.FullRect);
                sprite.name = name;
                AssetDatabase.CreateAsset(sprite, directory + "/Spectator_" + index.ToString("D2") + ".asset");
                return new MemeCrowdSettings.Spectator { name = name, sprite = sprite, weight = 1 };
            }).ToArray();
            AssetDatabase.CreateAsset(roster, directory + "/MemeCrowdSettings.asset");
            var settings = Object.Instantiate(previous.settings);
            try
            {
                settings.crowdSettings = roster;
                settings.enableFloorFracture = true;
                settings.enableVfx = true;
                MemeArenaValidation.ValidatePrefab(MemeArenaBuild.Generate(settings));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
            MemeArenaProduction.Apply();
        }
    }
}
