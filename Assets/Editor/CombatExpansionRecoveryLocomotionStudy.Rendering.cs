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
    public static partial class CombatExpansionRecoveryLocomotionStudy
    {
        [Serializable] sealed class Report
        {
            public string unityVersion, scope;
            public bool sourceBytesUnchanged;
            public SourceHash[] sourceHashes;
            public List<Candidate> candidates = new List<Candidate>();
        }

        [Serializable] sealed class SourceHash
        {
            public string path, sha256;
        }

        [Serializable] sealed class Candidate
        {
            public string avatar, clip, source, controller, idle, humanoidAvatar;
            public bool humanoid, forwardReference, renderReplayMatched;
            public float duration, endpoint;
            public float[] checkpointTimes;
            public Vector3 averageSpeed, originalWorldScale, initialRoot, endRoot, translation;
            public int sampleCount;
        }

        sealed class Sample
        {
            public float time;
            public Vector3[] positions;
            public Quaternion rotation;
            public Bounds bounds;
        }

        static void WriteCsv(string path, List<Sample> samples)
        {
            using var writer = new StreamWriter(path);
            string[] names = { "root", "hips", "leftFoot", "rightFoot", "leftToes", "rightToes" };
            writer.WriteLine("seconds," + string.Join(",", names.SelectMany(n =>
                new[] { n + "X", n + "Y", n + "Z" })) + ",rootQx,rootQy,rootQz,rootQw");
            foreach (var sample in samples)
            {
                var values = new[] { sample.time }.Concat(sample.positions.SelectMany(p => new[] { p.x, p.y, p.z }))
                    .Concat(new[] { sample.rotation.x, sample.rotation.y, sample.rotation.z, sample.rotation.w });
                writer.WriteLine(string.Join(",", values.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
            }
        }

        static Bounds SkinBounds(SkinnedMeshRenderer[] skins, Mesh mesh)
        {
            bool first = true;
            var bounds = new Bounds();
            foreach (var skin in skins.Where(s => s.gameObject.activeInHierarchy))
            {
                skin.BakeMesh(mesh, false);
                mesh.RecalculateBounds();
                for (int corner = 0; corner < 8; corner++)
                {
                    var local = mesh.bounds.center + Vector3.Scale(mesh.bounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    // Bake(false) already includes calibrated skin scale, matching CombatExpansionPreviewSkin.
                    var world = skin.transform.position + skin.transform.rotation * local;
                    if (first)
                        bounds = new Bounds(world, Vector3.zero);
                    else
                        bounds.Encapsulate(world);
                    first = false;
                }
            }
            if (first)
                throw new InvalidOperationException("No active character skin for fitted bounds.");
            return bounds;
        }

        sealed class Rendering : IDisposable
        {
            const int Width = 384;
            const int Height = 320;
            const int Footer = 28;
            readonly Actor actor;
            readonly Camera camera;
            readonly CombatExpansionPreviewSkin skin;
            readonly GameObject ground, light;
            readonly Material material;

            public Rendering(Actor value)
            {
                actor = value;
                try
                {
                    skin = new CombatExpansionPreviewSkin(actor.animator.gameObject);
                    var root = new GameObject("Recovery diagnostic camera");
                    SceneManager.MoveGameObjectToScene(root, actor.scene);
                    camera = root.AddComponent<Camera>();
                    camera.scene = actor.scene;
                    camera.enabled = false;
                    camera.orthographic = true;
                    camera.aspect = (float)Width / Height;
                    camera.nearClipPlane = .01f;
                    camera.allowHDR = false;
                    camera.allowMSAA = false;
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = new Color(.13f, .17f, .22f);
                    ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    SceneManager.MoveGameObjectToScene(ground, actor.scene);
                    Object.DestroyImmediate(ground.GetComponent<Collider>());
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    if (!shader)
                        throw new InvalidOperationException("Capture requires URP Lit.");
                    material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                    material.SetColor("_BaseColor", new Color(.24f, .29f, .34f));
                    ground.GetComponent<Renderer>().sharedMaterial = material;
                    light = new GameObject("Recovery diagnostic light");
                    SceneManager.MoveGameObjectToScene(light, actor.scene);
                    light.transform.rotation = Quaternion.Euler(45, -35, 0);
                    var lamp = light.AddComponent<Light>();
                    lamp.type = LightType.Directional;
                    lamp.intensity = 1.4f;
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Write(string path, List<Sample> samples, Candidate record)
            {
                var indices = Enumerable.Range(0, 12)
                    .Select(i => Mathf.RoundToInt(i * (samples.Count - 1) / 11f)).Distinct().ToArray();
                record.checkpointTimes = indices.Select(i => samples[i].time).ToArray();
                var bounds = samples[0].bounds;
                foreach (var sample in samples)
                    bounds.Encapsulate(sample.bounds);
                bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
                int rows = Mathf.CeilToInt(indices.Length / 3f);
                int tileWidth = Width * 2;
                int tileHeight = Height + Footer;
                if (Mathf.Max(tileWidth * 3, rows * tileHeight) > SystemInfo.maxTextureSize)
                    throw new InvalidOperationException("Diagnostic sheet exceeds texture limit.");
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
                    sheet.SetPixels32(Enumerable.Repeat(new Color32(16, 20, 27, 255),
                        sheet.width * sheet.height).ToArray());
                    actor.Reset();
                    int index = 0;
                    for (int frame = 0; frame < indices.Length; frame++)
                    {
                        while (index <= indices[frame])
                        {
                            var expected = samples[index];
                            actor.Advance(expected.time);
                            var actual = actor.Read(expected.time);
                            if (actual.positions.Where((p, i) => Vector3.Distance(p, expected.positions[i]) > .0001f)
                                .Any() || Quaternion.Angle(actual.rotation, expected.rotation) > .02f)
                                throw new InvalidOperationException("Render replay diverged from CSV at " + expected.time);
                            index++;
                        }
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
                            Label(sheet, x + view * Width + 12, y + 8, (view == 0 ? "SIDE " : "OBLIQUE ") +
                                samples[indices[frame]].time.ToString("F4", CultureInfo.InvariantCulture));
                        }
                    }
                    sheet.Apply();
                    File.WriteAllBytes(path, sheet.EncodeToPNG());
                    record.renderReplayMatched = true;
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
                var direction = view == 0 ? new Vector3(-1, .12f, 0) : new Vector3(-.8f, .24f, -.75f);
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
                const string characters = "0123456789.SIDEOBLQU";
                string[] glyphs =
                {
                    "111101101101111", "010110010010111", "111001111100111", "111001111001111",
                    "101101111001001", "111100111001111", "111100111101111", "111001001001001",
                    "111101111101111", "111101111001111", "000000000000010", "111100111001111",
                    "111010010010111", "110101101101110", "111100110100111", "111101101101111",
                    "110101110101110", "100100100100111", "111101101111001", "101101101101111"
                };
                foreach (char character in value)
                {
                    int glyph = characters.IndexOf(character);
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
                if (camera)
                    Object.DestroyImmediate(camera.gameObject);
                if (ground)
                    Object.DestroyImmediate(ground);
                if (light)
                    Object.DestroyImmediate(light);
                if (material)
                    Object.DestroyImmediate(material);
            }
        }
    }
}
