using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        static float[] LethalSheetTimes(LethalRawCase evidence, float duration, float attackEnd, float receiverEnd)
        {
            var times = new[] { 0f, .06f, .12f, .25f, evidence.contact - .12f, evidence.contact - .06f,
                evidence.contact, evidence.preserveEnd, evidence.blendEnd, evidence.compressedStart,
                evidence.compressedEnd, evidence.terminalStart, evidence.holdStart,
                (evidence.holdStart + receiverEnd) / 2, attackEnd, receiverEnd, duration };
            var blend = Enumerable.Range(0, 5).Select(i => evidence.preserveEnd + .22f * i / 4);
            var brace = Enumerable.Range(0, 7).Select(i => evidence.blendEnd + .48f * i / 6);
            var compressed = Enumerable.Range(0, 5).Select(i => evidence.compressedStart + .16f * i / 4);
            var terminal = Enumerable.Range(0, 9).Select(i => evidence.compressedEnd + .95f * i / 8);
            var result = times.Concat(blend).Concat(brace).Concat(compressed).Concat(terminal)
                .Where(t => t >= 0 && t <= duration).Distinct().OrderBy(t => t).ToArray();
            if (result.Length < 24 || evidence.holdStart > receiverEnd)
                throw new InvalidOperationException("Candidate cannot cover the required lethal phases.");
            return result;
        }

        static Vector3[] LethalPositions(List<(string role, string rig, string bone, Transform node)> nodes)
        {
            var positions = new Vector3[nodes.Count];
            for (int i = 0; i < nodes.Count; i++)
            {
                var node = nodes[i];
                var matrix = node.node.localToWorldMatrix;
                for (int component = 0; component < 16; component++)
                    if (!float.IsFinite(matrix[component]))
                        throw new InvalidOperationException("Nonfinite raw pose: " + node.role + "/" + node.bone);
                var q = node.node.rotation;
                if (!float.IsFinite(q.x) || !float.IsFinite(q.y) || !float.IsFinite(q.z) || !float.IsFinite(q.w))
                    throw new InvalidOperationException("Nonfinite raw rotation: " + node.role + "/" + node.bone);
                positions[i] = node.node.position;
            }
            return positions;
        }

        static void AppendLethalMeasurements(StringBuilder csv, LethalRawCase evidence,
            FrankBattlePairPlayback pair, CharacterCombat source, CharacterCombat target,
            float attackerY, float receiverY)
        {
            float seconds = pair.SampleTime;
            float attackClock = Mathf.Min(seconds, pair.Move.attackAnim.length);
            float receiverClock = Mathf.Min(seconds, pair.Move.hitAnim.length);
            var actors = pair.ReceiverActor.transform.position - pair.AttackerActor.transform.position;
            var fighters = target.Animator.transform.position - source.Animator.transform.position;
            csv.Append(FormattableString.Invariant($"{seconds:R},{attackClock:R},{receiverClock:R},"));
            csv.Append(FormattableString.Invariant($"{attackerY:R},{receiverY:R},"));
            csv.Append(FormattableString.Invariant($"{actors.x:R},{actors.y:R},{actors.z:R},"));
            csv.AppendLine(FormattableString.Invariant($"{fighters.x:R},{fighters.y:R},{fighters.z:R}"));
            if (attackerY < evidence.minimumAttackerY)
            {
                evidence.minimumAttackerY = attackerY;
                evidence.minimumAttackerSeconds = seconds;
            }
            if (receiverY < evidence.minimumReceiverY)
            {
                evidence.minimumReceiverY = receiverY;
                evidence.minimumReceiverSeconds = seconds;
            }
            if (attackerY < 0)
                evidence.attackerPenetratingSamples++;
            if (receiverY < 0)
                evidence.receiverPenetratingSamples++;
        }

        static void AppendLethalJoints(StringBuilder rows, CandidateRecord record, float seconds,
            Animator source, Animator target)
        {
            var head = target.GetBoneTransform(HumanBodyBones.Head).position;
            float left = Vector3.Distance(source.GetBoneTransform(HumanBodyBones.LeftHand).position, head);
            float right = Vector3.Distance(source.GetBoneTransform(HumanBodyBones.RightHand).position, head);
            rows.AppendLine(FormattableString.Invariant($"{seconds:R},{left:R},{right:R}"));
            if (seconds == 0)
            {
                record.entryAttackerFacingDot = CandidateForwardDot(source);
                record.entryReceiverFacingDot = CandidateForwardDot(target);
            }
            if (left < record.minimumLeftJointDistance)
            {
                record.minimumLeftJointDistance = left;
                record.minimumLeftJointSeconds = seconds;
            }
            if (right < record.minimumRightJointDistance)
            {
                record.minimumRightJointDistance = right;
                record.minimumRightJointSeconds = seconds;
            }
        }

        static void AppendLethalSourceTimes(StringBuilder csv, LethalRawCase evidence, float seconds,
            float attackEnd, float receiverEnd, float fallEnd, bool shown)
        {
            float receiverClock = Mathf.Min(seconds, receiverEnd);
            float elapsed = receiverClock - evidence.preserveEnd;
            float fallClock;
            if (elapsed <= 0)
                fallClock = .45f;
            else if (elapsed <= .70f)
                fallClock = Mathf.Lerp(.45f, 1.3f, elapsed / .70f);
            else if (elapsed <= .86f)
                fallClock = Mathf.Lerp(1.3f, 2f, (elapsed - .70f) / .16f);
            else if (elapsed <= 1.41f)
                fallClock = Mathf.Lerp(2f, 2.55f, (elapsed - .86f) / .55f);
            else
                fallClock = Mathf.Lerp(2.55f, fallEnd, Mathf.Clamp01((elapsed - 1.41f) / .40f));
            float weight = Mathf.Clamp01(elapsed / .22f);
            weight = weight * weight * (3 - 2 * weight);
            string phase = elapsed <= 0 ? "original" : elapsed < .22f ? "blend" :
                elapsed < .70f ? "brace" : elapsed < .86f ? "compressed" :
                elapsed < 1.41f ? "fall" : elapsed < 1.81f ? "terminal" : "hold";
            csv.Append(FormattableString.Invariant($"{seconds:R},{Mathf.Min(seconds, attackEnd):R},"));
            csv.Append(FormattableString.Invariant($"{receiverClock:R},{receiverClock:R},{fallClock:R},{weight:R},"));
            csv.AppendLine(phase + "," + (shown ? "1" : "0"));
        }
    }
}
