using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleAttackReview
    {
        public static void ValidateLight3()
        {
            var report = new StringBuilder();
            int cases = 0;
            BattleComboAuthoring.WithStudy((scene, game, fighters) =>
            {
                var vfx = game.battleVfx;
                foreach (var fighter in fighters)
                    fighter.battleVfx = vfx;
                foreach (var source in fighters)
                foreach (int direction in new[] { 1, -1 })
                foreach (bool lethal in new[] { false, true })
                foreach (bool skip in new[] { false, true })
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    vfx.ResetForMatch();
                    var target = fighters.Single(f => f != source);
                    var move = source.lightCombatMoves.Single(m => m.moveName == "Light_3");
                    source.transform.position = Vector3.zero;
                    target.transform.position = Vector3.right * direction * move.attackRange;
                    var impacts = new List<(float time, Vector3 position)>();
                    int count = 0;
                    Action<string, GameObject> observe = (id, root) =>
                    {
                        count++;
                        if (id != "heavy_hit")
                            return;
                        impacts.Add((source.SourcePlayback.SampleTime, root.transform.position));
                        var systems = root.GetComponentsInChildren<ParticleSystem>(true);
                        foreach (var system in systems)
                            system.Simulate(.045f, false, true, true);
                        if (!systems.Any(p => p.particleCount > 0))
                            throw new InvalidOperationException("Light_3 impact has no visible particles.");
                    };
                    vfx.EffectPlayed += observe;
                    try
                    {
                        if (!source.ExecuteAttack(move, target, lethal))
                            throw new InvalidOperationException("Light_3 attack rejected.");
                        var playback = source.SourcePlayback;
                        var profile = playback.PresentationProfile(vfx.timeline);
                        var cue = profile.cues.Single(IsContact);
                        if (cue.group != "heavy_hit" || Mathf.Abs(cue.seconds - Light3ImpactSeconds) > .0001f)
                            throw new InvalidOperationException("Light_3 knee impact must use native frame 78.");
                        playback.EvaluateAt(cue.seconds);
                        if (!cue.TryContactPosition(target.Animator, out var point))
                            throw new InvalidOperationException("Light_3 receiver anchor missing.");
                        var probe = new BattlePresentationContactSetup.ContactProbe(playback,
                            source, target, cue.contactSource);
                        float sourceGap = probe.SourceDistance(point);
                        float bodyGap = probe.BodyDistance(point);
                        if (sourceGap > .15f || bodyGap > .025f)
                        {
                            Capture(scene, playback, new[] { cue.seconds },
                                Output + "/Light3_contact_failure.png");
                            throw new InvalidOperationException($"Light_3 {source.name} direction={direction} " +
                                $"lethal={lethal} skip={skip}: source={sourceGap:R}m; skin={bodyGap:R}m; " +
                                $"anchor={point.ToString("R")}");
                        }
                        var times = skip ? new[] { playback.Duration } :
                            profile.cues.Select(c => c.seconds).Distinct().ToArray();
                        foreach (float time in times)
                        {
                            playback.EvaluateAt(time);
                            vfx.AdvanceSequence(playback, time);
                            if (Mathf.Abs(playback.SampleTime - time) > .0001f)
                                throw new InvalidOperationException("Light_3 VFX changed the animation clock.");
                        }
                        if (impacts.Count != 1 || Mathf.Abs(impacts[0].time - Light3ImpactSeconds) > .0001f ||
                            Vector3.Distance(impacts[0].position, point) > .016f)
                            throw new InvalidOperationException("Light_3 knee VFX was lost, duplicated or misplaced.");
                        int before = count;
                        vfx.AdvanceSequence(playback, playback.Duration);
                        vfx.AdvanceSequence(playback, .2f);
                        if (count != before)
                            throw new InvalidOperationException("Light_3 replayed an impact after a repeated frame.");
                        if (direction == 1 && !lethal && !skip)
                            CaptureEffects(scene, playback, vfx, profile, "Light3_" + source.name);
                        playback.Cancel();
                        if (vfx.ActiveEffectCount != 0)
                            throw new InvalidOperationException("Cancelled Light_3 retained VFX.");
                        report.AppendLine($"PASS {source.name} direction={direction} lethal={lethal} skip={skip}: " +
                            $"one visible knee hit at {cue.seconds:R}s; source={sourceGap:R}m; skin={bodyGap:R}m.");
                        File.WriteAllText(Output + "/Light3Validation.txt", report.ToString());
                        cases++;
                    }
                    finally
                    {
                        source.SourcePlayback?.Cancel();
                        vfx.EffectPlayed -= observe;
                    }
                }
            });
            report.AppendLine($"PASS {cases} Light_3 cases, including frame skips and both facing directions.");
            File.WriteAllText(Output + "/Light3Validation.txt", report.ToString());
        }
    }
}
