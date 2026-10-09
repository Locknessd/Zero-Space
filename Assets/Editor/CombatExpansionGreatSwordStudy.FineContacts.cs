using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordStudy
    {
        public static void CaptureFineContacts()
        {
            string output = Output + "/FineContacts";
            Directory.CreateDirectory(output);
            var texture = RenderTexture.GetTemporary(480, 320, 24);
            var stamp = new Texture2D(480, 320, TextureFormat.RGB24, false);
            var sheet = new Texture2D(1920, 960, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var times = new StringBuilder("Current grounded, lane-constrained source poses; effects disabled.\n" +
                "Frames left to right, then top to bottom. Timing in source seconds; review pending.\n");
            var rows = new StringBuilder("fighter,move,direction,seconds,role,bone,x,y,z\n");
            var ground = new StringBuilder("fighter,move,direction,seconds,receiverClearance\n");
            try
            {
                EachPair((source, target, move, pair, camera, framing, direction) =>
                {
                    float[] samples = FineTimes(move.moveName);
                    camera.targetTexture = texture;
                    camera.aspect = 1.5f;
                    var visible = new[] { source, target }
                        .SelectMany(f => f.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                        .Where(r => r.enabled).ToArray();
                    using var skin = new CombatExpansionPreviewSkin(new[] { source.gameObject, target.gameObject });
                    string key = source.name + "_" + move.moveName + "_" + direction;
                    times.Append(key + ": ");
                    for (int frame = 0; frame < samples.Length; frame++)
                    {
                        float seconds = samples[frame];
                        pair.EvaluateAt(seconds);
                        foreach (var renderer in visible)
                            renderer.enabled = true;
                        AppendBones(rows, source, source, move, "attacker", seconds, direction);
                        AppendBones(rows, source, target, move, "receiver", seconds, direction);
                        float clearance = BattlePresentationContactSetup.MeasureGroundClearance(target);
                        if (!float.IsFinite(clearance))
                            throw new InvalidOperationException("No visible body clearance: " + key);
                        ground.AppendLine(FormattableString.Invariant(
                            $"{source.name},{move.moveName},{direction},{seconds:R},{clearance:R}"));
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
                    File.WriteAllBytes(output + "/" + key + ".png", sheet.EncodeToPNG());
                    times.Length--;
                    times.AppendLine();
                    camera.targetTexture = null;
                }, new[] { 1, -1 }, true);
                File.WriteAllText(output + "/Times.txt", times.ToString());
                File.WriteAllText(output + "/Trajectories.csv", rows.ToString());
                File.WriteAllText(output + "/Ground.csv", ground.ToString());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(stamp);
                Object.DestroyImmediate(sheet);
                RenderTexture.ReleaseTemporary(texture);
            }
        }

        static float[] FineTimes(string action)
        {
            if (action.EndsWith("Ambush", StringComparison.Ordinal))
                return new[] { 135f, 136, 137, 138, 139, 140, 141, 142, 144, 147, 150, 156 }
                    .Select(frame => frame / 240).ToArray();
            if (action.EndsWith("Execution1", StringComparison.Ordinal))
                return new[] { 48f, 49, 50, 51, 52, 53, 131, 133, 135, 137, 139, 141 }
                    .Select(frame => frame / 60).ToArray();
            if (action.EndsWith("Execution2", StringComparison.Ordinal))
                return new[] { 46f, 47, 48, 49, 50, 51, 122, 124, 126, 128, 130, 132 }
                    .Select(frame => frame / 60).ToArray();
            return new[] { 87f, 88, 89, 163, 164, 165, 364, 365, 366, 367, 370, 374 }
                .Select(frame => frame / 240).ToArray();
        }
    }
}
