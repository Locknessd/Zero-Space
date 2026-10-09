using System;
using System.Linq;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        public static void ValidateExecution01Grounding()
        {
            ValidateExecutionGrounding(1);
        }

        static void ValidateExecutionGrounding(int execution)
        {
            ValidateExecutionGrounding(execution, null, null, null, null);
        }

        static void ValidateExecutionGrounding(int execution, string assetPathOverride,
            string reportOutput, AnimationClip attackOverride, AnimationClip reactionOverride)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            string executionName = GroundingExecutionName(execution);
            string assetPath = assetPathOverride ?? GroundingAssetPath(execution);
            var rows = new StringBuilder("source,target,direction,fighter,role,minimumClearance,seconds," +
                "samples,maximumCorrection,violations\n");
            var failures = new StringBuilder();
            float worst = float.PositiveInfinity;
            float maximumCorrection = 0;
            int pairCases = 0;
            int actorCases = 0;
            long evaluations = 0;
            long measurements = 0;
            long violations = 0;
            string failure = null;
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(assetPath);
                if (!asset)
                    throw new InvalidOperationException("Missing grounding asset: " + assetPath);
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                CheckGroundingFighters(session.Fighters);
                CheckGroundingTracks(asset.tracks, session.Fighters);
                foreach (var source in session.Fighters)
                foreach (int direction in new[] { 1, -1 })
                {
                    var target = session.Fighters.Single(fighter => fighter != source);
                    var pair = BeginGroundingStudy(session.Fighters, source, target, direction, asset, execution,
                        attackOverride, reactionOverride);
                    try
                    {
                        CCValidatePair(pair, asset, execution, attackOverride, reactionOverride);
                        foreach (var track in asset.tracks)
                            if (Mathf.Abs(track.duration - pair.Duration) > .0001f)
                                throw new InvalidOperationException("Stale Samurai grounding track duration.");
                        int intervals = GroundingIntervals(pair.Duration, GroundingValidationRate);
                        var actors = new[] { source, target };
                        var minima = new[] { float.PositiveInfinity, float.PositiveInfinity };
                        var times = new float[2];
                        var corrections = new float[2];
                        var counts = new int[2];
                        var tracks = actors.Select((actor, role) => asset.tracks.Single(track =>
                            track.avatar == actor.Animator.avatar && track.receiver == (role == 1))).ToArray();
                        for (int frame = 0; frame <= intervals; frame++)
                        {
                            float seconds = pair.Duration * frame / intervals;
                            pair.EvaluateAt(seconds);
                            evaluations++;
                            for (int role = 0; role < actors.Length; role++)
                            {
                                float clearance = GroundingClearance(actors[role]);
                                measurements++;
                                worst = Mathf.Min(worst, clearance);
                                corrections[role] = Mathf.Max(corrections[role], tracks[role].At(seconds));
                                maximumCorrection = Mathf.Max(maximumCorrection, corrections[role]);
                                if (clearance < minima[role])
                                {
                                    minima[role] = clearance;
                                    times[role] = seconds;
                                }
                                if (clearance < GroundingMinimumClearance)
                                {
                                    counts[role]++;
                                    violations++;
                                }
                            }
                        }
                        for (int role = 0; role < actors.Length; role++)
                        {
                            rows.AppendLine(GroundingCsv(source.name) + "," + GroundingCsv(target.name) + "," +
                                direction + "," + GroundingCsv(actors[role].name) + "," + GroundingRole(role) +
                                FormattableString.Invariant($",{minima[role]:R},{times[role]:R},{intervals + 1},") +
                                FormattableString.Invariant($"{corrections[role]:R},{counts[role]}"));
                            if (counts[role] > 0)
                                failures.AppendLine(FormattableString.Invariant(
                                    $"{source.name}->{target.name} direction={direction} {GroundingRole(role)}: ") +
                                    FormattableString.Invariant($"minimum={minima[role]:R} m at {times[role]:R}s; ") +
                                    FormattableString.Invariant($"violations={counts[role]}."));
                            actorCases++;
                        }
                        pairCases++;
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
                if (pairCases != 4 || actorCases != 8 || measurements != evaluations * 2)
                    throw new InvalidOperationException("Incomplete Samurai grounding coverage.");
                if (!float.IsFinite(worst) || worst < GroundingMinimumClearance || violations > 0)
                    throw new InvalidOperationException(FormattableString.Invariant(
                        $"Sampled clearance below {GroundingMinimumClearance:R} m: worst={worst:R} m.\n") + failures);
            }
            catch (Exception error)
            {
                failure = error.ToString();
                throw;
            }
            finally
            {
                var summary = new StringBuilder(failure == null ? "PASS\n" : "FAIL\n");
                summary.AppendLine(executionName + "; both assignments and both lane directions; actual source playback.");
                summary.AppendLine(FormattableString.Invariant(
                    $"Pair cases={pairCases}/4; actor cases={actorCases}/8; pair evaluations={evaluations}; ") +
                    FormattableString.Invariant($"clearance measurements={measurements}; violations={violations}."));
                summary.AppendLine(FormattableString.Invariant(
                    $"Independent grid minimum rate={GroundingValidationRate} Hz; endpoints included; ") +
                    FormattableString.Invariant($"acceptance clearance >= {GroundingMinimumClearance:R} m."));
                summary.AppendLine(FormattableString.Invariant(
                    $"Worst clearance={worst:R} m; maximum evaluated correction={maximumCorrection:R} m."));
                summary.AppendLine("Measures visible evaluated body skins against world Y=0 using existing geometry helper.");
                summary.AppendLine("Sampled grounding only; no contact, recovery or gameplay approval.");
                if (failure != null)
                    summary.AppendLine(failure);
                summary.Append(rows);
                if (reportOutput == null)
                    WriteGroundingReport(executionName + "GroundingValidation.txt", summary.ToString());
                else
                {
                    Directory.CreateDirectory(reportOutput);
                    File.WriteAllText(Path.Combine(reportOutput, executionName + "GroundingValidation.txt"),
                        summary.ToString());
                }
            }
        }
    }
}
