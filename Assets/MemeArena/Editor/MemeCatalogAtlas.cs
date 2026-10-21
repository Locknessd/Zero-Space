using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MemeArenaEditor
{
    public static class MemeCatalogAtlas
    {
        sealed class Sheet
        {
            public Texture2D image;
            public RectInt[] cells;
        }

        public static Sprite[] Build(MemeCatalogDefinition definition, MemeArena.MemeCrowdSettings previous,
            string directory)
        {
            var sheets = new Dictionary<int, Sheet>();
            var portraits = new List<Texture2D>();
            try
            {
                foreach (var group in definition.entries.Where(entry => string.IsNullOrEmpty(entry.existingName))
                    .GroupBy(entry => entry.sheet))
                {
                    if (group.Key < 0 || group.Key >= 32) throw new InvalidDataException("Invalid source sheet index.");
                    string path = definition.SheetPath(group.Key);
                    var image = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    image.name = Path.GetFileName(path);
                    var loaded = new Sheet { image = image };
                    sheets.Add(group.Key, loaded);
                    if (!ImageConversion.LoadImage(image, File.ReadAllBytes(path)))
                        throw new InvalidDataException("Could not read source sprite sheet " + path);
                    loaded.cells = MemeCatalogSpriteRegions.Detect(image, group.Select(entry => entry.cell));
                }
                foreach (var entry in definition.entries)
                {
                    Texture2D portrait;
                    if (!string.IsNullOrEmpty(entry.existingName))
                    {
                        var old = previous.spectators.Single(value => value.name == entry.existingName);
                        portrait = MemeArenaBuild.ReadSprite(old.sprite);
                    }
                    else
                    {
                        var sheet = sheets[entry.sheet];
                        var rect = sheet.cells[entry.cell];
                        portrait = new Texture2D(rect.width, rect.height, TextureFormat.RGBA32, false);
                        portrait.SetPixels(sheet.image.GetPixels(rect.x, rect.y, rect.width, rect.height));
                        portrait.Apply();
                    }
                    portraits.Add(LimitSize(portrait));
                }
                return SaveAtlas(definition, portraits.ToArray(), directory);
            }
            finally
            {
                foreach (var portrait in portraits) Object.DestroyImmediate(portrait);
                foreach (var sheet in sheets.Values) Object.DestroyImmediate(sheet.image);
            }
        }

        static Texture2D LimitSize(Texture2D source)
        {
            float ratio = Mathf.Min(1, 192f / Mathf.Max(source.width, source.height));
            if (ratio >= 1) return source;
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * ratio));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * ratio));
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var before = RenderTexture.active;
            var resized = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                resized.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                resized.Apply();
                return resized;
            }
            finally
            {
                RenderTexture.active = before;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(source);
            }
        }

        static Sprite[] SaveAtlas(MemeCatalogDefinition definition, Texture2D[] portraits, string directory)
        {
            var packed = new Texture2D(4, 4, TextureFormat.RGBA32, true);
            var regions = packed.PackTextures(portraits, 4, 2048, false);
            if (regions == null || regions.Length != portraits.Length)
                throw new InvalidOperationException("Could not pack every portrait into the shared crowd atlas.");
            string path = directory + "/SpectatorAtlas.png";
            File.WriteAllBytes(path, packed.EncodeToPNG());
            Object.DestroyImmediate(packed);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var sprites = new Sprite[definition.entries.Length];
            for (int i = 0; i < sprites.Length; i++)
            {
                var uv = regions[i];
                var rect = new Rect(uv.x * atlas.width, uv.y * atlas.height,
                    uv.width * atlas.width, uv.height * atlas.height);
                var sprite = Sprite.Create(atlas, rect, new Vector2(.5f, 0), 128, 0, SpriteMeshType.FullRect);
                sprite.name = definition.entries[i].name;
                AssetDatabase.CreateAsset(sprite, directory + "/Character_" + i.ToString("D3") + ".asset");
                sprites[i] = sprite;
            }
            File.WriteAllText(directory + "/Catalog.json", JsonUtility.ToJson(definition, true));
            return sprites;
        }
    }
}
