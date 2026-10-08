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
        static LethalRawCase CaptureLethalRawCase(CharacterCombat[] fighters, SourceRecord[] sources,
            AnimationClip candidate, float fallEnd, int sequence, int assignment, int direction)
        {
            var source = fighters[assignment];
            var target = fighters[1 - assignment];
            string label = "Sequence" + sequence;
            var attack = sources.Single(s => s.label == label + (sequence == 1 ? "_A" : "_B"));
            var reaction = sources.Single(s => s.label == label + (sequence == 1 ? "_B" : "_A"));
            if (Mathf.Abs(candidate.length - reaction.durationSeconds) > .0001f)
                throw new InvalidOperationException("Candidate must preserve the original receiver duration.");
            var move = CandidateMove(source, target, attack, reaction, 180, .8f);
            move.hitAnim = candidate;
            move.sourcePair.reaction = candidate;
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            source.Animator.transform.SetPositionAndRotation(Vector3.left * direction * .4f,
                Quaternion.LookRotation(Vector3.right * direction));
            target.Animator.transform.SetPositionAndRotation(Vector3.right * direction * .4f,
                Quaternion.LookRotation(Vector3.left * direction));
            string stem = "RAW_Lethal_" + label + "_" + source.name + "_" + target.name +
                (direction > 0 ? "_PositiveLane" : "_NegativeLane");
            var clip = LethalClipSnapshot(candidate);
            var record = new CandidateRecord
            {
                name = stem,
                status = "RAW_LETHAL_CANDIDATE",
                attacker = source.name,
                receiver = target.name,
                sequence = sequence,
                receiverYaw = 180,
                spacing = .8f,
                lane = Vector3.right * direction,
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
                reactionAdaptedIdentity = clip.identity,
                trajectory = stem + ".csv",
                measurements = stem + "_Measurements.csv"
            };
            float contact = sequence == 1 ? 1.4f : .933333333f;
            float preserve = contact + .12f;
            var evidence = new LethalRawCase
            {
                pair = record,
                candidateIdentity = clip.identity,
                candidatePath = clip.path,
                candidateHash = clip.byteHash,
                sourceTimeMap = stem + "_SourceTimes.csv",
                jointMeasurements = stem + "_Joints.csv",
                contact = contact,
                preserveEnd = preserve,
                blendEnd = preserve + .22f,
                compressedStart = preserve + .70f,
                compressedEnd = preserve + .86f,
                terminalStart = preserve + 1.41f,
                holdStart = preserve + 1.81f
            };
            try
            {
                if (!source.ExecuteAttack(move, target) || !source.SourcePlayback ||
                    !source.SourcePlayback.Playing)
                    throw new InvalidOperationException("Runtime rejected actual baked candidate: " + stem);
                var pair = source.SourcePlayback;
                if (pair.Move.grounding || pair.Move.sourcePair.reaction != candidate ||
                    pair.Move.hitAnim != candidate ||
                    pair.Move.attackAnim != attack.asset || pair.Move.sourcePair.attack != attack.asset)
                    throw new InvalidOperationException("Raw candidate pair substituted unexpected motion/grounding.");
                pair.AttackerActor.Pose.transferFingers = true;
                pair.ReceiverActor.Pose.transferFingers = true;
                record.duration = pair.Duration;
                record.sheetSeconds = LethalSheetTimes(evidence, pair.Duration,
                    attack.durationSeconds, candidate.length);
                var nodes = CandidateNodes(pair, source, target);
                var trajectories = new StringBuilder("seconds,sourceClock,role,rig,bone,x,y,z,qx,qy,qz,qw\n");
                var joints = new StringBuilder("seconds,leftHandPivotToHeadPivotMetres," +
                    "rightHandPivotToHeadPivotMetres\n");
                var measurements = new StringBuilder("seconds,attackerSourceClock,receiverSourceClock," +
                    "attackerBodyMinimumY,receiverBodyMinimumY,actorReceiverMinusAttackerX," +
                    "actorReceiverMinusAttackerY,actorReceiverMinusAttackerZ," +
                    "fighterReceiverMinusAttackerX,fighterReceiverMinusAttackerY,fighterReceiverMinusAttackerZ\n");
                var sourceTimes = new StringBuilder("seconds,attackerSourceClock,originalReceiverSourceClock," +
                    "candidateReceiverSourceClock,fallSourceClock,fallWeight,phase,shown\n");
                var shown = new Dictionary<float, (Vector3[] positions, float attackerY, float receiverY)>();
                var bounds = new Bounds(source.Animator.transform.position, Vector3.zero);
                var times = Enumerable.Range(0, GroundingIntervals(pair.Duration, 60) + 1)
                    .Select(i => Mathf.Min(i / 60f, pair.Duration)).Concat(record.sheetSeconds)
                    .Distinct().OrderBy(t => t).ToArray();
                foreach (float seconds in times)
                {
                    pair.EvaluateAt(seconds);
                    CheckCandidateEquipment(pair, fighters);
                    var positions = LethalPositions(nodes);
                    float attackerY = GroundingClearance(source);
                    float receiverY = GroundingClearance(target);
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        var node = nodes[i];
                        var p = positions[i];
                        var q = node.node.rotation;
                        if (node.rig == "fighter")
                            bounds.Encapsulate(p);
                        float clock = Mathf.Min(pair.SampleTime,
                            node.role == "giver" ? attack.durationSeconds : candidate.length);
                        trajectories.Append(FormattableString.Invariant(
                            $"{seconds:R},{clock:R},{node.role},{node.rig},{node.bone},"));
                        trajectories.AppendLine(FormattableString.Invariant(
                            $"{p.x:R},{p.y:R},{p.z:R},{q.x:R},{q.y:R},{q.z:R},{q.w:R}"));
                    }
                    bool isShown = Array.IndexOf(record.sheetSeconds, seconds) >= 0;
                    if (isShown)
                        shown.Add(seconds, (positions, attackerY, receiverY));
                    AppendLethalMeasurements(measurements, evidence, pair, source, target, attackerY, receiverY);
                    AppendLethalJoints(joints, record, seconds, source.Animator, target.Animator);
                    AppendLethalSourceTimes(sourceTimes, evidence, seconds, attack.durationSeconds,
                        candidate.length, fallEnd, isShown);
                }
                record.sampleCount = times.Length;
                File.WriteAllText(LethalRawOutput + "/" + record.trajectory, trajectories.ToString());
                File.WriteAllText(LethalRawOutput + "/" + record.measurements, measurements.ToString());
                File.WriteAllText(LethalRawOutput + "/" + evidence.sourceTimeMap, sourceTimes.ToString());
                File.WriteAllText(LethalRawOutput + "/" + evidence.jointMeasurements, joints.ToString());
                Action<float> verify = seconds =>
                {
                    pair.EvaluateAt(seconds);
                    CheckCandidateEquipment(pair, fighters);
                    var positions = LethalPositions(nodes);
                    var baseline = shown[seconds];
                    for (int i = 0; i < positions.Length; i++)
                        record.worstBackwardsSeekMetres = Mathf.Max(record.worstBackwardsSeekMetres,
                            Vector3.Distance(positions[i], baseline.positions[i]));
                    // Check transforms without rebaking geometry after CandidateRendering hides original skins.
                    if (record.worstBackwardsSeekMetres > .001f)
                        throw new InvalidOperationException("Backward seek exceeded 1mm: " + stem + " at " + seconds);
                };
                foreach (float seconds in record.sheetSeconds.Reverse())
                {
                    verify(seconds);
                    var baseline = shown[seconds];
                    evidence.worstBackwardFloorMetres = Mathf.Max(evidence.worstBackwardFloorMetres,
                        Mathf.Abs(GroundingClearance(source) - baseline.attackerY),
                        Mathf.Abs(GroundingClearance(target) - baseline.receiverY));
                    if (evidence.worstBackwardFloorMetres > .001f)
                        throw new InvalidOperationException(
                            "Backward-seek body floor changed by more than 1mm: " + stem);
                    record.backwardsSeekSamples++;
                }
                bounds.Encapsulate(new Vector3(bounds.center.x, evidence.minimumAttackerY, bounds.center.z));
                bounds.Encapsulate(new Vector3(bounds.center.x, evidence.minimumReceiverY, bounds.center.z));
                bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
                bounds.Expand(.45f);
                using (var rendering = new CandidateRendering(source.gameObject.scene, fighters, pair))
                    record.sheets = rendering.Write(LethalRawOutput + "/" + stem, bounds, record, verify);
                File.WriteAllText(LethalRawOutput + "/" + stem + ".json", JsonUtility.ToJson(evidence, true));
                return evidence;
            }
            finally
            {
                if (source.SourcePlayback)
                    source.SourcePlayback.Cancel();
            }
        }
    }
}
