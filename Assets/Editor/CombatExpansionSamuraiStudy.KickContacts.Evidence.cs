using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static void KCSelectCandidates(KCCase record)
        {
            var frames = record.samples;
            for (int channel = 0; channel < 4; channel++)
            {
                float minimum = frames.Min(f => f.gaps[channel].gapM);
                int global = frames.FindIndex(f => f.gaps[channel].gapM == minimum);
                var indices = new HashSet<int> { global };
                for (int index = 1; index + 1 < frames.Count; index++)
                    if (frames[index].gaps[channel].gapM < frames[index - 1].gaps[channel].gapM &&
                        frames[index].gaps[channel].gapM <= frames[index + 1].gaps[channel].gapM)
                        indices.Add(index);
                foreach (int index in indices.OrderBy(i => i))
                {
                    int before = Math.Max(0, index - 1), after = Math.Min(frames.Count - 1, index + 1);
                    record.candidates.Add(new KCCandidate
                    {
                        channel = KCChannels[channel], kind = index == global ? "globalMinimum" : "localMinimum",
                        sampleIndex = index, previousIndex = before, nextIndex = after,
                        seconds = frames[index].seconds, gapM = frames[index].gaps[channel].gapM,
                        previousSeconds = frames[before].seconds, previousGapM = frames[before].gaps[channel].gapM,
                        nextSeconds = frames[after].seconds, nextGapM = frames[after].gaps[channel].gapM,
                        tiedMinimumSamples = frames.Count(f =>
                            Mathf.Abs(f.gaps[channel].gapM - frames[index].gaps[channel].gapM) <= .00001f)
                    });
                }
            }
        }

        static void KCVerify(FrankBattlePairPlayback pair, CharacterCombat[] fighters,
            CharacterCombat source, CharacterCombat target, KCProbes probes,
            List<(string role, string rig, string bone, Transform node)> nodes,
            KCCase record, KCReport report, ref Bounds bounds)
        {
            foreach (var original in record.samples.AsEnumerable().Reverse())
            {
                var seek = new KCSeek
                {
                    seconds = original.seconds, fromSeconds = pair.Duration,
                    repeated = new KCFrame { seconds = original.seconds }
                };
                record.backwardSeeks.Add(seek);
                pair.EvaluateAt(pair.Duration);
                KCMeasure(pair, fighters, source, target, probes, nodes, seek.repeated, ref bounds);
                for (int channel = 0; channel < 4; channel++)
                {
                    var first = original.gaps[channel];
                    var again = seek.repeated.gaps[channel];
                    seek.maxGapErrorM = Mathf.Max(seek.maxGapErrorM, Mathf.Abs(first.gapM - again.gapM));
                    seek.maxClosestPointErrorM = Mathf.Max(seek.maxClosestPointErrorM,
                        Vector3.Distance(first.footWorldPoint, again.footWorldPoint),
                        Vector3.Distance(first.targetWorldPoint, again.targetWorldPoint));
                }
                for (int index = 0; index < original.transforms.Count; index++)
                {
                    var first = original.transforms[index];
                    var again = seek.repeated.transforms[index];
                    seek.maxAnchorErrorM = Mathf.Max(seek.maxAnchorErrorM,
                        Vector3.Distance(first.worldPosition, again.worldPosition));
                    seek.maxRotationErrorDegrees = Mathf.Max(seek.maxRotationErrorDegrees,
                        Quaternion.Angle(first.worldRotation, again.worldRotation));
                    seek.maxScaleError = Mathf.Max(seek.maxScaleError,
                        Vector3.Distance(first.worldScale, again.worldScale));
                }
                seek.maxFloorErrorM = Mathf.Max(
                    Mathf.Abs(original.attackerFloorClearanceM - seek.repeated.attackerFloorClearanceM),
                    Mathf.Abs(original.victimFloorClearanceM - seek.repeated.victimFloorClearanceM));
                float worst = Mathf.Max(seek.maxGapErrorM, seek.maxClosestPointErrorM,
                    seek.maxAnchorErrorM, seek.maxFloorErrorM);
                seek.withinOneMillimeter = float.IsFinite(worst) && worst <= KCSeekTolerance;
                KCFlush(report);
                if (!seek.withinOneMillimeter)
                    throw new InvalidOperationException(FormattableString.Invariant(
                        $"Execution08 kick backwards seek FAILED: {record.file} at {original.seconds:R}s; ") +
                        FormattableString.Invariant($"gap={seek.maxGapErrorM:R}m closest={seek.maxClosestPointErrorM:R}m ") +
                        FormattableString.Invariant($"anchors/feet={seek.maxAnchorErrorM:R}m floor={seek.maxFloorErrorM:R}m; limit=0.001m."));
            }
        }

        static void KCRender(FrankBattlePairPlayback pair, CharacterCombat[] fighters,
            CharacterCombat source, KCCase record, KCReport report, Bounds bounds)
        {
            var times = new List<float> { KCStart, KCEnd };
            foreach (float observed in new[] { 1.3958f, 1.6042f, 1.6667f })
            foreach (float delta in new[] { -1f / 30, 0, 1f / 30 })
                times.Add(record.samples.OrderBy(f => Mathf.Abs(f.seconds - observed - delta)).First().seconds);
            times.Add(record.samples.OrderBy(f => Mathf.Abs(f.seconds - 1.75f)).First().seconds);
            foreach (var candidate in record.candidates.Where(c => c.kind == "globalMinimum"))
                times.AddRange(new[] { candidate.previousSeconds, candidate.seconds, candidate.nextSeconds });
            record.imageTimes = times.Distinct().OrderBy(t => t).ToArray();
            bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
            bounds.Expand(.55f);
            using var rendering = new GameplayPairRendering(source.gameObject.scene, fighters, pair);
            for (int index = 0; index < record.imageTimes.Length; index += 12)
            {
                string name = record.file + "_Sheet" + (index / 12 + 1).ToString("D2") + ".png";
                rendering.Write(Path.Combine(KCOutput, name), bounds,
                    record.imageTimes.Skip(index).Take(12).ToArray(), seconds =>
                    {
                        pair.EvaluateAt(seconds);
                        CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
                    });
                record.sheets.Add(name);
                KCFlush(report);
            }
        }
    }
}
