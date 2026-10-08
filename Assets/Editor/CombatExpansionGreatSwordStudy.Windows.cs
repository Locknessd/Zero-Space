using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordStudy
    {
        public static void CaptureWindows()
        {
            string output = Output + "/ContactWindows";
            Directory.CreateDirectory(output);
            var texture = RenderTexture.GetTemporary(480, 320, 24);
            var stamp = new Texture2D(480, 320, TextureFormat.RGB24, false);
            var sheet = new Texture2D(1920, 960, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var times = new StringBuilder("Grounded contact review. Frames left to right, then top to bottom.\n" +
                "Each window contains twelve samples at 0.05 second intervals. No hit timing assigned.\n");
            try
            {
                EachPair((source, target, move, pair, camera, framing, direction) =>
                {
                    camera.targetTexture = texture;
                    camera.aspect = 1.5f;
                    var visible = new[] { source, target }
                        .SelectMany(f => f.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                        .Where(r => r.enabled).ToArray();
                    using var skin = new CombatExpansionPreviewSkin(new[] { source.gameObject, target.gameObject });
                    for (int window = 0; window < 4; window++)
                    {
                        float start = .2f + window * .55f;
                        times.Append(source.name + "/" + move.moveName + "/" + window + ": ");
                        for (int frame = 0; frame < 12; frame++)
                        {
                            float seconds = Mathf.Min(start + frame * .05f, pair.Duration);
                            pair.EvaluateAt(seconds);
                            foreach (var renderer in visible)
                                renderer.enabled = true;
                            framing.Apply(0, true);
                            skin.Sample();
                            camera.Render();
                            RenderTexture.active = texture;
                            stamp.ReadPixels(new Rect(0, 0, 480, 320), 0, 0);
                            stamp.Apply();
                            sheet.SetPixels(frame % 4 * 480, (2 - frame / 4) * 320, 480, 320, stamp.GetPixels());
                            times.Append(seconds.ToString("F4", System.Globalization.CultureInfo.InvariantCulture) + " ");
                        }
                        sheet.Apply();
                        string label = source.name + "_" + move.moveName + "_Window" + window;
                        File.WriteAllBytes(output + "/" + label + ".png", sheet.EncodeToPNG());
                        times.AppendLine();
                    }
                    camera.targetTexture = null;
                }, new[] { 1 }, true);
                File.WriteAllText(output + "/Times.txt", times.ToString());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(stamp);
                Object.DestroyImmediate(sheet);
                RenderTexture.ReleaseTemporary(texture);
            }
        }
    }
}
