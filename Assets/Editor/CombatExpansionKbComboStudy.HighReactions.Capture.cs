using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        static void CaptureHighReactionCase(CharacterCombat[] fighters, SourceRecord[] sources,
            int assignment, int lane, float spacing, ref FrankPairGrounding grounding)
        {
            var source = fighters[assignment];
            var target = fighters[1 - assignment];
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            using var tracks = new Tracks(sources);
            for (int index = 0; index < 3; index++)
                tracks.reactions.segments[index].seconds = HighOnsets[index];
            var move = Move(source, target, sources, tracks, spacing);
            var rotation = Quaternion.LookRotation(Vector3.right * lane);
            source.transform.position = Vector3.left * lane * spacing / 2;
            target.transform.position = Vector3.right * lane * spacing / 2;
            source.Animator.transform.SetPositionAndRotation(source.transform.position, rotation);
            target.Animator.transform.SetPositionAndRotation(target.transform.position,
                rotation * Quaternion.Euler(0, 180, 0));
            string stem = source.name + "_" + (lane > 0 ? "Positive" : "Negative") + "_Range" +
                Mathf.RoundToInt(spacing * 100) + "_Reaction" + sources[4].localId;
            FrankBattlePairPlayback pair = null;
            try
            {
                if (!source.ExecuteAttack(move, target) || !source.SourcePlayback.Playing)
                    throw new InvalidOperationException("Rejected high-reaction candidate " + stem);
                pair = source.SourcePlayback;
                pair.AttackerActor.Pose.transferFingers = true;
                pair.ReceiverActor.Pose.transferFingers = true;
                pair.EvaluateAt(0);
                CheckEquipment(pair, fighters);
                var report = new StringBuilder("PROVISIONAL high reaction correction " + stem + "\n");
                foreach (var record in sources)
                    report.AppendLine(record.guid + ":" + record.localId + " " + record.name);
                if (!grounding)
                    grounding = BakeHighReactionGrounding(pair, source, target, report);
                move.grounding = grounding;
                report.AppendLine("Grounding uses same assignment and reaction; independent check for this lane/range.");
                var nodes = Nodes(pair, source, target);
                var bounds = new Bounds(source.Animator.transform.position, Vector3.zero);
                var csv = new StringBuilder("seconds,attackerMinimumY,receiverMinimumY," +
                    "attackerHipsX,attackerHipsY,attackerHipsZ,receiverHipsX,receiverHipsY,receiverHipsZ\n");
                float worst = float.MaxValue;
                int intervals = Mathf.CeilToInt(pair.Duration * 361);
                for (int frame = 0; frame <= intervals; frame++)
                {
                    float seconds = pair.Duration * frame / intervals;
                    pair.EvaluateAt(seconds);
                    float a = BattlePresentationContactSetup.MeasureGroundClearance(source);
                    float b = BattlePresentationContactSetup.MeasureGroundClearance(target);
                    if (!float.IsFinite(a) || !float.IsFinite(b) || a < -.001f || b < -.001f)
                        throw new InvalidOperationException("High-reaction grounding clearance failed at " + seconds);
                    worst = Mathf.Min(worst, a, b);
                    var aHips = source.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    var bHips = target.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
                    csv.AppendLine(FormattableString.Invariant($"{seconds:R},{a:R},{b:R},") +
                        FormattableString.Invariant($"{aHips.x:R},{aHips.y:R},{aHips.z:R},") +
                        FormattableString.Invariant($"{bHips.x:R},{bHips.y:R},{bHips.z:R}"));
                    foreach (var node in nodes)
                        bounds.Encapsulate(node.node.position);
                }
                report.AppendLine(FormattableString.Invariant($"361Hz floor samples={intervals + 1}; minimum={worst:R}m"));
                File.WriteAllText(HighOutput + "/" + stem + "_Floor.csv", csv.ToString());
                var times = new[] { 0f, .08f, .1125f, .16f, .3f, .399f, .4f, .44f, .48f,
                    .504166667f, .55f, .68f, .799f, .8f, .84f, .88f, .916666667f, .966666667f,
                    1.016666667f, 1.1f, 1.25f, 1.5f, 1.7f, pair.Duration };
                var poses = new Dictionary<float, PoseSample[]>();
                foreach (float seconds in times)
                {
                    pair.EvaluateAt(seconds);
                    poses.Add(seconds, nodes.Select(node => new PoseSample(node.node.position,
                        node.node.rotation)).ToArray());
                }
                MeasureHighReactionContacts(pair, source, target, report, stem);
                var candidate = new CandidateRecord
                {
                    name = stem,
                    attacker = source.name,
                    receiver = target.name,
                    laneSign = lane,
                    spacing = spacing,
                    duration = pair.Duration,
                    sheetSeconds = times
                };
                bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
                bounds.Expand(.45f);
                using var rendering = new CandidateRendering(source.gameObject.scene, fighters, pair);
                candidate.sheets = rendering.Write(HighOutput + "/" + stem, bounds, candidate, seconds =>
                    Verify(pair, fighters, nodes, poses[seconds], seconds, candidate));
                report.AppendLine(FormattableString.Invariant(
                    $"Reverse selected frames: position={candidate.worstBackwardsSeekMetres:R}m; ") +
                    FormattableString.Invariant($"rotation={candidate.worstBackwardsSeekDegrees:R}degrees"));
                File.WriteAllText(HighOutput + "/" + stem + ".txt", report.ToString());
            }
            finally
            {
                if (pair)
                    pair.Cancel();
            }
        }

        static void MeasureHighReactionContacts(FrankBattlePairPlayback pair, CharacterCombat source,
            CharacterCombat target, StringBuilder report, string stem)
        {
            var rows = new List<ContactEvidence>();
            for (int index = 0; index < 3; index++)
            {
                var limb = index == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
                using var probe = new CombatExpansionAxeDenseStudy.SkinRegionProbe(source, limb,
                    target, HumanBodyBones.Head);
                pair.EvaluateAt(HighOnsets[index]);
                var contact = probe.Measure(new List<string>());
                var row = ContactRow(HighOnsets[index], pair.SampleTime, index + 1, "Head", contact,
                    source.Animator.GetBoneTransform(limb), target.Animator.GetBoneTransform(HumanBodyBones.Head),
                    BattlePresentationContactSetup.MeasureGroundClearance(source),
                    BattlePresentationContactSetup.MeasureGroundClearance(target), null);
                rows.Add(row);
                report.AppendLine(FormattableString.Invariant(
                    $"Candidate strike {index + 1} at {row.seconds:R}s: head gap={contact.gap:R}m."));
            }
            File.WriteAllText(HighOutput + "/" + stem + "_Contacts.csv", ContactCsv(rows));
        }
    }
}
