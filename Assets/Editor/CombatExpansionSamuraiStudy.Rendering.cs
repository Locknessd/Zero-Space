using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        sealed class PairRendering : IDisposable
        {
            const int Width = 480;
            const int Height = 360;
            const int Footer = 34;
            readonly Camera camera;
            readonly GameObject ground;
            readonly Material groundMaterial;
            readonly CombatExpansionPreviewSkin skin;
            public readonly Renderer[] Renderers;

            public PairRendering(Scene scene, Actor[] actors)
            {
                try
                {
                    var roots = actors.SelectMany(a => new[] { a.Character.gameObject, a.Driver.gameObject }).ToArray();
                    skin = new CombatExpansionPreviewSkin(roots);
                    Renderers = scene.GetRootGameObjects()
                        .SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
                    var cameraObject = new GameObject("Samurai source camera");
                    SceneManager.MoveGameObjectToScene(cameraObject, scene);
                    camera = cameraObject.AddComponent<Camera>();
                    camera.scene = scene;
                    camera.enabled = false;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.13f, .17f, .22f);
                    camera.orthographic = true;
                    camera.aspect = (float)Width / Height;
                    camera.nearClipPlane = .01f;
                    camera.farClipPlane = 500;
                    camera.allowHDR = false;
                    camera.allowMSAA = false;
                    foreach (bool key in new[] { true, false })
                    {
                        var lamp = new GameObject(key ? "Samurai key light" : "Samurai fill light");
                        SceneManager.MoveGameObjectToScene(lamp, scene);
                        var light = lamp.AddComponent<Light>();
                        light.type = LightType.Directional;
                        light.intensity = key ? 1.25f : .8f;
                        lamp.transform.rotation = Quaternion.Euler(key ? 40 : 25, key ? -35 : 145, 0);
                    }
                    ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    SceneManager.MoveGameObjectToScene(ground, scene);
                    Object.DestroyImmediate(ground.GetComponent<Collider>());
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (!shader)
                        throw new InvalidOperationException("Samurai source capture requires URP Lit shader");
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

            public void Sample() => skin.Sample();

            public void WriteSheet(string path, Actor[] actors, CaptureRecord record, Bounds bounds)
            {
                int frames = Mathf.Max(24, Mathf.CeilToInt(record.durationSeconds * 8) + 1);
                record.sheetSeconds = Enumerable.Range(0, frames)
                    .Select(i => record.durationSeconds * i / (frames - 1))
                    .Concat(actors.Select(a => a.Source.durationSeconds)).Distinct().OrderBy(t => t).ToArray();
                int rows = Mathf.CeilToInt(record.sheetSeconds.Length / 3f);
                int tileWidth = Width * 2;
                int tileHeight = Height + Footer;
                if (rows * tileHeight > SystemInfo.maxTextureSize || tileWidth * 3 > SystemInfo.maxTextureSize)
                    throw new InvalidOperationException("Samurai source sheet exceeds GPU texture size: " + path);
                ground.transform.position = new Vector3(bounds.center.x, -.015f, bounds.center.z);
                ground.transform.localScale = new Vector3(Mathf.Max(1, bounds.size.x / 8), 1,
                    Mathf.Max(1, bounds.size.z / 8));
                var target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
                Texture2D stamp = null;
                Texture2D sheet = null;
                var previous = RenderTexture.active;
                try
                {
                    stamp = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                    sheet = new Texture2D(tileWidth * 3, tileHeight * rows, TextureFormat.RGB24, false);
                    var background = new Color32[sheet.width * sheet.height];
                    for (int index = 0; index < background.Length; index++)
                        background[index] = new Color32(16, 20, 27, 255);
                    sheet.SetPixels32(background);
                    camera.targetTexture = target;
                    foreach (var actor in actors)
                        actor.Reset();
                    for (int frame = 0; frame < record.sheetSeconds.Length; frame++)
                    {
                        float seconds = record.sheetSeconds[frame];
                        foreach (var actor in actors)
                            actor.Evaluate(seconds);
                        Sample();
                        int x = frame % 3 * tileWidth;
                        int y = (rows - 1 - frame / 3) * tileHeight;
                        for (int view = 0; view < 2; view++)
                        {
                            Frame(bounds, view);
                            camera.Render();
                            RenderTexture.active = target;
                            stamp.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                            foreach (var actor in actors)
                            {
                                var head = actor.Character.GetBoneTransform(HumanBodyBones.Head);
                                var label = camera.WorldToViewportPoint(head.position + Vector3.up * .16f);
                                DrawText(stamp, Mathf.Clamp((int)(label.x * Width) - 42, 0, Width - 90),
                                    Mathf.Clamp((int)(label.y * Height), 0, Height - 16), actor.Source.role);
                            }
                            stamp.Apply();
                            sheet.SetPixels(x + view * Width, y + Footer, Width, Height, stamp.GetPixels());
                        }
                        DrawText(sheet, x + 12, y + 10, (frame + 1).ToString("D2") + "  " +
                            seconds.ToString("F4", CultureInfo.InvariantCulture));
                    }
                    sheet.Apply();
                    File.WriteAllBytes(path, sheet.EncodeToPNG());
                }
                finally
                {
                    camera.targetTexture = null;
                    RenderTexture.active = previous;
                    if (stamp)
                        Object.DestroyImmediate(stamp);
                    if (sheet)
                        Object.DestroyImmediate(sheet);
                    RenderTexture.ReleaseTemporary(target);
                }
            }

            void Frame(Bounds bounds, int view)
            {
                Vector3 direction = view == 0 ? new Vector3(0, .16f, -1) : new Vector3(-.75f, .24f, -.8f);
                float distance = Mathf.Max(8, bounds.size.magnitude * 2);
                camera.transform.position = bounds.center + direction.normalized * distance;
                camera.transform.LookAt(bounds.center);
                float extent = .5f;
                for (int corner = 0; corner < 8; corner++)
                {
                    var world = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var local = camera.transform.InverseTransformPoint(world);
                    extent = Mathf.Max(extent, Mathf.Abs(local.x) / camera.aspect, Mathf.Abs(local.y));
                }
                camera.orthographicSize = extent * 1.12f;
                camera.farClipPlane = distance + bounds.size.magnitude + 10;
            }

            static void DrawText(Texture2D texture, int x, int y, string value)
            {
                const string alphabet = "0123456789.PLAY ERB";
                string[] glyphs =
                {
                    "111101101101111", "010110010010111", "111001111100111", "111001111001111",
                    "101101111001001", "111100111001111", "111100111101111", "111001001001001",
                    "111101111101111", "111101111001111", "000000000000010", "110101110100100",
                    "100100100100111", "010101111101101", "101101010010010", "000000000000000",
                    "111100110100111", "110101110101101", "110101110101110"
                };
                foreach (char character in value.ToUpperInvariant())
                {
                    int index = alphabet.IndexOf(character);
                    string glyph = index < 0 ? glyphs[15] : glyphs[index];
                    for (int row = 0; row < 5; row++)
                    for (int column = 0; column < 3; column++)
                    for (int yy = 0; yy < 2; yy++)
                    for (int xx = 0; xx < 2; xx++)
                    {
                        int px = x + column * 2 + xx;
                        int py = y + (4 - row) * 2 + yy;
                        if (px >= 0 && px < texture.width && py >= 0 && py < texture.height)
                            texture.SetPixel(px, py, glyph[row * 3 + column] == '1' ? Color.white : Color.black);
                    }
                    x += 8;
                }
            }

            public void Dispose()
            {
                skin?.Dispose();
                if (ground)
                    Object.DestroyImmediate(ground);
                if (groundMaterial)
                    Object.DestroyImmediate(groundMaterial);
            }
        }
    }
}
