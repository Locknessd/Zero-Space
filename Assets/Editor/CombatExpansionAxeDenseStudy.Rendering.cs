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
    public static partial class CombatExpansionAxeDenseStudy
    {
        static void WriteSheets(Scene scene, CharacterCombat[] fighters, FrankBattlePairPlayback pair,
            AxeRegion[] axes, CharacterCombat source, int direction, float[] seconds, List<string> scope)
        {
            using var view = new CaptureView(scene, fighters, pair, axes);
            using var skin = new CombatExpansionPreviewSkin(fighters.Select(f => f.gameObject).ToArray());
            const int width = 480;
            const int height = 360;
            const int label = 26;
            int rows = (seconds.Length + 1) / 2;
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var stamp = new Texture2D(width, height, TextureFormat.RGB24, false);
            var sheet = new Texture2D(width * 4, rows * (height + label), TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            try
            {
                view.camera.targetTexture = target;
                foreach (bool regions in new[] { false, true })
                {
                    var pixels = new Color32[sheet.width * sheet.height];
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = new Color32(20, 25, 32, 255);
                    sheet.SetPixels32(pixels);
                    for (int frame = 0; frame < seconds.Length; frame++)
                    {
                        pair.EvaluateAt(seconds[frame]);
                        skin.Sample();
                        foreach (var axe in axes)
                            axe.Update();
                        view.Sample(regions);
                        int x = frame % 2 * width * 2;
                        int y = (rows - 1 - frame / 2) * (height + label);
                        for (int side = 0; side < 2; side++)
                        {
                            view.Frame(side, direction, width / (float)height);
                            view.camera.Render();
                            RenderTexture.active = target;
                            stamp.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                            stamp.Apply();
                            sheet.SetPixels(x + side * width, y + label, width, height, stamp.GetPixels());
                        }
                        Stamp(sheet, x + 12, y + 5, (frame + 1).ToString("D2") + "  " +
                            seconds[frame].ToString("F6", CultureInfo.InvariantCulture));
                    }
                    sheet.Apply();
                    string file = source.name + "_" + direction + (regions ? "_HeadRegions.png" : "_Plain.png");
                    File.WriteAllBytes(Output + "/" + file, sheet.EncodeToPNG());
                    scope.Add(file + ": each time has two adjacent opposing oblique views; times=" +
                        string.Join(";", seconds.Select(t => t.ToString("R", CultureInfo.InvariantCulture))));
                }
            }
            finally
            {
                view.camera.targetTexture = null;
                RenderTexture.active = previous;
                Object.DestroyImmediate(stamp);
                Object.DestroyImmediate(sheet);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        sealed class CaptureView : IDisposable
        {
            public readonly Camera camera;
            readonly Dictionary<Renderer, bool> visibility;
            readonly List<Object> owned = new List<Object>();
            readonly AxeRegion[] axes;
            readonly Mesh[] highlights;
            readonly MeshRenderer[] highlighted;
            readonly CharacterCombat[] fighters;
            Bounds bounds;

            public CaptureView(Scene scene, CharacterCombat[] fighters, FrankBattlePairPlayback pair, AxeRegion[] axes)
            {
                this.fighters = fighters;
                this.axes = axes;
                visibility = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true))
                    .ToDictionary(r => r, r => r.enabled);
                highlights = new Mesh[axes.Length];
                highlighted = new MeshRenderer[axes.Length];
                try
                {
                    foreach (var renderer in visibility.Keys)
                    {
                        bool actor = renderer.transform.IsChildOf(pair.AttackerActor.transform) ||
                            renderer.transform.IsChildOf(pair.ReceiverActor.transform);
                        bool fighter = fighters.Any(f => renderer.transform == f.transform ||
                            renderer.transform.IsChildOf(f.transform));
                        if (!actor && !fighter)
                            renderer.enabled = false;
                    }
                    var cameraObject = Create(scene, "Dense study camera");
                    camera = cameraObject.AddComponent<Camera>();
                    camera.scene = scene;
                    camera.enabled = false;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.10f, .13f, .17f);
                    camera.orthographic = true;
                    camera.nearClipPlane = .01f;
                    camera.farClipPlane = 100;
                    camera.allowHDR = false;
                    camera.allowMSAA = false;
                    for (int i = 0; i < axes.Length; i++)
                    {
                        var highlight = Create(scene, "Selected " + axes[i].hand + " head wings");
                        highlights[i] = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                        owned.Add(highlights[i]);
                        highlight.AddComponent<MeshFilter>().sharedMesh = highlights[i];
                        highlighted[i] = highlight.AddComponent<MeshRenderer>();
                        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
                        if (!shader)
                            throw new InvalidOperationException("Cannot find a head highlight shader");
                        var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                        owned.Add(material);
                        var color = i == 0 ? new Color(.1f, .95f, 1f) : new Color(1f, .45f, .05f);
                        material.color = color;
                        if (material.HasProperty("_BaseColor"))
                            material.SetColor("_BaseColor", color);
                        highlighted[i].sharedMaterial = material;
                        highlighted[i].enabled = false;
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            GameObject Create(Scene scene, string name)
            {
                var result = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(result, scene);
                owned.Add(result);
                return result;
            }

            public void Sample(bool showRegions)
            {
                bounds = new Bounds(fighters[0].Animator.GetBoneTransform(HumanBodyBones.Hips).position, Vector3.zero);
                foreach (var fighter in fighters)
                foreach (var bone in fighter.Animator.GetComponentsInChildren<Transform>())
                    bounds.Encapsulate(bone.position);
                for (int i = 0; i < axes.Length; i++)
                {
                    foreach (var vertex in axes[i].head.vertices)
                        bounds.Encapsulate(vertex);
                    highlights[i].Clear();
                    highlights[i].SetVertices(axes[i].head.vertices);
                    highlights[i].SetTriangles(axes[i].head.triangles, 0);
                    highlights[i].RecalculateNormals();
                    var vertices = highlights[i].vertices;
                    var normals = highlights[i].normals;
                    for (int vertex = 0; vertex < vertices.Length; vertex++)
                        vertices[vertex] += normals[vertex] * .001f;
                    highlights[i].vertices = vertices;
                    highlights[i].RecalculateBounds();
                    highlighted[i].enabled = showRegions;
                }
            }

            public void Frame(int side, int direction, float aspect)
            {
                Vector3 offset = new Vector3(direction * (side == 0 ? 1.2f : -1.2f), .8f,
                    side == 0 ? -4f : 4f).normalized;
                camera.transform.position = bounds.center + offset * 12;
                camera.transform.LookAt(bounds.center);
                camera.aspect = aspect;
                float extent = .5f;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Vector3 local = camera.transform.InverseTransformPoint(point);
                    extent = Mathf.Max(extent, Mathf.Abs(local.x) / aspect, Mathf.Abs(local.y));
                }
                camera.orthographicSize = extent * 1.08f;
            }

            public void Dispose()
            {
                foreach (var pair in visibility)
                    if (pair.Key)
                        pair.Key.enabled = pair.Value;
                for (int i = owned.Count - 1; i >= 0; i--)
                    if (owned[i])
                        Object.DestroyImmediate(owned[i]);
                owned.Clear();
            }
        }

        static void Stamp(Texture2D texture, int x, int y, string label)
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
                        texture.SetPixel(x + column * 3 + xx, y + (4 - row) * 3 + yy, Color.white);
                }
                x += 12;
            }
        }
    }
}
