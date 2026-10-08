using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordStudy
    {
        public static void ScanFirstLandings()
        {
            Directory.CreateDirectory(Output);
            var csv = new StringBuilder("fighter,move,direction,seconds,clearance,hipsY,headY,leftFootY,rightFootY\n");
            EachPair((source, target, move, pair, camera, framing, direction) =>
            {
                Vector2 window = move.moveName.EndsWith("Ambush", StringComparison.Ordinal) ?
                    new Vector2(.54f, .76f) : move.moveName.EndsWith("Execution1", StringComparison.Ordinal) ?
                    new Vector2(1.9f, 2.5f) : move.moveName.EndsWith("Execution2", StringComparison.Ordinal) ?
                    new Vector2(1.85f, 2.3f) : new Vector2(1.4f, 1.85f);
                for (int frame = Mathf.RoundToInt(window.x * 240); frame <= window.y * 240; frame++)
                {
                    float seconds = frame / 240f;
                    pair.EvaluateAt(seconds);
                    float clearance = BattlePresentationContactSetup.MeasureGroundClearance(target);
                    if (!float.IsFinite(clearance))
                        throw new InvalidOperationException("No visible receiver surface for " + move.moveName);
                    var animator = target.Animator;
                    float hips = animator.GetBoneTransform(HumanBodyBones.Hips).position.y;
                    float head = animator.GetBoneTransform(HumanBodyBones.Head).position.y;
                    float left = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y;
                    float right = animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y;
                    csv.AppendLine(FormattableString.Invariant(
                        $"{source.name},{move.moveName},{direction},{seconds:R},{clearance:R},") +
                        FormattableString.Invariant($"{hips:R},{head:R},{left:R},{right:R}"));
                }
                File.WriteAllText(Output + "/FirstLandings.csv", csv.ToString());
            }, new[] { 1, -1 }, true);
        }

        public static void ScanBladeContacts()
        {
            Directory.CreateDirectory(Output);
            var csv = new StringBuilder("fighter,move,direction,seconds,gap,region," +
                "contactX,contactY,contactZ,offsetX,offsetY,offsetZ,tipX,tipY,tipZ\n");
            EachPair((source, target, move, pair, camera, framing, direction) =>
            {
                Vector2[] windows;
                if (move.moveName.EndsWith("Ambush", StringComparison.Ordinal))
                    windows = new[] { new Vector2(.55f, .625f) };
                else if (move.moveName.EndsWith("Execution3", StringComparison.Ordinal))
                    windows = new[] { new Vector2(21f / 60, 24f / 60),
                        new Vector2(40f / 60, 42f / 60), new Vector2(1.5f, 1.55f) };
                else
                    windows = new[] { new Vector2(.8f, .875f) };
                foreach (var window in windows)
                {
                    int first = Mathf.RoundToInt(window.x * 240);
                    int last = Mathf.RoundToInt(window.y * 240);
                    for (int frame = first; frame <= last; frame++)
                    {
                        float seconds = frame / 240f;
                        pair.EvaluateAt(seconds);
                        var probe = new BattlePresentationContactSetup.BladeContactProbe(pair, source, target);
                        float gap = probe.Measure(out var world, out var bone, out var offset);
                        var tip = probe.Tip;
                        csv.AppendLine(FormattableString.Invariant(
                            $"{source.name},{move.moveName},{direction},{seconds:R},{gap:R},{bone},") +
                            FormattableString.Invariant(
                                $"{world.x:R},{world.y:R},{world.z:R},{offset.x:R},{offset.y:R},{offset.z:R},") +
                            FormattableString.Invariant($"{tip.x:R},{tip.y:R},{tip.z:R}"));
                    }
                }
                File.WriteAllText(Output + "/BladeContacts.csv", csv.ToString());
            }, new[] { 1, -1 }, true);
            File.WriteAllText(Output + "/BladeContactsScope.txt",
                "240 Hz selected windows on grounded current poses; both avatars and both directions. " +
                "Excludes the proximal fifth of the grip-to-tip span to separate blade from hilt/guard. " +
                "Geometry evidence requires matching source-pose review; no automatic damage assignment.\n");
        }
    }
}
