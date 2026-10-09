using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string Execution01GroundingPath =
            "Assets/CombatExpansion/SamuraiStudy/Grounding/Samurai_Execution01_Grounding.asset";
        const int GroundingBakeRate = 240;
        const int GroundingValidationRate = 361;
        const float GroundingMaximumLift = .3f;
        const float GroundingCushion = .01f;
        const float GroundingMinimumClearance = -.001f;

        public static void BakeExecution01Grounding()
        {
            BakeExecutionGrounding(1);
        }

        static void BakeExecutionGrounding(int execution)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            string executionName = GroundingExecutionName(execution);
            string assetPath = GroundingAssetPath(execution);
            var rows = new StringBuilder("fighter,avatar,direction,role,seconds,clearance,baseLift,storedLift\n");
            string failure = null;
            float maximumLift = 0;
            try
            {
                var existing = AssetDatabase.LoadMainAssetAtPath(assetPath);
                if ((existing && !(existing is FrankPairGrounding)) ||
                    (!existing && File.Exists(assetPath)))
                    throw new InvalidOperationException("Unexpected asset type at " + assetPath);
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                CheckGroundingFighters(session.Fighters);
                var tracks = new List<FrankPairGrounding.Track>();
                foreach (var source in session.Fighters)
                {
                    var target = session.Fighters.Single(fighter => fighter != source);
                    var pair = BeginGroundingStudy(session.Fighters, source, target, 1, null, execution);
                    try
                    {
                        var sampled = BakeGroundingPair(pair, source, target, rows, execution);
                        maximumLift = Mathf.Max(maximumLift, sampled.Max(track => track.lift.Max()));
                        tracks.AddRange(sampled);
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
                CheckGroundingTracks(tracks.ToArray(), session.Fighters);
                EnsureGroundingFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
                var asset = existing as FrankPairGrounding;
                if (!asset)
                {
                    asset = ScriptableObject.CreateInstance<FrankPairGrounding>();
                    asset.tracks = tracks.ToArray();
                    AssetDatabase.CreateAsset(asset, assetPath);
                }
                else
                {
                    asset.tracks = tracks.ToArray();
                    EditorUtility.SetDirty(asset);
                }
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            catch (Exception error)
            {
                failure = error.ToString();
                throw;
            }
            finally
            {
                WriteGroundingReport(executionName + "Grounding.csv", rows.ToString());
                WriteGroundingReport(executionName + "GroundingBake.txt",
                    (failure == null ? "BAKED\n" : "FAIL\n") +
                    executionName + "; both avatars in both roles; positive lane direction; endpoints included.\n" +
                    FormattableString.Invariant($"Minimum rate={GroundingBakeRate} Hz; cushion={GroundingCushion:R} m; ") +
                    FormattableString.Invariant($"maximum stored lift={maximumLift:R} m; limit={GroundingMaximumLift:R} m.\n") +
                    "Execution06/10 Mankey receiver uses a measured 0.005m cushion; all other tracks retain 0.01m.\n" +
                    "Correction bound and independent floor-clearance requirement are unchanged.\n" +
                    "One adjacent sample maximum envelope; original samples are never modified in place.\n" +
                    "Visible body clearance uses existing evaluated rendered skin geometry against world Y=0.\n" +
                    "Independent validation is required; bake is not contact, recovery or gameplay approval.\n" +
                    (failure ?? "Asset=" + assetPath));
            }
        }

        static FrankPairGrounding.Track[] BakeGroundingPair(FrankBattlePairPlayback pair,
            CharacterCombat source, CharacterCombat target, StringBuilder rows, int execution)
        {
            int intervals = GroundingIntervals(pair.Duration, GroundingBakeRate);
            var actors = new[] { source, target };
            var tracks = actors.Select((actor, role) => new FrankPairGrounding.Track
            {
                avatar = actor.Animator.avatar,
                receiver = role == 1,
                duration = pair.Duration,
                lift = new float[intervals + 1]
            }).ToArray();
            var clearances = actors.Select(actor => new float[intervals + 1]).ToArray();
            for (int frame = 0; frame <= intervals; frame++)
            {
                float seconds = pair.Duration * frame / intervals;
                pair.EvaluateAt(seconds);
                for (int role = 0; role < actors.Length; role++)
                {
                    float clearance = GroundingClearance(actors[role]);
                    clearances[role][frame] = clearance;
                    float cushion = GroundingCushionFor(execution, actors[role], role == 1);
                    tracks[role].lift[frame] = Mathf.Max(0, cushion - clearance);
                }
            }
            for (int role = 0; role < actors.Length; role++)
            {
                var original = tracks[role].lift;
                var stored = new float[original.Length];
                for (int frame = 0; frame <= intervals; frame++)
                {
                    stored[frame] = Mathf.Max(original[frame], Mathf.Max(original[Mathf.Max(0, frame - 1)],
                        original[Mathf.Min(intervals, frame + 1)]));
                    float seconds = pair.Duration * frame / intervals;
                    rows.AppendLine(GroundingCsv(actors[role].name) + "," +
                        GroundingCsv(AssetDatabase.GetAssetPath(tracks[role].avatar)) + ",1," + GroundingRole(role) +
                        FormattableString.Invariant($",{seconds:R},{clearances[role][frame]:R},") +
                        FormattableString.Invariant($"{original[frame]:R},{stored[frame]:R}"));
                }
                tracks[role].lift = stored;
            }
            return tracks;
        }

        static void EnsureGroundingFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureGroundingFolder(parent);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))))
                throw new InvalidOperationException("Cannot create grounding asset folder " + path);
        }
    }
}
