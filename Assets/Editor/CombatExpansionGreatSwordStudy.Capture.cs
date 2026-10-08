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
        static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
            HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.Hips
        };

        public static void Capture() => Capture(false);
        public static void CaptureGrounded() => Capture(true);

        static void Capture(bool grounded)
        {
            string output = grounded ? Output + "/Grounded" : Output;
            Directory.CreateDirectory(output);
            var texture = RenderTexture.GetTemporary(480, 320, 24);
            var stamp = new Texture2D(480, 320, TextureFormat.RGB24, false);
            var sheet = new Texture2D(1920, 960, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var rows = new StringBuilder("fighter,move,direction,seconds,role,bone,x,y,z\n");
            var times = new StringBuilder("Actual BattleScene fighter motion study, not gameplay approval.\n" +
                "Rows left to right then top to bottom. Source library offsets; optional feedback disabled.\n" +
                "Grounding corrections enabled: " + grounded + ".\n");
            try
            {
                EachPair((source, target, move, pair, camera, framing, direction) =>
                {
                    camera.targetTexture = texture;
                    camera.aspect = 1.5f;
                    int samples = Mathf.CeilToInt(pair.Duration * 60);
                    for (int frame = 0; frame <= samples; frame++)
                    {
                        float seconds = Mathf.Min(frame / 60f, pair.Duration);
                        pair.EvaluateAt(seconds);
                        AppendBones(rows, source, source, move, "attacker", seconds, direction);
                        AppendBones(rows, source, target, move, "receiver", seconds, direction);
                    }
                    var visible = new[] { source, target }
                        .SelectMany(f => f.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                        .Where(r => r.enabled).ToArray();
                    using var skin = new CombatExpansionPreviewSkin(new[] { source.gameObject, target.gameObject });
                    times.Append(source.name + "/" + move.moveName + "/" + direction + ": ");
                    for (int frame = 0; frame < 12; frame++)
                    {
                        float seconds = pair.Duration * frame / 11;
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
                    File.WriteAllBytes(output + $"/{source.name}_{move.moveName}_{direction}.png", sheet.EncodeToPNG());
                    times.AppendLine();
                    camera.targetTexture = null;
                }, new[] { 1 }, grounded);
                File.WriteAllText(output + "/Times.txt", times.ToString());
                File.WriteAllText(output + "/Trajectories.csv", rows.ToString());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(stamp);
                Object.DestroyImmediate(sheet);
                RenderTexture.ReleaseTemporary(texture);
            }
        }

        static void AppendBones(StringBuilder csv, CharacterCombat source, CharacterCombat actor,
            CombatTripletData move, string role, float seconds, int direction)
        {
            foreach (var bone in Bones)
            {
                var transform = actor.Animator.GetBoneTransform(bone);
                if (!transform)
                    continue;
                var p = transform.position;
                csv.AppendLine(FormattableString.Invariant(
                    $"{source.name},{move.moveName},{direction},{seconds:R},{role},{bone},{p.x:R},{p.y:R},{p.z:R}"));
            }
        }
    }
}
