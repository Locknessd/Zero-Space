using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        static string LgMetricsHeader()
        {
            string header = "seconds,attackerBodyY,receiverBodyY,attackerLift,receiverLift," +
                "attackerSupportGap,receiverSupportGap,attackerBothFeetGap,receiverBothFeetGap";
            foreach (string role in new[] { "attacker", "receiver" })
            foreach (var region in CombatExpansionAxeDenseStudy.LethalSupportProbe.Regions)
                header += "," + role + region + "X," + role + region + "Y," + role + region + "Z";
            return header;
        }

        static void LgWriteMetrics(StreamWriter writer, float seconds, LgPose pose, FrankPairGrounding grounding,
            CharacterCombat source, CharacterCombat target, LgCase evidence, LgPhase phase)
        {
            float aLift = LgTrackFor(grounding, source, false).At(seconds);
            float bLift = LgTrackFor(grounding, target, true).At(seconds);
            float aGap = pose.support.Take(5).Min(p => p.y);
            float bGap = pose.support.Skip(5).Min(p => p.y);
            float aFeet = Mathf.Min(pose.support[0].y, pose.support[1].y);
            float bFeet = Mathf.Min(pose.support[5].y, pose.support[6].y);
            writer.Write(FormattableString.Invariant($"{seconds:R},{pose.attackerY:R},{pose.receiverY:R},"));
            writer.Write(FormattableString.Invariant($"{aLift:R},{bLift:R},{aGap:R},{bGap:R},{aFeet:R},{bFeet:R}"));
            foreach (var point in pose.support)
                writer.Write(FormattableString.Invariant($",{point.x:R},{point.y:R},{point.z:R}"));
            writer.WriteLine();
            evidence.minimumAttackerY = Mathf.Min(evidence.minimumAttackerY, pose.attackerY);
            evidence.minimumReceiverY = Mathf.Min(evidence.minimumReceiverY, pose.receiverY);
            evidence.maximumAttackerLift = Mathf.Max(evidence.maximumAttackerLift, aLift);
            evidence.maximumReceiverLift = Mathf.Max(evidence.maximumReceiverLift, bLift);
            evidence.maximumBothFeetHeight = Mathf.Max(evidence.maximumBothFeetHeight, aFeet, bFeet);
            if (pose.attackerY < GroundingMinimumClearance || pose.receiverY < GroundingMinimumClearance)
            {
                evidence.floorViolations++;
                if (seconds <= phase.preserve)
                    evidence.prefixViolations++;
            }
            if (aGap > LgSupportBand || bGap > LgSupportBand)
                evidence.unsupportedSamples++;
            if (aFeet > LgSupportBand || bFeet > LgSupportBand)
                evidence.footFloatSamples++;
            if (seconds >= phase.hold)
            {
                evidence.maximumHoldSupportGap = Mathf.Max(evidence.maximumHoldSupportGap, bGap);
                if (bGap > LgSupportBand)
                    evidence.receiverUnsupportedHoldSamples++;
            }
        }

        static void LgWriteTrajectory(StreamWriter writer, float seconds,
            List<(string role, string rig, string bone, Transform node)> nodes, LgPose pose,
            FrankBattlePairPlayback pair)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var p = pose.positions[i];
                var q = pose.rotations[i];
                float clock = Mathf.Min(seconds,
                    node.role == "giver" ? pair.Move.attackAnim.length : pair.Move.hitAnim.length);
                writer.Write(FormattableString.Invariant(
                    $"{seconds:R},{clock:R},{node.role},{node.rig},{node.bone},{p.x:R},{p.y:R},{p.z:R},"));
                writer.WriteLine(FormattableString.Invariant($"{q.x:R},{q.y:R},{q.z:R},{q.w:R}"));
            }
        }

        static void LgContactRow(StreamWriter writer, float time, LgPose native,
            CombatExpansionAxeDenseStudy.BodyContact contact, LgCase evidence)
        {
            var h = contact.sourcePoint;
            var f = contact.bodyPoint;
            writer.WriteLine(FormattableString.Invariant(
                $"{time:R},{native.contactGap:R},{contact.gap:R},{h.x:R},{h.y:R},{h.z:R},{f.x:R},{f.y:R},{f.z:R}"));
            evidence.maximumContactGapError = Mathf.Max(evidence.maximumContactGapError,
                Mathf.Abs(contact.gap - native.contactGap));
            if (contact.gap < evidence.minimumContactGap)
            {
                evidence.minimumContactGap = contact.gap;
                evidence.measuredContactSeconds = time;
                evidence.measuredHandPoint = h;
                evidence.measuredFacePoint = f;
            }
        }

        static void LgFindLanding(Dictionary<float, LgPose> samples, LgPhase phase, CharacterCombat target,
            List<(string role, string rig, string bone, Transform node)> nodes, LgCase evidence)
        {
            var times = samples.Keys.OrderBy(t => t).ToArray();
            int hips = nodes.FindIndex(n => n.rig == "fighter" && n.role == "receiver" && n.bone == "Hips");
            if (hips < 0)
                throw new InvalidOperationException("Missing receiver hips trajectory.");
            int runStart = -1;
            foreach (int index in Enumerable.Range(0, times.Length))
            {
                float time = times[index];
                var pose = samples[time];
                var nonFoot = pose.support.Skip(7).OrderBy(p => p.y).First();
                bool supported = time > phase.preserve && nonFoot.y >= GroundingMinimumClearance &&
                    nonFoot.y <= LgSupportBand;
                if (!supported)
                    runStart = -1;
                else if (runStart < 0)
                    runStart = index;
                if (evidence.landingSeconds < 0 && runStart >= 0 && time - times[runStart] >= .1f)
                {
                    evidence.landingSeconds = times[runStart];
                    evidence.landingPreviousSeconds = times[Mathf.Max(0, runStart - 1)];
                    var landing = samples[times[runStart]];
                    evidence.landingSupportPoint = landing.support.Skip(7).OrderBy(p => p.y).First();
                    evidence.landingHips = landing.positions[hips];
                }
            }
            var held = times.Where(t => t >= phase.hold).ToArray();
            if (held.Length == 0)
                throw new InvalidOperationException("Missing full held-end evidence.");
            var reference = samples[held[0]];
            foreach (float time in held)
                for (int i = 0; i < nodes.Count; i++)
                    if (nodes[i].role == "receiver" && nodes[i].rig == "fighter")
                        evidence.maximumHoldMotion = Mathf.Max(evidence.maximumHoldMotion,
                            Vector3.Distance(reference.positions[i], samples[time].positions[i]));
        }

        static float[] LgMeasuredSheets(float[] initial, Dictionary<float, LgPose> samples, LgCase evidence)
        {
            var cues = new List<float>(initial);
            if (evidence.measuredContactSeconds >= 0)
                cues.Add(evidence.measuredContactSeconds);
            if (evidence.landingSeconds >= 0)
            {
                float[] times = samples.Keys.OrderBy(t => t).ToArray();
                foreach (float offset in new[] { -.1f, -.05f, 0, .05f, .1f })
                    cues.Add(times.OrderBy(t => Mathf.Abs(t - evidence.landingSeconds - offset)).First());
            }
            return cues.Distinct().OrderBy(t => t).ToArray();
        }
    }
}
