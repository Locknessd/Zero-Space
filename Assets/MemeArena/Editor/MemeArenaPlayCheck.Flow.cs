using System;
using System.Linq;
using MemeArena;
using UnityEngine;

namespace MemeArenaEditor
{
    public static partial class MemeArenaPlayCheck
    {
        static void Advance()
        {
            if (game.QueueError != null) throw new Exception(game.QueueError);
            float elapsed = Time.unscaledTime - phaseBegan;
            if (phase == 1 && elapsed > 3)
            {
                if (!savedIdle)
                {
                    capture = "BattleIdle";
                    savedIdle = true;
                    return;
                }
                if (capture != null) return;
                if (!game.PlayAnimationTest(PlayerUI.Side.Left, combo))
                    throw new Exception("Could not start the existing animation test API.");
                Next(2);
            }
            else if (phase == 2)
            {
                CaptureFloor();
                if (!savedCombo && adapter.ContactCount > 0 && elapsed > .4f)
                {
                    capture = "BattleCombo";
                    savedCombo = true;
                }
                if (game.IsAnimationTestPlaying || elapsed < 1) return;
                if (adapter.ContactCount == 0 || adapter.ComboCount == 0)
                    throw new Exception("Real animation contacts did not produce a crowd combo reaction.");
                report.AppendLine("PASS actual combo: " + combo.moveName + "; contacts=" + adapter.ContactCount +
                    "; combo reactions=" + adapter.ComboCount + ".");
                VerifyFloorContacts();
                game.StopAnimationTest();
                if (fracture.ActiveCount != 0) throw new Exception("Animation reset did not clear pooled floor effects.");
                game.uiManager.UpdateHealth(MemeBattleUI.Side.Right, 200, 1000);
                Next(3);
            }
            else if (phase == 3 && elapsed > .7f)
            {
                var group = stage.GetComponentsInChildren<MemeCrowdGroup>()
                    .Single(value => value.supporters == MemeCrowdSide.FighterB);
                if (group.LastReaction != MemeCrowdReaction.Boo) throw new Exception("Low health did not reach supporters.");
                report.AppendLine("PASS low-health slider subscription; losing supporters react independently.");
                if (!game.PlayAnimationTest(PlayerUI.Side.Left, combo, true)) throw new Exception("Could not start lethal combo.");
                Next(4);
            }
            else if (phase == 4)
            {
                CaptureFloor();
                if (!savedKo && adapter.KoCount > 0)
                {
                    capture = "BattleKO";
                    savedKo = true;
                }
                if (game.IsAnimationTestPlaying || elapsed < 1) return;
                if (adapter.KoCount != 1 || adapter.crowd.celebration.PlayCount != 1)
                    throw new Exception("Expected one KO and one reused confetti burst: " + adapter.KoCount);
                var groups = stage.GetComponentsInChildren<MemeCrowdGroup>();
                report.AppendLine("Final supporter reactions: " + string.Join(", ",
                    groups.Select(value => value.supporters + "=" + value.LastReaction)));
                if (groups.Single(value => value.supporters == MemeCrowdSide.FighterA).LastReaction != MemeCrowdReaction.Victory ||
                    groups.Single(value => value.supporters == MemeCrowdSide.FighterB).LastReaction != MemeCrowdReaction.Defeat)
                    throw new Exception("KO supporter groups are not independent.");
                report.AppendLine("PASS actual lethal animation: one KO, supporters victory/defeat, pooled edge confetti.");
                VerifyFloorContacts();
                Next(5);
            }
            else if (phase == 5 && elapsed > 6.5f)
            {
                if (fracture.ActiveCount != 0) throw new Exception("Floor cracks did not fade back into their pool.");
                report.AppendLine("PASS floor scars fade away and pool becomes inactive after the configured lifetime.");
                var samples = probe.frameMilliseconds.OrderBy(value => value).ToArray();
                report.AppendLine("EDITOR profiling only, 1280x720, real camera and fighters; samples=" + samples.Length);
                if (samples.Length > 0)
                    report.AppendLine("Mean frame=" + samples.Average().ToString("F2") + "ms; p95=" +
                        samples[Mathf.Min(samples.Length - 1, (int)(samples.Length * .95f))].ToString("F2") + "ms.");
                report.AppendLine("Profiler counters available=" + probe.countersAvailable + "; peak batches=" +
                    probe.peakBatches + "; peak triangles=" + probe.peakTriangles + "; peak GC/frame=" + probe.peakGcBytes);
                report.AppendLine("These Editor timings do not verify 60 FPS in a WebGL or standalone build.");
                if (errorCount > 0) throw new Exception("Runtime stage errors=" + errorCount);
                report.AppendLine("PASS no stage exceptions, null references or queue errors in the tested path.");
                Finish();
            }
        }
    }
}
