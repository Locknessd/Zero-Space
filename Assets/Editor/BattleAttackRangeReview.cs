using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string RangeReview = "GeneratedAssets/AttackRangeReview";

        sealed class RangeSample
        {
            public BattlePresentationContactSetup.BladeContactProbe probe;
            public Vector3 anchor;
            public float seconds;
            public string source;
            public bool center;
            public int direction;
        }

        [MenuItem("Tools/Battle/Review Execution attack ranges")]
        public static void ScanBattleAttackRanges()
        {
            Directory.CreateDirectory(RangeReview);
            var contacts = new StringBuilder("fighter,move,direction,delta,range,seconds,source,surfaceGap,anchorGap\n");
            var summary = new StringBuilder("fighter,move,originalRange,delta,selectedRange,oldGap,newGap\n");
            BattleComboAuthoring.WithStudy((scene, game, fighters) =>
            {
                var registry = new StringBuilder("fighter,move,range,offset,alignmentTolerance\n");
                foreach (var fighter in fighters)
                foreach (var move in fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves))
                {
                    if (!move.IsValid || move.sourcePair == null ||
                        Mathf.Abs(move.attackRange - move.sourcePair.receiverOffset.z) > .0001f)
                        throw new InvalidOperationException("Invalid attack spacing: " + move.moveName);
                    registry.AppendLine(FormattableString.Invariant($"{fighter.name},{move.moveName},") +
                        FormattableString.Invariant($"{move.attackRange:R},{move.sourcePair.receiverOffset.z:R},") +
                        FormattableString.Invariant($"{move.sourcePair.maximumAlignmentError:R}"));
                }
                File.WriteAllText(RangeReview + "/Registry.csv", registry.ToString());
                foreach (var source in fighters)
                foreach (var move in source.heavyCombatMoves.Where(IsExecutionRangeMove))
                {
                    var target = fighters.Single(f => f != source);
                    var profile = game.battleVfx.timeline.FindMove(move);
                    var samples = new List<RangeSample>();
                    foreach (int direction in new[] { 1, -1 })
                    {
                        PrepareRangePair(fighters, source, target, move.attackRange, direction);
                        if (profile == null || !source.ExecuteAttack(move, target))
                            throw new InvalidOperationException("Cannot evaluate " + move.moveName);
                        var pair = source.SourcePlayback;
                        try
                        {
                            foreach (var cue in profile.cues.Where(IsMeleeRangeContact))
                            foreach (int frame in new[] { -1, 0, 1 })
                            {
                                pair.EvaluateAt(cue.seconds + frame / 60f);
                                if (!cue.TryContactPosition(target.Animator, out var anchor))
                                    throw new InvalidOperationException("Missing contact anchor: " + move.moveName);
                                samples.Add(new RangeSample
                                {
                                    probe = new BattlePresentationContactSetup.BladeContactProbe(pair, source, target),
                                    anchor = anchor,
                                    seconds = cue.seconds + frame / 60f,
                                    source = cue.contactSource,
                                    center = frame == 0,
                                    direction = direction
                                });
                            }
                        }
                        finally
                        {
                            pair.Cancel();
                        }
                    }
                    if (samples.Count == 0)
                        throw new InvalidOperationException("No melee contacts: " + move.moveName);
                    float oldGap = samples.Max(s => s.probe.MeasureReceiverShift(Vector3.zero));
                    float bestScore = float.PositiveInfinity;
                    float bestDelta = 0;
                    float bestGap = oldGap;
                    for (int step = -16; step <= 6; step++)
                    {
                        float delta = step * .025f;
                        var gaps = samples.Select(s => s.probe.MeasureReceiverShift(
                            Vector3.right * s.direction * delta)).ToArray();
                        var anchors = samples.Select(s => s.probe.BladeDistance(
                            s.anchor + Vector3.right * s.direction * delta)).ToArray();
                        for (int i = 0; i < samples.Count; i++)
                            contacts.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},") +
                                FormattableString.Invariant($"{samples[i].direction},{delta:R},") +
                                FormattableString.Invariant($"{move.attackRange + delta:R},") +
                                FormattableString.Invariant($"{samples[i].seconds:R},{samples[i].source},") +
                                FormattableString.Invariant($"{gaps[i]:R},{anchors[i]:R}"));
                        // Fast cuts need contact at the accepted cue, not throughout adjacent frames.
                        var centers = Enumerable.Range(0, samples.Count).Where(i => samples[i].center).ToArray();
                        if (centers.Any(i => anchors[i] > .12f || gaps[i] > .025f))
                            continue;
                        float score = centers.Max(i => anchors[i]) + centers.Max(i => gaps[i]) * 4 +
                            Mathf.Abs(delta + move.attackRange * .08f) * .15f;
                        if (score >= bestScore)
                            continue;
                        bestScore = score;
                        bestDelta = delta;
                        bestGap = gaps.Max();
                    }
                    summary.AppendLine(FormattableString.Invariant($"{source.name},{move.moveName},") +
                        FormattableString.Invariant($"{move.attackRange:R},{bestDelta:R},") +
                        FormattableString.Invariant($"{move.attackRange + bestDelta:R},{oldGap:R},{bestGap:R}"));
                    File.WriteAllText(RangeReview + "/Candidates.csv", contacts.ToString());
                    File.WriteAllText(RangeReview + "/Suggested.csv", summary.ToString());
                }
            });
            File.WriteAllText(RangeReview + "/Scan.txt",
                "PASS saved attack ranges agree with source-pair entry offsets.\n" +
                "Measured blade contacts and adjacent frames for both fighters' Attack_Execution1-3.\n" +
                "Suggestions translate evaluated receiver surfaces; verify them with real pair playback before applying.\n");
        }

        static bool IsMeleeRangeContact(BattleSfxBank.Cue cue) => cue != null && cue.hasContactPoint &&
            cue.contactSource != "Gun" &&
            (cue.group == "light_hit" || cue.group == "heavy_hit" || cue.group == "stab_hit");

        static bool IsExecutionRangeMove(CombatTripletData move) => move != null &&
            move.moveName.StartsWith("Frank_GreatSword_Attack_Execution", StringComparison.Ordinal);

        static void PrepareRangePair(CharacterCombat[] fighters, CharacterCombat source,
            CharacterCombat target, float range, int direction)
        {
            foreach (var fighter in fighters)
                fighter.SourcePlayback?.Cancel();
            source.Animator.transform.SetPositionAndRotation(Vector3.left * direction * range * .5f,
                Quaternion.LookRotation(Vector3.right * direction));
            target.Animator.transform.SetPositionAndRotation(Vector3.right * direction * range * .5f,
                Quaternion.LookRotation(Vector3.left * direction));
            foreach (var fighter in fighters)
                fighter.ResetCombat();
        }
    }
}
