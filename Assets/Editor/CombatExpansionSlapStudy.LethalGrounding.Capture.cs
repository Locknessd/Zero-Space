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
        static void LgMeasureCase(CharacterCombat[] fighters, SourceRecord[] sources, LgPhase phase,
            int assignment, bool capture, LgCase evidence)
        {
            var source = fighters[assignment];
            var target = fighters[1 - assignment];
            var grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(LgPath(phase.sequence));
            if (!grounding || grounding.tracks.Length != 4)
                throw new InvalidOperationException("Missing complete candidate grounding.");
            float duration = LgTrackFor(grounding, target, true).duration;
            string stem = (capture ? "Capture" : "Validation") + "_Sequence" + phase.sequence + "_" +
                source.name + "_" + target.name + "_" + (evidence.direction > 0 ? "Positive" : "Negative");
            var times = LgTimes(duration, phase, capture);
            var native = LgNativePrefix(fighters, sources, phase, assignment, evidence.direction, times, evidence);
            var pair = LgBegin(fighters, sources, phase.sequence, assignment, evidence.direction, grounding, true);
            try
            {
                var record = new CandidateRecord
                {
                    name = stem,
                    status = "PROVISIONAL_GROUNDED_LETHAL",
                    sequence = phase.sequence,
                    attacker = source.name,
                    receiver = target.name,
                    receiverYaw = 180,
                    spacing = .8f,
                    lane = Vector3.right * evidence.direction,
                    receiverOffset = pair.Move.sourcePair.receiverOffset,
                    initialAttackerPosition = source.Animator.transform.position,
                    initialReceiverPosition = target.Animator.transform.position,
                    initialAttackerRotation = source.Animator.transform.rotation,
                    initialReceiverRotation = target.Animator.transform.rotation,
                    duration = pair.Duration,
                    sheetSeconds = LgInitialSheets(duration, phase)
                };
                evidence.pair = record;
                if (Mathf.Abs(duration - pair.Duration) > .0001f)
                    throw new InvalidOperationException("Stored grounding duration changed.");
                var nodes = LgNodes(pair, source, target);
                using var aSupport = new CombatExpansionAxeDenseStudy.LethalSupportProbe(source);
                using var bSupport = new CombatExpansionAxeDenseStudy.LethalSupportProbe(target);
                using var face = new CombatExpansionAxeDenseStudy.SkinRegionProbe(source,
                    HumanBodyBones.RightHand, target, HumanBodyBones.Head);
                evidence.supportSelection = aSupport.SelectionSummary + "\n" + bSupport.SelectionSummary +
                    "\n" + face.SelectionSummary;
                using var metrics = new StreamWriter(LgOutput + "/" + stem + ".Metrics.csv");
                using var trajectories = new StreamWriter(LgOutput + "/" + stem + ".Trajectory.csv");
                using var contacts = new StreamWriter(LgOutput + "/" + stem + ".Contact.csv");
                evidence.metrics = stem + ".Metrics.csv";
                evidence.trajectory = stem + ".Trajectory.csv";
                evidence.contactRows = stem + ".Contact.csv";
                metrics.WriteLine(LgMetricsHeader());
                trajectories.WriteLine("seconds,sourceClock,role,rig,bone,x,y,z,qx,qy,qz,qw");
                contacts.WriteLine("seconds,nativeGap,candidateGap,handX,handY,handZ,faceX,faceY,faceZ");
                var samples = new Dictionary<float, LgPose>();
                var bounds = new Bounds(source.Animator.transform.position, Vector3.zero);
                var diagnostics = new List<string>();
                foreach (float time in times)
                {
                    pair.EvaluateAt(time);
                    CheckCandidateEquipment(pair, fighters);
                    var pose = LgReadPose(nodes, aSupport, bSupport);
                    if (LgContactTime(time, phase))
                    {
                        diagnostics.Clear();
                        var contact = face.Measure(diagnostics);
                        pose.contactGap = contact.gap;
                        LgContactRow(contacts, time, native[time], contact, evidence);
                    }
                    if (native.TryGetValue(time, out var expected))
                        LgComparePrefix(pose, expected, evidence);
                    samples.Add(time, pose);
                    LgWriteMetrics(metrics, time, pose, grounding, source, target, evidence, phase);
                    LgWriteTrajectory(trajectories, time, nodes, pose, pair);
                    foreach (var p in pose.positions)
                        bounds.Encapsulate(p);
                }
                record.sampleCount = evidence.samples = samples.Count;
                LgFindLanding(samples, phase, target, nodes, evidence);
                record.sheetSeconds = LgMeasuredSheets(record.sheetSeconds, samples, evidence);
                // Every independent timeline sample is reverse-seeked, including feet/head geometry.
                foreach (float time in times.Reverse())
                {
                    pair.EvaluateAt(time);
                    var actual = LgReadPose(nodes, aSupport, bSupport);
                    if (LgContactTime(time, phase))
                    {
                        diagnostics.Clear();
                        actual.contactGap = face.Measure(diagnostics).gap;
                    }
                    LgCompareReverse(actual, samples[time], evidence);
                    evidence.reverseSamples++;
                }
                record.backwardsSeekSamples = evidence.reverseSamples;
                record.worstBackwardsSeekMetres = evidence.maximumReversePositionError;
                if (capture)
                {
                    bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
                    bounds.Expand(.45f);
                    using var rendering = new CandidateRendering(source.gameObject.scene, fighters, pair);
                    record.sheets = rendering.Write(LgOutput + "/" + stem, bounds, record, time =>
                    {
                        pair.EvaluateAt(time);
                        // Cached selected skins stay valid when CPU preview disables source renderers.
                        LgCompareReverse(LgReadPose(nodes, aSupport, bSupport), samples[time], evidence, false);
                    });
                }
                evidence.geometryGatesPassed = evidence.floorViolations == 0 && evidence.prefixViolations == 0 &&
                    evidence.maximumReversePositionError <= .001f && evidence.maximumReverseAngleError <= .1f &&
                    evidence.maximumReverseFloorError <= .001f && evidence.maximumReverseSupportError <= .001f &&
                    evidence.maximumReverseContactError <= .001f &&
                    evidence.maximumContactGapError <= LgContactTolerance &&
                    evidence.minimumContactGap <= .001f && evidence.nativeMinimumContactGap <= .001f;
                evidence.status = evidence.geometryGatesPassed ? "GEOMETRY_ONLY_SUPPORT_REVIEW_REQUIRED" :
                    "FAILED_GEOMETRY_SUPPORT_REVIEW_REQUIRED";
                if (!evidence.geometryGatesPassed)
                    evidence.failure = "Floor, prefix, contact or reverse-seek gate failed; see measured maxima/counts.";
            }
            catch (Exception error)
            {
                evidence.status = "FAILED_CASE";
                evidence.failure = error.ToString();
                throw;
            }
            finally
            {
                pair.Cancel();
                File.WriteAllText(LgOutput + "/" + stem + ".json", JsonUtility.ToJson(evidence, true));
            }
        }

        static LgPose LgReadPose(List<(string role, string rig, string bone, Transform node)> nodes,
            CombatExpansionAxeDenseStudy.LethalSupportProbe a, CombatExpansionAxeDenseStudy.LethalSupportProbe b)
        {
            var pose = LgPoseOnly(nodes);
            var aPoints = a.Measure();
            var bPoints = b.Measure();
            pose.support = aPoints.Concat(bPoints).ToArray();
            pose.attackerY = a.MinimumY;
            pose.receiverY = b.MinimumY;
            return pose;
        }

        static void LgComparePrefix(LgPose actual, LgPose expected, LgCase evidence)
        {
            if (actual.positions.Length != expected.positions.Length)
                throw new InvalidOperationException("Native/candidate mapped bone coverage differs.");
            float worst = 0;
            for (int i = 0; i < actual.positions.Length; i++)
                worst = Mathf.Max(worst, Vector3.Distance(actual.positions[i], expected.positions[i]));
            evidence.maximumPrefixPositionError = Mathf.Max(evidence.maximumPrefixPositionError, worst);
            if (worst > LgContactTolerance)
                evidence.prefixViolations++;
        }

        static void LgCompareReverse(LgPose actual, LgPose expected, LgCase evidence, bool compareContact = true)
        {
            for (int i = 0; i < actual.positions.Length; i++)
            {
                evidence.maximumReversePositionError = Mathf.Max(evidence.maximumReversePositionError,
                    Vector3.Distance(actual.positions[i], expected.positions[i]));
                evidence.maximumReverseAngleError = Mathf.Max(evidence.maximumReverseAngleError,
                    Quaternion.Angle(actual.rotations[i], expected.rotations[i]));
            }
            evidence.maximumReverseFloorError = Mathf.Max(evidence.maximumReverseFloorError,
                Mathf.Abs(actual.attackerY - expected.attackerY), Mathf.Abs(actual.receiverY - expected.receiverY));
            for (int i = 0; i < actual.support.Length; i++)
                evidence.maximumReverseSupportError = Mathf.Max(evidence.maximumReverseSupportError,
                    Mathf.Abs(actual.support[i].y - expected.support[i].y));
            if (compareContact && expected.contactGap >= 0)
                evidence.maximumReverseContactError = Mathf.Max(evidence.maximumReverseContactError,
                    Mathf.Abs(actual.contactGap - expected.contactGap));
        }
    }
}
