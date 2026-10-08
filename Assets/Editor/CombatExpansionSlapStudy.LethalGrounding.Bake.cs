using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        [MenuItem("Tools/Combat Expansion/Slap/Bake Candidate Grounding")]
        public static void BakeCandidateGrounding()
        {
            var report = LgPrepare("Bake", out var guards);
            var pending = new List<FrankPairGrounding>();
            try
            {
                foreach (var phase in report.phases)
                {
                    using var session = new SourceSession();
                    InitializeFaceGroundingFighters(session.Fighters);
                    CheckGroundingFighters(session.Fighters);
                    var native = LgLoadNative(phase.sequence, session.Fighters);
                    var tracks = new List<FrankPairGrounding.Track>();
                    for (int assignment = 0; assignment < 2; assignment++)
                        tracks.AddRange(LgBakeAssignment(session.Fighters, report, native, phase, assignment));
                    var asset = ScriptableObject.CreateInstance<FrankPairGrounding>();
                    asset.name = Path.GetFileNameWithoutExtension(LgPath(phase.sequence));
                    asset.tracks = tracks.ToArray();
                    pending.Add(asset);
                }
                if (report.tracks.Count != 8 || report.tracks.Any(t => t.floorViolations > 0 ||
                    t.prefixViolations > 0 || t.maximumAuthoredSpeed > LgMaximumSpeed ||
                    t.maximumAuthoredAcceleration > LgMaximumAcceleration))
                    throw new InvalidOperationException("Measured bake gates failed; inspect track CSV/JSON. " +
                        "Prefix contact has not been moved and no grounding asset has been saved.");
                // All output paths are checked before the first write. No native asset is a target.
                foreach (var phase in report.phases)
                    LgPreflightTarget(phase.sequence);
                LgAssertFiles(report.guardedFiles);
                EnsureGroundingFolder(LgAssets);
                for (int index = 0; index < pending.Count; index++)
                {
                    string path = LgPath(index + 1);
                    var saved = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(path);
                    if (saved)
                    {
                        EditorUtility.CopySerialized(pending[index], saved);
                        EditorUtility.SetDirty(saved);
                    }
                    else
                    {
                        AssetDatabase.CreateAsset(pending[index], path);
                        saved = pending[index];
                    }
                    AssetDatabase.SaveAssetIfDirty(saved);
                }
                File.WriteAllText(LgOutput + "/GroundingAssets.json", JsonUtility.ToJson(new LgFiles
                {
                    files = new[] { LgSnapshot(LgPath(1)), LgSnapshot(LgPath(2)) }
                }, true));
                report.status = "BAKED_PENDING_INDEPENDENT_VALIDATION";
            }
            catch (Exception error)
            {
                report.status = "FAILED_BAKE";
                report.failure = error.ToString();
                throw;
            }
            finally
            {
                foreach (var asset in pending)
                    if (asset && !EditorUtility.IsPersistent(asset))
                        Object.DestroyImmediate(asset);
                LgFinish(report, guards);
            }
        }

        [Serializable]
        sealed class LgFiles
        {
            public LgFile[] files;
        }

        static void LgPreflightTarget(int sequence)
        {
            string path = LgPath(sequence);
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if ((File.Exists(path) && !asset) || (asset && !(asset is FrankPairGrounding)) ||
                (asset && EditorUtility.IsDirty(asset)))
                throw new InvalidOperationException("Unsafe or dirty project-owned grounding target: " + path);
        }

        static FrankPairGrounding.Track[] LgBakeAssignment(CharacterCombat[] fighters, LgReport report,
            FrankPairGrounding native, LgPhase phase, int assignment)
        {
            var actors = new[] { fighters[assignment], fighters[1 - assignment] };
            var originals = actors.Select((f, role) => LgTrackFor(native, f, role == 1)).ToArray();
            int count = originals[1].lift.Length;
            float duration = originals[1].duration;
            if (originals.Any(t => t.lift.Length != count || Mathf.Abs(t.duration - duration) > .0001f))
                throw new InvalidOperationException("Native grounding grids must agree to preserve interpolation.");
            float dt = duration / (count - 1);
            phase.transitionStart = Mathf.Ceil(phase.preserve / dt) * dt;
            phase.transitionEnd = phase.transitionStart + .22f;
            var raw = new[] { Enumerable.Repeat(float.MaxValue, count).ToArray(),
                Enumerable.Repeat(float.MaxValue, count).ToArray() };
            string stem = "Sequence" + phase.sequence + "_" + actors[0].name + "_" + actors[1].name;
            var csv = new StringBuilder("direction,role,seconds,rawBodyY,nativeLift\n");
            try
            {
                foreach (int direction in new[] { 1, -1 })
                {
                    var pair = LgBegin(fighters, report.sources, phase.sequence, assignment, direction, null, true);
                    try
                    {
                        if (Mathf.Abs(pair.Duration - duration) > .0001f)
                            throw new InvalidOperationException("Native grounding duration differs from full pair.");
                        for (int i = 0; i < count; i++)
                        {
                            float seconds = dt * i;
                            pair.EvaluateAt(seconds);
                            CheckCandidateEquipment(pair, fighters);
                            for (int role = 0; role < 2; role++)
                            {
                                float y = GroundingClearance(actors[role]);
                                raw[role][i] = Mathf.Min(raw[role][i], y);
                                csv.AppendLine(FormattableString.Invariant(
                                    $"{direction},{role},{seconds:R},{y:R},{originals[role].At(seconds):R}"));
                            }
                        }
                    }
                    finally
                    {
                        pair.Cancel();
                    }
                }
            }
            finally
            {
                File.WriteAllText(LgOutput + "/" + stem + ".RawBake.csv", csv.ToString());
            }
            var result = new FrankPairGrounding.Track[2];
            for (int role = 0; role < 2; role++)
            {
                var track = new FrankPairGrounding.Track
                {
                    avatar = originals[role].avatar,
                    receiver = role == 1,
                    duration = duration,
                    lift = (float[])originals[role].lift.Clone()
                };
                if (role == 1)
                    track.lift = LgEnvelope(raw[role], originals[role], phase);
                var evidence = LgDescribeTrack(track, originals[role], raw[role], phase, actors[role].name);
                report.tracks.Add(evidence);
                LgWriteTrack(stem, role, track, originals[role], raw[role], phase);
                result[role] = track;
            }
            LgWriteReport(report);
            return result;
        }
    }
}
