using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordStudy
    {
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
