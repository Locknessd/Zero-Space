using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionHumanoidStudy
    {
        sealed partial class MotionPreview
        {
            const int ViewSize = 320;
            const int LabelHeight = 32;
            Camera camera;

            void CreateCamera()
            {
                var cameraObject = new GameObject("Owned study camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                camera = cameraObject.AddComponent<Camera>();
                camera.scene = scene;
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.13f, .17f, .22f);
                camera.orthographic = true;
                camera.aspect = 1;
                camera.nearClipPlane = .01f;
                camera.farClipPlane = 500;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                foreach (bool key in new[] { true, false })
                {
                    var lamp = new GameObject(key ? "Owned key light" : "Owned fill light");
                    SceneManager.MoveGameObjectToScene(lamp, scene);
                    var light = lamp.AddComponent<Light>();
                    light.type = LightType.Directional;
                    light.intensity = key ? 1.25f : .75f;
                    light.color = key ? new Color(1, .93f, .85f) : new Color(.72f, .85f, 1);
                    lamp.transform.rotation = Quaternion.Euler(key ? 42 : 25, key ? -28 : 145, 0);
                }
            }

            public void WriteSheet(string path, StudyRecord record)
            {
                int frames = Mathf.Max(12, Mathf.CeilToInt(Duration * 6) + 1);
                frames = Mathf.CeilToInt(frames / 4f) * 4;
                int rows = frames / 4;
                int tileWidth = ViewSize * 2;
                int tileHeight = ViewSize + LabelHeight;
                if (rows * tileHeight > SystemInfo.maxTextureSize || tileWidth * 4 > SystemInfo.maxTextureSize)
                    throw new InvalidOperationException("Contact sheet exceeds GPU limits; split this source study");
                var target = RenderTexture.GetTemporary(ViewSize, ViewSize, 24, RenderTextureFormat.ARGB32);
                var stamp = new Texture2D(ViewSize, ViewSize, TextureFormat.RGB24, false);
                var sheet = new Texture2D(tileWidth * 4, tileHeight * rows, TextureFormat.RGB24, false);
                var previous = RenderTexture.active;
                try
                {
                    var background = new Color32[sheet.width * sheet.height];
                    for (int pixel = 0; pixel < background.Length; pixel++)
                        background[pixel] = new Color32(16, 20, 27, 255);
                    sheet.SetPixels32(background);
                    camera.targetTexture = target;
                    record.sheetSeconds = new float[frames];
                    Reset();
                    for (int frame = 0; frame < frames; frame++)
                    {
                        float seconds = Duration * frame / (frames - 1);
                        record.sheetSeconds[frame] = seconds;
                        Evaluate(seconds);
                        int x = frame % 4 * tileWidth;
                        int y = (rows - 1 - frame / 4) * tileHeight;
                        for (int view = 0; view < 2; view++)
                        {
                            FrameCamera(view);
                            camera.Render();
                            RenderTexture.active = target;
                            stamp.ReadPixels(new Rect(0, 0, ViewSize, ViewSize), 0, 0);
                            stamp.Apply();
                            sheet.SetPixels(x + view * ViewSize, y + LabelHeight, ViewSize, ViewSize,
                                stamp.GetPixels());
                        }
                        DrawStamp(sheet, x + 10, y + 9, (frame + 1).ToString("D2") + "  " +
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
                Vector3 direction = view == 0 ? new Vector3(2.7f, 1.25f, 5) : new Vector3(5, 1.25f, -.5f);
                float distance = Mathf.Max(8, motionBounds.size.magnitude * 2);
                camera.transform.position = motionBounds.center + direction.normalized * distance;
                camera.transform.LookAt(motionBounds.center);
                float extent = .5f;
                for (int corner = 0; corner < 8; corner++)
                {
                    var world = motionBounds.center + Vector3.Scale(motionBounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var local = camera.transform.InverseTransformPoint(world);
                    extent = Mathf.Max(extent, Mathf.Abs(local.x), Mathf.Abs(local.y));
                }
                camera.orthographicSize = extent * 1.08f;
                camera.farClipPlane = distance + motionBounds.size.magnitude + 10;
            }

            static void DrawStamp(Texture2D image, int x, int y, string label)
            {
                // Five rows of three bits; stamped labels are frame index followed by seconds.
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
