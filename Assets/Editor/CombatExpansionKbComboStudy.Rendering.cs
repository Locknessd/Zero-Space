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
    public static partial class CombatExpansionKbComboStudy
    {
        sealed class CandidateRendering : IDisposable
        {
            const int Width = 480;
            const int Height = 288;
            const int Footer = 46;
            readonly Camera camera;
            readonly CombatExpansionPreviewSkin skin;
            readonly List<(Renderer renderer, bool enabled)> environment =
                new List<(Renderer, bool)>();
            readonly List<(Canvas canvas, bool enabled)> canvases = new List<(Canvas, bool)>();
            readonly GameObject ground;
            readonly Material groundMaterial;

            public CandidateRendering(Scene scene, CharacterCombat[] fighters, FrankBattlePairPlayback pair)
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
                    var root = new GameObject("KB combo candidate runtime pair capture camera");
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
                        throw new InvalidOperationException("KB combo candidate capture requires URP Lit shader.");
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

            public string[] Write(string stem, Bounds bounds, CandidateRecord record, Action<float> sample)
            {
                int pageSize = Mathf.Min(36, SystemInfo.maxTextureSize / (Height + Footer) * 3);
                if (pageSize < 24 || SystemInfo.maxTextureSize < Width * 6)
                    throw new InvalidOperationException("GPU cannot fit clear candidate sheets.");
                var pages = new List<string>();
                for (int first = 0; first < record.sheetSeconds.Length; first += pageSize)
                {
                    string path = stem + "_" + (pages.Count + 1).ToString("D2") + ".png";
                    WritePage(path, bounds, record.sheetSeconds.Skip(first).Take(pageSize).ToArray(), record, sample);
                    pages.Add(Path.GetFileName(path));
                }
                return pages.ToArray();
            }

            void WritePage(string path, Bounds bounds, float[] times, CandidateRecord record, Action<float> sample)
            {
                int rows = Mathf.CeilToInt(times.Length / 3f);
                int tileWidth = Width * 2;
                int tileHeight = Height + Footer;
                if (rows * tileHeight > SystemInfo.maxTextureSize)
                    throw new InvalidOperationException("KB combo candidate runtime sheet exceeds texture limits.");
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
                        CandidateLabel(sheet, x + 12, y + 26, "KB CANDIDATE " +
                            record.attacker.ToUpperInvariant() + " LANE" + (record.laneSign > 0 ? "POS" : "NEG") +
                            " RANGE" + record.spacing.ToString("F2", CultureInfo.InvariantCulture) + "M");
                        CandidateLabel(sheet, x + 12, y + 8, "SIDE  |  OBLIQUE   T=" +
                            times[frame].ToString("F4", CultureInfo.InvariantCulture) + "S");
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
