using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordStudy
    {
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
