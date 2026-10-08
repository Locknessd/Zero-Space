using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordStudy
    {
        public static void ScanContactCandidates()
        {
            Directory.CreateDirectory(Output);
            var report = new StringBuilder("fighter,move,direction,seconds,gap,region," +
                "contactX,contactY,contactZ,offsetX,offsetY,offsetZ\n");
            EachPair((source, target, move, pair, camera, framing, direction) =>
            {
                float[] times;
                if (move.moveName.EndsWith("Ambush", StringComparison.Ordinal))
                    times = new[] { .4833333f, .5f, .5166667f, .5333333f, .55f, .5666667f, .5833333f, .6f };
                else if (move.moveName.EndsWith("Execution3", StringComparison.Ordinal))
                    times = new[] { .35f, .3666667f, .3833333f, .4f, .6333333f, .65f, .6666667f,
                        .6833333f, .7f, .7166667f, .7333333f, 1.4833333f, 1.5f, 1.5166667f,
                        1.5333333f, 1.55f, 1.5666667f, 1.5833333f };
                else
                    times = new[] { .7666667f, .7833333f, .8f, .8166667f, .8333333f, .85f, .8666667f, .8833333f };
                foreach (float seconds in times)
                {
                    pair.EvaluateAt(seconds);
                    BattlePresentationContactSetup.TryMeasureContact(pair, source, target, "Weapon",
                        out var bone, out var offset, out float gap, out var world);
                    report.AppendLine(FormattableString.Invariant(
                        $"{source.name},{move.moveName},{direction},{seconds:R},{gap:R},{bone},") +
                        FormattableString.Invariant(
                            $"{world.x:R},{world.y:R},{world.z:R},{offset.x:R},{offset.y:R},{offset.z:R}"));
                }
                File.WriteAllText(Output + "/ContactCandidates.csv", report.ToString());
            }, new[] { 1, -1 }, true);
            File.WriteAllText(Output + "/ContactCandidatesScope.txt",
                "Selected windows from reviewed source motion; both avatars and both directions. " +
                "Uses current lane-constrained, grounded poses and complete weapon mesh. " +
                "These are geometry candidates, not assigned or confirmed damage events.\n");
        }

        public static void ScanContacts()
        {
            Directory.CreateDirectory(Output);
            var report = new StringBuilder("fighter,move,direction,seconds,striker,gap,region," +
                "contactX,contactY,contactZ,offsetX,offsetY,offsetZ,attackerHipsX,receiverHipsX\n");
            EachPair((source, target, move, pair, camera, framing, direction) =>
            {
                int samples = Mathf.CeilToInt(Mathf.Min(pair.Duration, 2.6f) * 60);
                for (int frame = 12; frame <= samples; frame++)
                {
                    float seconds = Mathf.Min(frame / 60f, pair.Duration);
                    pair.EvaluateAt(seconds);
                    BattlePresentationContactSetup.TryMeasureContact(pair, source, target, "Weapon",
                        out var bone, out var offset, out float gap, out var world);
                    float a = pair.AttackerActor.Pose.targetHips.position.x;
                    float b = pair.ReceiverActor.Pose.targetHips.position.x;
                    report.AppendLine(FormattableString.Invariant(
                        $"{source.name},{move.moveName},{direction},{seconds:R},Weapon,{gap:R},{bone},{world.x:R},{world.y:R},{world.z:R},{offset.x:R},{offset.y:R},{offset.z:R},{a:R},{b:R}"));
                }
                File.WriteAllText(Output + "/Contacts.csv", report.ToString());
            }, new[] { 1 }, true);
            File.WriteAllText(Output + "/ContactScope.txt", "60Hz moving sword mesh versus current victim mesh. " +
                "Grounding enabled. Closest points include the complete weapon, so motion review must identify " +
                "blade contact versus grip or passive overlap. No damage timing is assigned by this scan.\n");
        }
    }
}
