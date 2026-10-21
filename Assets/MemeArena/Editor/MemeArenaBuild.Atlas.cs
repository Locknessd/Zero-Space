using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MemeArenaEditor
{
    public static partial class MemeArenaBuild
    {
        static Texture2D CrowdAtlas(Sprite[] sprites, out Rect[] regions)
        {
            var shared = sprites[0].texture;
            if (sprites.All(sprite => sprite.texture == shared))
            {
                regions = sprites.Select(sprite => new Rect(sprite.rect.x / shared.width,
                    sprite.rect.y / shared.height, sprite.rect.width / shared.width,
                    sprite.rect.height / shared.height)).ToArray();
                return shared;
            }
            var textures = sprites.Select(ReadSprite).ToArray();
            var atlas = new Texture2D(4, 4, TextureFormat.RGBA32, true);
            regions = atlas.PackTextures(textures, 8, 2048);
            string path = folder + "/CrowdAtlas.png";
            File.WriteAllBytes(path, atlas.EncodeToPNG());
            Object.DestroyImmediate(atlas);
            foreach (var texture in textures) Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
