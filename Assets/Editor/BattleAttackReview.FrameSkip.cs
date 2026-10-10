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
        readonly struct EffectPosition
        {
            internal readonly string id;
            internal readonly Vector3 position;

            internal EffectPosition(string id, Vector3 position)
            {
                this.id = id;
                this.position = position;
            }
        }

        public static void ValidateComboFrameSkips()
        {
            var report = new StringBuilder();
            int cases = 0;
            BattleComboAuthoring.WithStudy((scene, game, fighters) =>
            {
                var vfx = game.battleVfx;
                foreach (var fighter in fighters)
                    fighter.battleVfx = vfx;
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves.Where(m => m.moveName.StartsWith("combo_")))
                foreach (int direction in new[] { 1, -1 })
                {
                    var target = fighters.Single(f => f != source);
                    var exact = SampleEffects(fighters, source, target, move, vfx, direction, false);
                    var skipped = SampleEffects(fighters, source, target, move, vfx, direction, true);
                    if (exact.Count != skipped.Count)
                        throw new InvalidOperationException("Frame skip lost combo effects: " + move.moveName);
                    float worst = 0;
                    for (int index = 0; index < exact.Count; index++)
                    {
                        float gap = Vector3.Distance(exact[index].position, skipped[index].position);
                        if (exact[index].id != skipped[index].id || gap > .005f)
                            throw new InvalidOperationException("Frame skip displaced combo VFX: " + move.moveName);
                        worst = Mathf.Max(worst, gap);
                    }
                    report.AppendLine($"PASS {source.name}/{move.moveName} direction={direction}: " +
                        $"{exact.Count} effects; worst position delta={worst:R}m; audio disabled.");
                    cases++;
                }
            });
            report.AppendLine("PASS all " + cases + " combo frame skips and cancellation checks.");
            File.WriteAllText(Output + "/FrameSkips.txt", report.ToString());
        }

        static List<EffectPosition> SampleEffects(CharacterCombat[] fighters, CharacterCombat source,
            CharacterCombat target, CombatTripletData move, BattleVfxPlayer vfx, int direction, bool skip)
        {
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            vfx.ResetForMatch();
            source.transform.position = Vector3.zero;
            target.transform.position = Vector3.right * direction * move.attackRange;
            var positions = new List<EffectPosition>();
            Action<string, GameObject> observe = (id, root) =>
                positions.Add(new EffectPosition(id, root.transform.position));
            vfx.EffectPlayed += observe;
            try
            {
                if (!source.ExecuteAttack(move, target))
                    throw new InvalidOperationException("Frame skip attack rejected.");
                var playback = source.SourcePlayback;
                var profile = playback.PresentationProfile(vfx.timeline);
                var times = skip ? new[] { playback.Duration } :
                    profile.cues.Select(c => c.seconds).Distinct().ToArray();
                foreach (float time in times)
                {
                    playback.EvaluateAt(time);
                    vfx.AdvanceSequence(playback, time);
                    if (Mathf.Abs(playback.SampleTime - time) > .0001f)
                        throw new InvalidOperationException("Frame skip changed the displayed pose.");
                }
                int expected = profile.cues.Length + profile.cues.Count(c => c.group == "body_fall") +
                    profile.cues.Count(c => c.finalLanding);
                if (positions.Count != expected)
                    throw new InvalidOperationException("Missing combo VFX with audio disabled.");
                playback.Cancel();
                if (vfx.ActiveEffectCount != 0)
                    throw new InvalidOperationException("Cancelled combo retained VFX.");
                return positions;
            }
            finally
            {
                source.SourcePlayback?.Cancel();
                vfx.EffectPlayed -= observe;
            }
        }
    }
}
