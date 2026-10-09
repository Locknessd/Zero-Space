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
    public static partial class CombatExpansionSlapStudy
    {
        sealed class SourceRendering : IDisposable
        {
            const int Width = 384;
            const int Height = 320;
            const int Footer = 34;
            readonly Camera camera;
            readonly PreviewSkin skin;
            readonly List<GameObject> owned = new List<GameObject>();
            public readonly Renderer[] Renderers;

            public SourceRendering(Scene scene, Actor actor)
            {
                try
                {
                    skin = new PreviewSkin(actor.Character.gameObject);
                    Renderers = scene.GetRootGameObjects()
                        .SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
                    var cameraObject = CreateObject("Slap source camera", scene);
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
                        var lamp = CreateObject(key ? "Slap key light" : "Slap fill light", scene);
                        var light = lamp.AddComponent<Light>();
                        light.type = LightType.Directional;
                        light.intensity = key ? 1.25f : .8f;
                        lamp.transform.rotation = Quaternion.Euler(key ? 40 : 25, key ? -35 : 145, 0);
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            GameObject CreateObject(string name, Scene scene)
            {
                var value = new GameObject(name);
                owned.Add(value);
                SceneManager.MoveGameObjectToScene(value, scene);
                return value;
            }

            public void Sample() => skin.Sample();

            public void WriteSheets(string stem, Actor actor, CaptureRecord record, Bounds bounds)
            {
                int last = Mathf.CeilToInt(record.durationSeconds * 60);
                record.sheetSeconds = Enumerable.Range(0, last + 1).Where(i => i % 7 == 0 || i == last)
                    .Select(i => Mathf.Min(i / 60f, record.durationSeconds)).Distinct().ToArray();
                int columns = Mathf.Min(3, SystemInfo.maxTextureSize / (Width * 2));
                int rows = Mathf.Min(4, SystemInfo.maxTextureSize / (Height + Footer));
                if (columns < 1 || rows < 1)
                    throw new InvalidOperationException("GPU cannot fit one SlapFace evidence tile");
                int pageSize = columns * rows;
                var pages = new List<string>();
                actor.Reset();
                for (int first = 0; first < record.sheetSeconds.Length; first += pageSize)
                {
                    int count = Mathf.Min(pageSize, record.sheetSeconds.Length - first);
                    string filename = stem + "_" + (pages.Count + 1).ToString("D2") + ".png";
                    WritePage(Output + "/" + filename, actor, record, bounds, first, count, columns);
                    pages.Add(filename);
                }
                record.sheets = pages.ToArray();
            }

            void WritePage(string path, Actor actor, CaptureRecord record, Bounds bounds,
                int first, int count, int columns)
            {
                RenderTexture target = null;
                Texture2D stamp = null;
                Texture2D sheet = null;
                var previous = RenderTexture.active;
                int rows = Mathf.CeilToInt((float)count / columns);
                int tileWidth = Width * 2;
                int tileHeight = Height + Footer;
                try
                {
                    target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
                    stamp = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                    sheet = new Texture2D(tileWidth * columns, tileHeight * rows, TextureFormat.RGB24, false);
                    var background = new Color32[sheet.width * sheet.height];
                    for (int index = 0; index < background.Length; index++)
                        background[index] = new Color32(16, 20, 27, 255);
                    sheet.SetPixels32(background);
                    camera.targetTexture = target;
                    for (int frame = 0; frame < count; frame++)
                    {
                        float seconds = record.sheetSeconds[first + frame];
                        actor.Evaluate(seconds);
                        Sample();
                        int x = frame % columns * tileWidth;
                        int y = (rows - 1 - frame / columns) * tileHeight;
                        for (int view = 0; view < 2; view++)
                        {
                            Frame(bounds, view);
                            camera.Render();
                            RenderTexture.active = target;
                            stamp.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                            stamp.Apply();
                            sheet.SetPixels(x + view * Width, y + Footer, Width, Height, stamp.GetPixels());
                        }
                        DrawStamp(sheet, x + 12, y + 10, (first + frame + 1).ToString("D3") + "  " +
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
                    if (target)
                        RenderTexture.ReleaseTemporary(target);
                }
            }

            void Frame(Bounds bounds, int view)
            {
                Vector3 direction = view == 0 ? new Vector3(1, .12f, 0) : new Vector3(1, .24f, -1);
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
                camera.orthographicSize = extent * 1.1f;
                camera.farClipPlane = distance + bounds.size.magnitude + 10;
            }

            static void DrawStamp(Texture2D image, int x, int y, string label)
            {
                string[] digits =
                {
                    "111101101101111", "010110010010111", "111001111100111", "111001111001111",
                    "101101111001001", "111100111001111", "111100111101111", "111001001001001",
                    "111101111101111", "111101111001111"
                };
                foreach (char character in label)
                {
                    string pixels = character >= '0' && character <= '9' ? digits[character - '0'] :
                        character == '.' ? "000000000000010" : "000000000000000";
                    for (int row = 0; row < 5; row++)
                    for (int column = 0; column < 3; column++)
                    {
                        if (pixels[row * 3 + column] != '1')
                            continue;
                        for (int yy = 0; yy < 3; yy++)
                        for (int xx = 0; xx < 3; xx++)
                            image.SetPixel(x + column * 3 + xx, y + (4 - row) * 3 + yy, Color.white);
                    }
                    x += 12;
                }
            }

            public void Dispose()
            {
                skin?.Dispose();
                foreach (var item in owned)
                    if (item)
                        Object.DestroyImmediate(item);
                owned.Clear();
            }
        }
    }
}
