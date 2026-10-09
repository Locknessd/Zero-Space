using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static void E10Render(CharacterCombat[] fighters, CharacterCombat source, CharacterCombat target,
            E10Case record)
        {
            var pair = E10Begin(fighters, source, target, record.direction, record);
            try
            {
                pair.EvaluateAt(pair.Duration);
                var visible = fighters.SelectMany(f => f.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                    .Where(r => r.enabled).ToArray();
                // Construct while actors exist; their native weapons are static meshes, not CPU skins.
                using var rendering = new GameplayPairRendering(source.gameObject.scene, fighters, pair);
                record.sheetRecoverySeconds = new[] { 0, .05f, .1f, .2f, .35f, .5f, .65f, .8f, .95f,
                    record.recoveryDuration - .001f };
                var times = new[] { pair.Duration }.Concat(record.sheetRecoverySeconds
                    .Select(t => pair.Duration + t)).ToArray();
                int frame = 0;
                float current = 0;
                string stem = record.source + "_" + record.target + "_" +
                    (record.direction > 0 ? "Positive" : "Negative");
                string path = E10Output + "/" + stem + ".png";
                rendering.Write(path, record.bounds, times, ignored =>
                {
                    if (frame == 1)
                        E10Complete(pair);
                    if (frame > 0)
                    {
                        float seconds = record.sheetRecoverySeconds[frame - 1];
                        while (current < seconds)
                        {
                            float step = Mathf.Min(1f / 361, seconds - current);
                            E10Advance(pair, target, step);
                            current += step;
                        }
                        E10Positions(E10Bones(target));
                    }
                    foreach (var renderer in visible)
                        renderer.enabled = true;
                    frame++;
                });
                record.sheet = path;
                File.WriteAllText(E10Output + "/" + stem + ".txt",
                    E10Scope + "\nTiles are row-major; each tile has fixed side and oblique views.\n" +
                    "Tile 1: full source endpoint. Tile 2: actual recovery entry at t=0.\n" +
                    "Remaining recovery seconds: " + string.Join(", ", record.sheetRecoverySeconds.Skip(1)
                        .Select(t => t.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) +
                    "\nNumeric image footers are source duration plus recovery seconds; " +
                    "the first two footers deliberately share the endpoint time.\n" +
                    "End-left-limit is recovery clip duration minus 0.001 seconds.\n");
            }
            finally
            {
                E10Cancel(pair, source, target);
            }
        }
    }
}
