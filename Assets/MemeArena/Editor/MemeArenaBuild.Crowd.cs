using System.Collections.Generic;
using System.IO;
using System.Linq;
using MemeArena;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MemeArenaEditor
{
    public static partial class MemeArenaBuild
    {
        static void BuildCrowd(MemeArenaStage stage)
        {
            var crowdSettings = settings.crowdSettings ? Object.Instantiate(settings.crowdSettings) :
                ScriptableObject.CreateInstance<MemeCrowdSettings>();
            if (crowdSettings.spectators.Length == 0)
            {
                crowdSettings.spectators = new[] { "Pepe", "Doge", "Wojak", "Popcat", "Ape" }
                    .Select(name => new MemeCrowdSettings.Spectator { name = name, sprite = PlaceholderSprite(name) })
                    .ToArray();
            }
            AssetDatabase.CreateAsset(crowdSettings, folder + "/CrowdSettings.asset");
            settings.crowdSettings = crowdSettings;
            EditorUtility.SetDirty(settings);
            var sprites = crowdSettings.spectators.Where(value => value.sprite).Select(value => value.sprite).ToArray();
            if (sprites.Length == 0) sprites = new[] { PlaceholderSprite("Ape") };
            var atlas = CrowdAtlas(sprites, out var regions);
            var material = new Material(Shader.Find("MemeArena/Crowd Card"))
                { name = "Meme spectator atlas", enableInstancing = true };
            material.SetTexture("_BaseMap", atlas);
            material.SetColor("_BaseColor", new Color(.9f, .9f, .9f, 1));
            AssetDatabase.CreateAsset(material, folder + "/Materials/MemeCrowdAtlas.mat");
            var random = new System.Random(settings.seed);
            var initialOrder = Enumerable.Range(0, crowdSettings.spectators.Length)
                .OrderBy(index => random.Next()).ToArray();
            int placement = 0;
            var placedNames = new HashSet<string>();
            var manager = stage.crowd.gameObject.AddComponent<MemeCrowdReactionManager>();
            manager.settings = crowdSettings;
            manager.intensity = settings.reactionIntensity;
            stage.crowd.gameObject.AddComponent<MemeCrowdReactions>();
            var spawner = stage.crowd.gameObject.AddComponent<MemeCrowdSpawner>();
            spawner.settings = crowdSettings;
            spawner.seed = settings.seed;
            var animated = new List<GameObject>();
            var groups = new MemeCrowdGroup[3];
            var staticCards = new List<CrowdCard>[3];
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = Group(stage.crowd, ((MemeCrowdSide)i) + " supporters").gameObject
                    .AddComponent<MemeCrowdGroup>();
                groups[i].supporters = (MemeCrowdSide)i;
                staticCards[i] = new List<CrowdCard>();
            }
            var cardMeshes = new Mesh[regions.Length];
            // One additional atlas-backed row replaces the old generic far-crowd silhouette image.
            for (int tier = 0; tier <= settings.seatingTiers; tier++)
            {
                int count = Mathf.RoundToInt((settings.colosseumRadius + tier) * 2.5f * settings.crowdDensity);
                for (int seat = 0; seat < count; seat++)
                {
                    float halfArc = settings.arcDegrees * .5f - 5;
                    float angle = Mathf.Lerp(-halfArc, halfArc, (seat + .5f) / count);
                    float jitter = (float)random.NextDouble();
                    int group = Mathf.Abs(angle) < 12 ? 0 : angle < 0 ? 1 : 2;
                    float totalWeight = crowdSettings.spectators.Sum(value => Mathf.Max(.01f, value.weight));
                    float choice = (float)random.NextDouble() * totalWeight;
                    var selected = crowdSettings.spectators[0];
                    foreach (var candidate in crowdSettings.spectators)
                    {
                        selected = candidate;
                        choice -= Mathf.Max(.01f, candidate.weight);
                        if (choice <= 0) break;
                    }
                    if (placement < initialOrder.Length) selected = crowdSettings.spectators[initialOrder[placement]];
                    placement++;
                    placedNames.Add(selected.name);
                    int type = selected.sprite ? System.Array.IndexOf(sprites, selected.sprite) : 0;
                    type = Mathf.Max(0, type);
                    var position = Arc(settings.colosseumRadius + tier * 1.18f + .42f,
                        .66f + tier * .74f, (angle + (jitter - .5f)) * Mathf.Deg2Rad);
                    if (animated.Count < settings.EffectiveAnimatedCount && tier < 2)
                    {
                        var fan = Group(groups[group].transform, selected.name + " spectator");
                        fan.localPosition = position;
                        fan.localScale = Vector3.one * Mathf.Lerp(.9f, 1.08f, jitter);
                        if (selected.prefab)
                        {
                            var model = (GameObject)PrefabUtility.InstantiatePrefab(selected.prefab);
                            model.transform.SetParent(fan, false);
                            foreach (var collider in model.GetComponentsInChildren<Collider>(true))
                                collider.enabled = false;
                        }
                        else
                        {
                            if (!cardMeshes[type])
                            {
                                cardMeshes[type] = CrowdMesh(new[] { new CrowdCard { center = Vector3.zero,
                                    rect = regions[type], height = crowdSettings.spectatorHeight, seed = type * .713f,
                                    aspect = sprites[type].rect.width / sprites[type].rect.height } });
                                AssetDatabase.CreateAsset(cardMeshes[type], folder + "/Meshes/Fan_" + type + ".asset");
                            }
                            fan.gameObject.AddComponent<MeshFilter>().sharedMesh = cardMeshes[type];
                            var renderer = fan.gameObject.AddComponent<MeshRenderer>();
                            renderer.sharedMaterial = material;
                            renderer.shadowCastingMode = ShadowCastingMode.Off;
                        }
                        var animation = fan.gameObject.AddComponent<MemeCrowdFan>();
                        animation.phase = jitter * Mathf.PI * 2;
                        animation.speed = Mathf.Lerp(2, 3.1f, (float)random.NextDouble());
                        animated.Add(fan.gameObject);
                    }
                    else staticCards[group].Add(new CrowdCard { center = position, rect = regions[type],
                        height = crowdSettings.spectatorHeight * Mathf.Lerp(.9f, 1.08f, jitter), seed = jitter,
                        aspect = sprites[type].rect.width / sprites[type].rect.height });
                }
            }
            for (int i = 0; i < groups.Length; i++)
            {
                var mesh = CrowdMesh(staticCards[i]);
                AssetDatabase.CreateAsset(mesh, folder + "/Meshes/CrowdGroup_" + i + ".asset");
                var batch = Group(groups[i].transform, "GPU animated crowd batch");
                batch.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = batch.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                groups[i].staticBatches = new Renderer[] { renderer };
                groups[i].animatedFans = groups[i].GetComponentsInChildren<MemeCrowdFan>();
            }
            spawner.generatedFans = animated.ToArray();
            File.WriteAllText(folder + "/CrowdPlacement.json", JsonUtility.ToJson(new MemeCatalogImport.PlacementReport
                { names = placedNames.OrderBy(name => name).ToArray() }, true));
        }

        static Sprite PlaceholderSprite(string name)
        {
            string path = Root + "/Art/" + name + ".png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.spritePixelsPerUnit = 256;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        internal static Texture2D ReadSprite(Sprite sprite)
        {
            var rect = sprite.rect;
            var target = RenderTexture.GetTemporary(sprite.texture.width, sprite.texture.height);
            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(sprite.texture, target);
                RenderTexture.active = target;
                var texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
                texture.ReadPixels(rect, 0, 0);
                texture.Apply();
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
