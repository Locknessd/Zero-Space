using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        [MenuItem("Tools/Combat Expansion/Slap/Validate Candidate Grounding")]
        public static void ValidateCandidateGrounding() => LgRunStudy(false);

        [MenuItem("Tools/Combat Expansion/Slap/Capture Grounded Candidates")]
        public static void CaptureGroundedCandidates() => LgRunStudy(true);

        static void LgRunStudy(bool capture)
        {
            var report = LgPrepare(capture ? "Capture" : "Validation", out var guards);
            try
            {
                var bake = JsonUtility.FromJson<LgReport>(File.ReadAllText(LgOutput + "/Bake.json"));
                if (bake.status != "BAKED_PENDING_INDEPENDENT_VALIDATION" || !bake.sourceGuardsPassed)
                    throw new InvalidOperationException("A complete guarded bake is required.");
                LgAssertFiles(bake.guardedFiles, false);
                var files = JsonUtility.FromJson<LgFiles>(File.ReadAllText(LgOutput + "/GroundingAssets.json"));
                LgAssertFiles(files.files, false);
                var outputGuards = files.files.Select(f => new LethalAssetGuard(f.path)).ToArray();
                report.tracks = bake.tracks;
                foreach (var phase in bake.phases)
                    if (FileHash(phase.timeMap) != phase.timeMapHash)
                        throw new InvalidOperationException("Authored time map changed since grounding bake.");
                report.phases = bake.phases;
                try
                {
                    foreach (var phase in report.phases)
                    for (int assignment = 0; assignment < 2; assignment++)
                    foreach (int direction in new[] { 1, -1 })
                    {
                        using var session = new SourceSession();
                        InitializeFaceGroundingFighters(session.Fighters);
                        CheckGroundingFighters(session.Fighters);
                        var evidence = new LgCase { direction = direction };
                        report.cases.Add(evidence);
                        try
                        {
                            LgMeasureCase(session.Fighters, report.sources, phase, assignment, capture, evidence);
                        }
                        catch (Exception error)
                        {
                            evidence.status = "FAILED_CASE";
                            evidence.failure = error.ToString();
                        }
                        LgWriteReport(report);
                    }
                }
                finally
                {
                    foreach (var guard in outputGuards)
                        guard.AssertUnchanged();
                    LgAssertFiles(files.files, false);
                }
                if (report.cases.Count != 8 || report.cases.Any(c => !c.geometryGatesPassed))
                    throw new InvalidOperationException("One or more of eight cases failed geometry gates. " +
                        "All completed evidence is retained; inspect per-case failures and metrics.");
                report.status = capture ? "CAPTURED_PROVISIONAL_SUPPORT_REVIEW_REQUIRED" :
                    "VALIDATED_GEOMETRY_ONLY_SUPPORT_REVIEW_REQUIRED";
            }
            catch (Exception error)
            {
                report.status = "FAILED_" + report.operation.ToUpperInvariant();
                report.failure = error.ToString();
                throw;
            }
            finally
            {
                LgFinish(report, guards);
            }
        }

        static float[] LgTimes(float duration, LgPhase phase, bool capture)
        {
            int rate = capture ? 61 : 361;
            int intervals = GroundingIntervals(duration, rate);
            // Interior half-step grid is independent of native 240Hz bake knots.
            var full = Enumerable.Range(0, intervals).Select(i => duration * (i + .5f) / intervals);
            float start = Mathf.Max(0, phase.anchor - .20f);
            int contactCount = Mathf.CeilToInt((phase.preserve - start) * 241);
            var contact = Enumerable.Range(0, contactCount + 1)
                .Select(i => Mathf.Min(phase.preserve, start + i / 241f));
            return full.Concat(contact).Concat(LgInitialSheets(duration, phase))
                .Append(0).Append(duration).Distinct().OrderBy(t => t).ToArray();
        }

        static float[] LgInitialSheets(float duration, LgPhase phase)
        {
            var prefix = Enumerable.Range(0, 7).Select(i => phase.anchor - .15f + i * .05f);
            var transition = Enumerable.Range(0, 7)
                .Select(i => Mathf.Lerp(phase.preserve, phase.transitionEnd, i / 6f));
            var fall = Enumerable.Range(0, 13).Select(i => Mathf.Lerp(phase.transitionEnd, phase.hold, i / 12f));
            return prefix.Concat(transition).Concat(fall)
                .Concat(new[] { 0, .12f, phase.preserve, phase.hold, (phase.hold + duration) * .5f, duration })
                .Where(t => t >= 0 && t <= duration).Distinct().OrderBy(t => t).ToArray();
        }

        static bool LgContactTime(float seconds, LgPhase phase) =>
            seconds >= phase.anchor - .20f && seconds <= phase.preserve;

        static List<(string role, string rig, string bone, Transform node)> LgNodes(
            FrankBattlePairPlayback pair, CharacterCombat source, CharacterCombat target)
        {
            var nodes = CandidateNodes(pair, source, target);
            var animators = new[] { pair.AttackerActor.Pose.driver, source.Animator,
                pair.ReceiverActor.Pose.driver, target.Animator };
            for (int actor = 0; actor < animators.Length; actor++)
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone < HumanBodyBones.LeftThumbProximal || bone >= HumanBodyBones.LastBone)
                    continue;
                var node = animators[actor].GetBoneTransform(bone);
                if (node && !nodes.Any(n => n.node == node))
                    nodes.Add((actor < 2 ? "giver" : "receiver", actor % 2 == 0 ? "native" : "fighter",
                        bone.ToString(), node));
            }
            return nodes;
        }

        static LgPose LgPoseOnly(List<(string role, string rig, string bone, Transform node)> nodes)
        {
            return new LgPose
            {
                positions = LethalPositions(nodes),
                rotations = nodes.Select(n => n.node.rotation).ToArray()
            };
        }

        static Dictionary<float, LgPose> LgNativePrefix(CharacterCombat[] fighters, SourceRecord[] sources,
            LgPhase phase, int assignment, int direction, float[] times, LgCase evidence)
        {
            var native = LgLoadNative(phase.sequence, fighters);
            var pair = LgBegin(fighters, sources, phase.sequence, assignment, direction, native, false);
            var result = new Dictionary<float, LgPose>();
            try
            {
                var source = fighters[assignment];
                var target = fighters[1 - assignment];
                var nodes = LgNodes(pair, source, target);
                using var face = new CombatExpansionAxeDenseStudy.SkinRegionProbe(source,
                    HumanBodyBones.RightHand, target, HumanBodyBones.Head);
                var diagnostics = new List<string>();
                foreach (float time in times.Where(t => t <= phase.preserve))
                {
                    pair.EvaluateAt(time);
                    var pose = LgPoseOnly(nodes);
                    if (LgContactTime(time, phase))
                    {
                        diagnostics.Clear();
                        pose.contactGap = face.Measure(diagnostics).gap;
                        if (pose.contactGap < evidence.nativeMinimumContactGap)
                        {
                            evidence.nativeMinimumContactGap = pose.contactGap;
                            evidence.nativeContactSeconds = time;
                        }
                    }
                    result.Add(time, pose);
                }
            }
            finally
            {
                pair.Cancel();
            }
            return result;
        }
    }
}
