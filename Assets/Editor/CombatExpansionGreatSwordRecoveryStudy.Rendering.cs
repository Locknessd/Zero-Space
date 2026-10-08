using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRecoveryStudy
    {
        sealed partial class RecoveryPreview
        {
            const int Width = 360;
            const int Height = 320;
            const int LabelHeight = 24;
            Camera camera;

            void CreateCamera()
            {
                var owner = new GameObject("Recovery study camera");
                SceneManager.MoveGameObjectToScene(owner, scene);
                camera = owner.AddComponent<Camera>();
                camera.scene = scene;
                camera.enabled = false;
                camera.orthographic = true;
                camera.aspect = Width / (float)Height;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.12f, .16f, .21f);
                camera.nearClipPlane = .01f;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                foreach (bool key in new[] { true, false })
                {
                    var ownerLight = new GameObject(key ? "Recovery key light" : "Recovery fill light");
                    SceneManager.MoveGameObjectToScene(ownerLight, scene);
                    var light = ownerLight.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.intensity = key ? 1.25f : .75f;
                    light.color = key ? new Color(1, .93f, .85f) : new Color(.72f, .85f, 1);
                    ownerLight.transform.rotation = Quaternion.Euler(key ? 42 : 25, key ? -28 : 145, 0);
                }
            }

            public void WriteSheet(string path)
            {
                using var skin = new CombatExpansionPreviewSkin(actor.gameObject);
                MeasureMotion(skin);
                int tileWidth = Width * 2;
                int tileHeight = Height + LabelHeight;
                var target = RenderTexture.GetTemporary(Width, Height, 24, RenderTextureFormat.ARGB32);
                var stamp = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                var sheet = new Texture2D(tileWidth * 4, tileHeight * 3, TextureFormat.RGB24, false);
                var previous = RenderTexture.active;
                try
                {
                    var background = new Color32[sheet.width * sheet.height];
                    for (int i = 0; i < background.Length; i++)
                        background[i] = new Color32(16, 20, 27, 255);
                    sheet.SetPixels32(background);
                    camera.targetTexture = target;
                    FrameSeconds = new float[12];
                    Reset();
                    for (int frame = 0; frame < 12; frame++)
                    {
                        float seconds = clip.length * frame / 11;
                        FrameSeconds[frame] = seconds;
                        Evaluate(seconds);
                        skin.Sample();
                        int x = frame % 4 * tileWidth;
                        int y = (2 - frame / 4) * tileHeight;
                        for (int view = 0; view < 2; view++)
                        {
                            FrameCamera(view);
                            camera.Render();
                            RenderTexture.active = target;
                            stamp.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                            stamp.Apply();
                            RequireVisiblePixels(stamp);
                            sheet.SetPixels(x + view * Width, y + LabelHeight, Width, Height, stamp.GetPixels());
                        }
                        DrawLabel(sheet, x + 8, y + 5, (frame + 1).ToString("D2") + "  " +
                            seconds.ToString("F6", CultureInfo.InvariantCulture));
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

            void FrameCamera(int view)
            {
                var direction = view == 0 ? new Vector3(2.7f, 1.8f, 5) : new Vector3(5, 1.3f, -.5f);
                float distance = Mathf.Max(8, motionBounds.size.magnitude * 2);
                camera.transform.position = motionBounds.center + direction.normalized * distance;
                camera.transform.LookAt(motionBounds.center);
                float extent = .1f;
                for (int corner = 0; corner < 8; corner++)
                {
                    var world = motionBounds.center + Vector3.Scale(motionBounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var local = camera.transform.InverseTransformPoint(world);
                    extent = Mathf.Max(extent, Mathf.Abs(local.x) / camera.aspect, Mathf.Abs(local.y));
                }
                camera.orthographicSize = extent * 1.12f;
                camera.farClipPlane = distance + motionBounds.size.magnitude + 10;
            }

            void RequireVisiblePixels(Texture2D stamp)
            {
                var pixels = stamp.GetPixels32();
                var background = pixels[0];
                int visible = 0;
                foreach (var pixel in pixels)
                    if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) +
                        Math.Abs(pixel.b - background.b) > 18)
                        visible++;
                if (visible < 64)
                    throw new InvalidOperationException("Recovery render has no visible fighter: " + clipIdentity);
            }

            static void DrawLabel(Texture2D image, int x, int y, string label)
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
        }
    }
}
