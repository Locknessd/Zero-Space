using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        static CandidateRecord CaptureCandidate(CharacterCombat[] fighters, SourceRecord[] sources,
            int sequence, int assignment, float yaw, float spacing)
        {
            var source = fighters[assignment];
            var target = fighters[1 - assignment];
            var attack = sources.Single(s => s.label == "Sequence" + sequence + (sequence == 1 ? "_A" : "_B"));
            var reaction = sources.Single(s => s.label == "Sequence" + sequence + (sequence == 1 ? "_B" : "_A"));
            var move = CandidateMove(source, target, attack, reaction, yaw, spacing);
            source.Animator.transform.SetPositionAndRotation(Vector3.left * spacing / 2,
                Quaternion.LookRotation(Vector3.right));
            target.Animator.transform.SetPositionAndRotation(Vector3.right * spacing / 2,
                Quaternion.LookRotation(Vector3.right) * Quaternion.Euler(0, yaw, 0));
            string stem = "CANDIDATE_Sequence" + sequence + "_" + source.name + "_" + target.name +
                "_Yaw" + yaw.ToString("F0") + "_Range" + Mathf.RoundToInt(spacing * 100) + "cm";
            var record = new CandidateRecord
            {
                name = stem,
                attacker = source.name,
                receiver = target.name,
                sequence = sequence,
                receiverYaw = yaw,
                spacing = spacing,
                receiverOffset = move.sourcePair.receiverOffset,
                initialAttackerPosition = source.Animator.transform.position,
                initialReceiverPosition = target.Animator.transform.position,
                initialAttackerRotation = source.Animator.transform.rotation,
                initialReceiverRotation = target.Animator.transform.rotation,
                attackerDriver = DriverPath(source.name, attack),
                receiverDriver = DriverPath(target.name, reaction),
                attackOriginalGuid = attack.guid,
                reactionOriginalGuid = reaction.guid,
                attackOriginalLocalId = attack.localId,
                reactionOriginalLocalId = reaction.localId,
                attackAdaptedIdentity = CombatExpansionInventory.Identity(attack.asset),
                reactionAdaptedIdentity = CombatExpansionInventory.Identity(reaction.asset),
                trajectory = stem + ".csv",
                measurements = stem + "_Joints.csv"
            };
            if (!source.ExecuteAttack(move, target) || !source.SourcePlayback || !source.SourcePlayback.Playing)
                throw new InvalidOperationException("Runtime rejected candidate " + stem);
            var pair = source.SourcePlayback;
            try
            {
                pair.AttackerActor.Pose.transferFingers = true;
                pair.ReceiverActor.Pose.transferFingers = true;
                record.duration = pair.Duration;
                record.sheetSeconds = CandidateSheetTimes(sequence, pair.Duration,
                    attack.durationSeconds, reaction.durationSeconds);
                var nodes = CandidateNodes(pair, source, target);
                                var rows = new StringBuilder("seconds,sourceClock,role,rig,bone,x,y,z,qx,qy,qz,qw\n");
                var joints = new StringBuilder("seconds,rig,attackerForwardDotPositiveLane," +
                    "receiverForwardDotPositiveLane,leftHandPivotToHeadPivotMetres,rightHandPivotToHeadPivotMetres\n");
                var bounds = new Bounds(source.Animator.GetBoneTransform(HumanBodyBones.Hips).position, Vector3.zero);
                int ticks = Mathf.CeilToInt(pair.Duration * 60);
                var times = Enumerable.Range(0, ticks + 1).Select(i => Mathf.Min(i / 60f, pair.Duration))
                    .Concat(record.sheetSeconds).Append(.12f).Distinct().OrderBy(t => t).ToArray();
                foreach (float seconds in times)
                {
                    pair.EvaluateAt(seconds);
                    CheckCandidateEquipment(pair, fighters);
                    var positions = nodes.Select(n => n.node.position).ToArray();
                    for (int index = 0; index < nodes.Count; index++)
                    {
                        var node = nodes[index];
                        var p = positions[index];
                        var q = node.node.rotation;
                        if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z))
                            throw new InvalidOperationException("Nonfinite candidate trajectory " + stem);
                        if (node.rig == "fighter")
                            bounds.Encapsulate(p);
                        float clock = Mathf.Min(pair.SampleTime,
                            node.role == "giver" ? attack.durationSeconds : reaction.durationSeconds);
                        rows.AppendLine(FormattableString.Invariant(
                            $"{seconds:R},{clock:R},{node.role},{node.rig},{node.bone},{p.x:R},{p.y:R},{p.z:R},{q.x:R},{q.y:R},{q.z:R},{q.w:R}"));
                    }
                    CandidateMeasurements(joints, record, pair.SampleTime, source.Animator, target.Animator, "fighter");
                    CandidateMeasurements(joints, record, pair.SampleTime,
                        pair.AttackerActor.Pose.driver, pair.ReceiverActor.Pose.driver, "native");
                }
                record.sampleCount = times.Length;
                File.WriteAllText(CandidateOutput + "/" + record.trajectory, rows.ToString());
                File.WriteAllText(CandidateOutput + "/" + record.measurements, joints.ToString());
                var samples = CandidateReadCsv(CandidateOutput + "/" + record.trajectory, nodes.Count);
                Action<float> verify = seconds =>
                {
                    pair.EvaluateAt(seconds);
                    CheckCandidateEquipment(pair, fighters);
                    for (int index = 0; index < nodes.Count; index++)
                        record.worstBackwardsSeekMetres = Mathf.Max(record.worstBackwardsSeekMetres,
                            Vector3.Distance(nodes[index].node.position, samples[seconds][index]));
                    if (record.worstBackwardsSeekMetres > .001f)
                        throw new InvalidOperationException("Backwards seek exceeded 0.001m for " + stem +
                            " at " + seconds + ": " + record.worstBackwardsSeekMetres);
                };
                foreach (float seconds in record.sheetSeconds.Reverse())
                {
                    verify(seconds);
                    record.backwardsSeekSamples++;
                }
                bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
                bounds.Expand(.45f);
                using var rendering = new CandidateRendering(source.gameObject.scene, fighters, pair);
                record.sheets = rendering.Write(CandidateOutput + "/" + stem, bounds, record, verify);
                File.WriteAllText(CandidateOutput + "/" + stem + ".json", JsonUtility.ToJson(record, true));
                return record;
            }
            finally
            {
                pair.Cancel();
            }
        }

        static float[] CandidateSheetTimes(int sequence, float duration, float attackEnd, float reactionEnd)
        {
            float start = sequence == 1 ? 1f : .5f;
            var focused = Enumerable.Range(0, 25).Select(i => start + i / 16f);
            return focused.Concat(new[] { 0f, .12f, .25f, 3f, 5f, 6f, 7f, duration, attackEnd, reactionEnd })
                .Where(t => t <= duration).Distinct().OrderBy(t => t).ToArray();
        }
    }
}
