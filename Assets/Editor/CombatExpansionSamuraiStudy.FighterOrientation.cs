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
        public static void BakeExecution02FighterOrientationGrounding()
        {
            FORun("Bake", FOBake);
        }

        public static void ValidateExecution02FighterOrientationGrounding()
        {
            FORun("Validation", (clips, sources, output) =>
            {
                using (var session = new SourceSession())
                {
                    InitializePairStudy(session.Fighters);
                    FOLoadGrounding(clips, session.Fighters);
                }
                ValidateExecutionGrounding(2, FOGroundingPath, output, clips[0], clips[1]);
            });
        }

        public static void CaptureExecution02FighterOrientationContacts()
        {
            FORun("Contacts", FOCapture);
        }

        static void FOBake(AnimationClip[] clips, SourceRecord[] sources, string output)
        {
            var rows = new StringBuilder("fighter,avatar,direction,role,seconds,clearance,baseLift,storedLift\n");
            string failure = null;
            float maximumLift = 0;
            try
            {
                var existing = AssetDatabase.LoadMainAssetAtPath(FOGroundingPath);
                if ((existing && !(existing is FrankPairGrounding)) ||
                    (!existing && File.Exists(FOGroundingPath)))
                    throw new InvalidOperationException("Unexpected asset at " + FOGroundingPath);
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                CheckGroundingFighters(session.Fighters);
                var tracks = new List<FrankPairGrounding.Track>();
                foreach (var source in session.Fighters)
                {
                    var target = session.Fighters.Single(fighter => fighter != source);
                    var pair = BeginGroundingStudy(session.Fighters, source, target, 1, null, 2, clips[0], clips[1]);
                    try
                    {
                        var sampled = BakeGroundingPair(pair, source, target, rows, 2);
                        maximumLift = Mathf.Max(maximumLift, sampled.Max(track => track.lift.Max()));
                        tracks.AddRange(sampled);
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
                CheckGroundingTracks(tracks.ToArray(), session.Fighters);
                EnsureGroundingFolder(Path.GetDirectoryName(FOGroundingPath).Replace('\\', '/'));
                var asset = existing as FrankPairGrounding;
                if (!asset)
                {
                    asset = ScriptableObject.CreateInstance<FrankPairGrounding>();
                    asset.tracks = tracks.ToArray();
                    AssetDatabase.CreateAsset(asset, FOGroundingPath);
                }
                else
                {
                    asset.tracks = tracks.ToArray();
                    EditorUtility.SetDirty(asset);
                }
                AssetDatabase.SaveAssetIfDirty(asset);
                File.WriteAllText(FOBindingPath, JsonUtility.ToJson(FOBind(asset, clips, session.Fighters), true));
            }
            catch (Exception error)
            {
                failure = error.ToString();
                throw;
            }
            finally
            {
                File.WriteAllText(output + "/Execution02Grounding.csv", rows.ToString());
                File.WriteAllText(output + "/Execution02GroundingBake.txt",
                    (failure == null ? "BAKED\n" : "FAIL; partial evidence only\n") + FOScope + "\n" +
                    "Both fighter assignments; four avatar/role tracks; positive lane; endpoints included.\n" +
                    FormattableString.Invariant($"Rate={GroundingBakeRate}Hz; cushion={GroundingCushion:R}m; ") +
                    FormattableString.Invariant($"maximum lift={maximumLift:R}m; cap={GroundingMaximumLift:R}m.\n") +
                    "Unmodified BakeGroundingPair with adjacent sample maximum envelope.\n" +
                    "Independent 361Hz validation in both lanes is required.\n" + (failure ?? FOGroundingPath));
            }
        }

        static void FOCapture(AnimationClip[] clips, SourceRecord[] sources, string output)
        {
            var report = new CCReport { execution = 2, sources = sources, scope = FOScope + CCScope };
            CCFlush(output, report);
            try
            {
                // Revalidate from a new playback on the independent full-duration grid before contact capture.
                ValidateExecution02FighterOrientationGrounding();
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                var grounding = FOLoadGrounding(clips, session.Fighters);
                report.grounding = CombatExpansionInventory.Identity(grounding);
                foreach (var source in session.Fighters)
                foreach (int direction in new[] { 1, -1 })
                {
                    var record = new CCCase { grounding = report.grounding };
                    report.cases.Add(record);
                    CCFlush(output, report);
                    CCCapturePair(session.Fighters, source, direction, 2, grounding, output, record,
                        clips[0], clips[1]);
                    CCFlush(output, report);
                }
                report.status = "CAPTURED four variant fighter cases; candidates/recovery/gameplay unapproved.";
            }
            catch (Exception error)
            {
                report.status = "FAILED; partial variant evidence is not a completed capture.";
                report.error = error.ToString();
                throw;
            }
            finally
            {
                CCFlush(output, report);
            }
        }
    }
}
