using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        sealed class GameplayPairRendering : IDisposable
        {
            const int Width = 480;
            const int Height = 360;
            const int Footer = 28;
            readonly Camera camera;
            readonly CombatExpansionPreviewSkin skin;
            readonly List<(Renderer renderer, bool enabled)> environment =
                new List<(Renderer, bool)>();
            readonly List<(Canvas canvas, bool enabled)> canvases = new List<(Canvas, bool)>();
            readonly GameObject ground;
            readonly Material groundMaterial;

            public GameplayPairRendering(Scene scene, CharacterCombat[] fighters, FrankBattlePairPlayback pair)
            {
                try
                {
                    var roots = fighters.Select(f => f.Animator.gameObject)
                        .Concat(new[] { pair.AttackerActor.gameObject, pair.ReceiverActor.gameObject }).ToArray();
                    foreach (var renderer in scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Renderer>(true)))
                    {
                        if (roots.Any(root => renderer.transform.IsChildOf(root.transform)))
                            continue;
                        environment.Add((renderer, renderer.enabled));
                        renderer.enabled = false;
                    }
                    foreach (var canvas in scene.GetRootGameObjects()
                        .SelectMany(root => root.GetComponentsInChildren<Canvas>(true)))
                    {
                        canvases.Add((canvas, canvas.enabled));
                        canvas.enabled = false;
                    }
                    skin = new CombatExpansionPreviewSkin(fighters.Select(f => f.gameObject)
                        .Concat(new[] { pair.AttackerActor.gameObject, pair.ReceiverActor.gameObject }).ToArray());
                    var root = new GameObject("Samurai runtime pair capture camera");
                    SceneManager.MoveGameObjectToScene(root, scene);
                    camera = root.AddComponent<Camera>();
                    camera.scene = scene;
                    camera.enabled = false;
                    camera.orthographic = true;
                    camera.aspect = (float)Width / Height;
                    camera.nearClipPlane = .01f;
                    camera.allowHDR = false;
                    camera.allowMSAA = false;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.13f, .17f, .22f);
                    ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    SceneManager.MoveGameObjectToScene(ground, scene);
                    Object.DestroyImmediate(ground.GetComponent<Collider>());
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (!shader)
                        throw new InvalidOperationException("Samurai capture requires URP Lit shader.");
                    groundMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    groundMaterial.SetColor("_BaseColor", new Color(.24f, .29f, .34f));
                    ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Write(string path, Bounds bounds, float[] times, Action<float> sample)
            {
                int rows = Mathf.CeilToInt(times.Length / 3f);
                int tileWidth = Width * 2;
                int tileHeight = Height + Footer;
                if (rows * tileHeight > SystemInfo.maxTextureSize)
                    throw new InvalidOperationException("Samurai runtime sheet exceeds texture limits.");
                var target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
                var stamp = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                var sheet = new Texture2D(tileWidth * 3, tileHeight * rows, TextureFormat.RGB24, false);
                var previous = RenderTexture.active;
                try
                {
                    ground.transform.position = new Vector3(bounds.center.x, -.015f, bounds.center.z);
                    ground.transform.localScale = new Vector3(Mathf.Max(1, bounds.size.x / 8), 1,
                        Mathf.Max(1, bounds.size.z / 8));
                    camera.targetTexture = target;
                    var background = new Color32[sheet.width * sheet.height];
                    for (int pixel = 0; pixel < background.Length; pixel++)
                        background[pixel] = new Color32(16, 20, 27, 255);
                    sheet.SetPixels32(background);
                    for (int frame = 0; frame < times.Length; frame++)
                    {
                        sample(times[frame]);
                        skin.Sample();
                        int x = frame % 3 * tileWidth;
                        int y = (rows - 1 - frame / 3) * tileHeight;
                        for (int view = 0; view < 2; view++)
                        {
                            Frame(bounds, view);
                            camera.Render();
                            RenderTexture.active = target;
                            stamp.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                            stamp.Apply();
                            sheet.SetPixels(x + view * Width, y + Footer, Width, Height, stamp.GetPixels());
                        }
                        Label(sheet, x + 12, y + 8, times[frame].ToString("F4", CultureInfo.InvariantCulture));
                    }
                    sheet.Apply();
                    File.WriteAllBytes(path, sheet.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = previous;
                    Object.DestroyImmediate(stamp);
                    Object.DestroyImmediate(sheet);
                    RenderTexture.ReleaseTemporary(target);
                }
            }

            void Frame(Bounds bounds, int view)
            {
                var direction = view == 0 ? new Vector3(0, .16f, -1) : new Vector3(-.75f, .24f, -.8f);
                float distance = Mathf.Max(8, bounds.size.magnitude * 2);
                camera.transform.position = bounds.center + direction.normalized * distance;
                camera.transform.LookAt(bounds.center);
                float extent = .5f;
                for (int corner = 0; corner < 8; corner++)
                {
                    var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var local = camera.transform.InverseTransformPoint(point);
                    extent = Mathf.Max(extent, Mathf.Abs(local.x) / camera.aspect, Mathf.Abs(local.y));
                }
                camera.orthographicSize = extent * 1.08f;
                camera.farClipPlane = distance + bounds.size.magnitude + 10;
            }

            static void Label(Texture2D texture, int x, int y, string value)
            {
                const string digits = "0123456789.";
                string[] glyphs =
                {
                    "111101101101111", "010110010010111", "111001111100111", "111001111001111",
                    "101101111001001", "111100111001111", "111100111101111", "111001001001001",
                    "111101111101111", "111101111001111", "000000000000010"
                };
                foreach (char character in value)
                {
                    int glyph = digits.IndexOf(character);
                    if (glyph >= 0)
                        for (int row = 0; row < 5; row++)
                        for (int column = 0; column < 3; column++)
                            if (glyphs[glyph][row * 3 + column] == '1')
                                for (int dy = 0; dy < 2; dy++)
                                for (int dx = 0; dx < 2; dx++)
                                    texture.SetPixel(x + column * 2 + dx, y + (4 - row) * 2 + dy, Color.white);
                    x += 8;
                }
            }

            public void Dispose()
            {
                skin?.Dispose();
                foreach (var item in environment)
                    if (item.renderer)
                        item.renderer.enabled = item.enabled;
                foreach (var item in canvases)
                    if (item.canvas)
                        item.canvas.enabled = item.enabled;
                if (camera)
                    Object.DestroyImmediate(camera.gameObject);
                if (ground)
                    Object.DestroyImmediate(ground);
                if (groundMaterial)
                    Object.DestroyImmediate(groundMaterial);
            }
        }
    }
}
