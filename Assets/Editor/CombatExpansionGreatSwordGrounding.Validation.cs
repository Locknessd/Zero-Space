using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordGrounding
    {
        public static void Validate()
        {
            var rows = new StringBuilder("fighter,action,direction,role,minimumClearance,seconds,samples\n");
            float worst = float.PositiveInfinity;
            int cases = 0;
            int actorCases = 0;
            long evaluations = 0;
            long measurements = 0;
            string failure = null;
            try
            {
                WithFighters(fighters =>
                {
                    // Preflight every required asset before sampling any case.
                    var assets = new FrankPairGrounding[4];
                    for (int index = 0; index < assets.Length; index++)
                    {
                        var move = CombatExpansionGreatSwordStudy.MakeMove(fighters[0], fighters[1], index);
                        string path = AssetPath(move.moveName);
                        assets[index] = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(path);
                        if (!assets[index])
                            throw new InvalidOperationException("Missing required grounding asset: " + path);
                        CheckTracks(assets[index], fighters);
                    }
                    foreach (var source in fighters)
                    for (int index = 0; index < assets.Length; index++)
                    foreach (int direction in new[] { 1, -1 })
                    {
                        var target = fighters.Single(f => f != source);
                        Reset(fighters);
                        var move = CombatExpansionGreatSwordStudy.MakeMove(source, target, index);
                        move.grounding = assets[index];
                        Sample(source, target, move, direction, pair =>
                        {
                            foreach (var track in move.grounding.tracks)
                                if (Mathf.Abs(track.duration - pair.Duration) > .0001f)
                                    throw new InvalidOperationException("Stale grounding duration: " + move.moveName);
                            int intervals = Intervals(pair.Duration, ValidationRate);
                            var actors = new[] { source, target };
                            var minima = new[] { float.PositiveInfinity, float.PositiveInfinity };
                            var times = new float[2];
                            for (int frame = 0; frame <= intervals; frame++)
                            {
                                float seconds = pair.Duration * frame / intervals;
                                pair.EvaluateAt(seconds);
                                evaluations++;
                                for (int role = 0; role < actors.Length; role++)
                                {
                                    float clearance = Clearance(actors[role]);
                                    measurements++;
                                    worst = Mathf.Min(worst, clearance);
                                    if (clearance < minima[role])
                                    {
                                        minima[role] = clearance;
                                        times[role] = seconds;
                                    }
                                }
                            }
                            for (int role = 0; role < actors.Length; role++)
                            {
                                rows.AppendLine(FormattableString.Invariant(
                                    $"{Csv(actors[role].name)},{Csv(move.moveName)},{direction},{Role(role)},") +
                                    FormattableString.Invariant($"{minima[role]:R},{times[role]:R},{intervals + 1}"));
                                actorCases++;
                            }
                            cases++;
                        });
                    }
                });
                if (cases != 16 || actorCases != 32 || measurements != evaluations * 2)
                    throw new InvalidOperationException("Incomplete sampled grounding coverage.");
                if (!float.IsFinite(worst) || worst < -.025f)
                    throw new InvalidOperationException("Ground clearance below -0.025 m: " + worst);
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
                throw;
            }
            finally
            {
                var summary = new StringBuilder(failure == null ? "PASS\n" : "FAIL\n");
                summary.AppendLine(FormattableString.Invariant(
                    $"Completed pair cases: {cases}; actor cases: {actorCases}; ") +
                    FormattableString.Invariant(
                        $"pair evaluations: {evaluations}; clearance measurements: {measurements}."));
                summary.AppendLine(FormattableString.Invariant(
                    $"Sampling: {ValidationRate} Hz minimum, including endpoints; worst clearance: {worst:R} m."));
                summary.AppendLine("Four GreatSword pairs, both BattleScene avatars and both directions required.");
                summary.AppendLine("Sampled grounding only; this is not contact or gameplay approval.");
                if (failure != null)
                    summary.AppendLine(failure);
                summary.Append(rows);
                WriteReport("GreatSwordGroundingValidation.txt", summary.ToString());
            }
        }

        static void CheckTracks(FrankPairGrounding asset, CharacterCombat[] fighters)
        {
            if (asset.tracks == null || asset.tracks.Length != 4 || asset.tracks.Any(t => t == null))
                throw new InvalidOperationException("Expected four tracks in " + asset.name);
            foreach (var fighter in fighters)
            foreach (bool receiver in new[] { false, true })
            {
                var matches = asset.tracks.Where(t => t.avatar == fighter.Animator.avatar && t.receiver == receiver)
                    .ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException("Missing or duplicate avatar/role track in " + asset.name);
                var track = matches[0];
                int intervals = Intervals(track.duration, BakeRate);
                if (track.lift == null || track.lift.Length != intervals + 1 ||
                    track.lift.Any(lift => !float.IsFinite(lift) || lift < 0 || lift > MaximumLift))
                    throw new InvalidOperationException("Invalid lift samples in " + asset.name);
            }
        }
    }
}
