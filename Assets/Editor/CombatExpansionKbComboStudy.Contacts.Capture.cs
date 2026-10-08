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
        static readonly HumanBodyBones[] ContactTorsoBones = { HumanBodyBones.Hips, HumanBodyBones.Spine,
            HumanBodyBones.Chest, HumanBodyBones.UpperChest };

        static void CaptureContactsCase(CharacterCombat[] fighters, SourceRecord[] sources,
            ContactsCase record, ContactsReport report)
        {
            var source = fighters[record.assignment];
            var target = fighters[1 - record.assignment];
            record.attacker = source.name;
            record.receiver = target.name;
            using var tracks = new Tracks(sources);
            var move = Move(source, target, sources, tracks, record.spacing);
            var rotation = Quaternion.LookRotation(Vector3.right * record.laneSign);
            source.transform.position = Vector3.left * record.laneSign * record.spacing / 2;
            target.transform.position = Vector3.right * record.laneSign * record.spacing / 2;
            source.Animator.transform.SetPositionAndRotation(source.transform.position, rotation);
            target.Animator.transform.SetPositionAndRotation(target.transform.position,
                rotation * Quaternion.Euler(0, 180, 0));
            FrankBattlePairPlayback pair = null;
            var probes = new List<CombatExpansionAxeDenseStudy.SkinRegionProbe>();
            try
            {
                if (!source.ExecuteAttack(move, target, false) || !source.SourcePlayback.Playing)
                    throw new InvalidOperationException("Runtime rejected " + record.name);
                pair = source.SourcePlayback;
                pair.AttackerActor.Pose.transferFingers = true;
                pair.ReceiverActor.Pose.transferFingers = true;
                pair.EvaluateAt(0);
                CheckEquipment(pair, fighters);
                record.duration = pair.Duration;
                foreach (var limb in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                {
                    probes.Add(new CombatExpansionAxeDenseStudy.SkinRegionProbe(source, limb,
                        target, HumanBodyBones.Head));
                    probes.Add(new CombatExpansionAxeDenseStudy.SkinRegionProbe(source, limb,
                        target, HumanBodyBones.Hips, ContactTorsoBones));
                }
                record.selection.AddRange(probes.Select(p => p.SelectionSummary));
                var attackerBody = new CombatExpansionAxeDenseStudy.MovingBodyProbe(source);
                var receiverBody = new CombatExpansionAxeDenseStudy.MovingBodyProbe(target);
                var nodes = Nodes(pair, source, target);
                var bounds = new Bounds(source.Animator.transform.position, Vector3.zero);
                var diagnostics = new List<string>();
                var snapshots = new List<ContactSnapshot>();
                var previous = new Dictionary<int, ContactEvidence>();
                record.phase = "sampling";
                foreach (float seconds in ContactsTimes(pair.Duration, tracks.attacks.Duration))
                {
                    pair.EvaluateAt(seconds);
                    if (Mathf.Abs(pair.SampleTime - seconds) > .00001f)
                        throw new InvalidOperationException("Pair clock mismatch at " + seconds);
                    diagnostics.Clear();
                    attackerBody.Update(diagnostics);
                    receiverBody.Update(diagnostics);
                    var rows = new List<ContactEvidence>();
                    foreach (int strike in ContactStrikes(seconds))
                    for (int surface = 0; surface < 2; surface++)
                    {
                        int probe = (strike == 1 ? 0 : 2) + surface;
                        var contact = probes[probe].Measure(diagnostics);
                        var hand = source.Animator.GetBoneTransform(strike == 1 ?
                            HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                        var anchor = target.Animator.GetBoneTransform(contact.nearestBone);
                        int key = strike * 2 + surface;
                        previous.TryGetValue(key, out var prior);
                        var row = ContactRow(seconds, pair.SampleTime, strike, surface == 0 ? "Head" : "Torso",
                            contact, hand, anchor, attackerBody.MinimumY, receiverBody.MinimumY, prior);
                        previous[key] = row;
                        rows.Add(row);
                    }
                    snapshots.Add(new ContactSnapshot
                    {
                        seconds = seconds,
                        contacts = rows.ToArray(),
                        poses = nodes.Select(n => new PoseSample(n.node.position, n.node.rotation)).ToArray()
                    });
                    foreach (var node in nodes)
                        bounds.Encapsulate(node.node.position);
                    record.minimumAttackerY = Mathf.Min(record.minimumAttackerY, attackerBody.MinimumY);
                    record.minimumReceiverY = Mathf.Min(record.minimumReceiverY, receiverBody.MinimumY);
                    record.samples++;
                    if (record.samples == 1)
                        record.selection.AddRange(diagnostics);
                    if (record.samples % 30 == 0)
                        SaveContactsReport(report);
                }
                record.trajectory = record.name + ".csv";
                File.WriteAllText(ContactsOutput + "/" + record.trajectory,
                    ContactCsv(snapshots.SelectMany(s => s.contacts)));
                SelectContactCandidates(record, snapshots, tracks.attacks.Duration);
                var poseRecord = new CandidateRecord { name = record.name };
                var backwards = new StringBuilder("seconds,gapErrorM,clearanceErrorM," +
                    "maximumPoseErrorM,maximumPoseErrorDegrees\n");
                record.phase = "reverse_seek";
                foreach (var snapshot in snapshots.AsEnumerable().Reverse())
                {
                    VerifyContacts(pair, fighters, nodes, probes, attackerBody, receiverBody,
                        snapshot, poseRecord, record, backwards);
                    record.reverseSamples++;
                    if (record.reverseSamples % 30 == 0)
                        SaveContactsReport(report);
                }
                record.backwardsSeek = record.name + "_BackwardsSeek.csv";
                File.WriteAllText(ContactsOutput + "/" + record.backwardsSeek, backwards.ToString());
                record.phase = "overview_png";
                SaveContactsReport(report);
                var byTime = snapshots.ToDictionary(s => s.seconds);
                poseRecord.attacker = source.name;
                poseRecord.receiver = target.name;
                poseRecord.laneSign = record.laneSign;
                poseRecord.spacing = record.spacing;
                poseRecord.sheetSeconds = record.sheetContext.Select(s => s.seconds).Distinct().OrderBy(s => s).ToArray();
                bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
                bounds.Expand(.45f);
                using var rendering = new CandidateRendering(source.gameObject.scene, fighters, pair);
                record.sheets = rendering.Write(ContactsOutput + "/" + record.name, bounds, poseRecord, seconds =>
                    VerifyContacts(pair, fighters, nodes, probes, attackerBody, receiverBody,
                        byTime[seconds], poseRecord, record, null));
                record.phase = "complete";
            }
            finally
            {
                foreach (var probe in probes)
                    probe.Dispose();
                if (pair)
                    pair.Cancel();
                else if (source.SourcePlayback)
                    source.SourcePlayback.Cancel();
            }
        }

        static void VerifyContacts(FrankBattlePairPlayback pair, CharacterCombat[] fighters, List<Node> nodes,
            List<CombatExpansionAxeDenseStudy.SkinRegionProbe> probes,
            CombatExpansionAxeDenseStudy.MovingBodyProbe attackerBody,
            CombatExpansionAxeDenseStudy.MovingBodyProbe receiverBody, ContactSnapshot saved,
            CandidateRecord poseRecord, ContactsCase record, StringBuilder rows)
        {
            Verify(pair, fighters, nodes, saved.poses, saved.seconds, poseRecord);
            var diagnostics = new List<string>();
            attackerBody.Update(diagnostics);
            receiverBody.Update(diagnostics);
            float gapError = 0;
            float clearanceError = 0;
            foreach (var expected in saved.contacts)
            {
                int probe = (expected.strike == 1 ? 0 : 2) + (expected.surface == "Head" ? 0 : 1);
                var actual = probes[probe].Measure(diagnostics);
                gapError = Mathf.Max(gapError, Mathf.Abs(actual.gap - expected.gap));
                clearanceError = Mathf.Max(clearanceError,
                    Mathf.Abs(attackerBody.MinimumY - expected.attackerMinimumY),
                    Mathf.Abs(receiverBody.MinimumY - expected.receiverMinimumY));
            }
            record.worstGapReseekMetres = Mathf.Max(record.worstGapReseekMetres, gapError);
            record.worstClearanceReseekMetres = Mathf.Max(record.worstClearanceReseekMetres, clearanceError);
            record.worstPoseReseekMetres = poseRecord.worstBackwardsSeekMetres;
            record.worstPoseReseekDegrees = poseRecord.worstBackwardsSeekDegrees;
            rows?.AppendLine(FormattableString.Invariant($"{saved.seconds:R},{gapError:R},{clearanceError:R},") +
                FormattableString.Invariant($"{record.worstPoseReseekMetres:R},{record.worstPoseReseekDegrees:R}"));
            if (!float.IsFinite(gapError) || !float.IsFinite(clearanceError) ||
                gapError > .001f || clearanceError > .001f)
                throw new InvalidOperationException("Contact/clearance reseek error exceeds 1mm: " + record.name);
        }
    }
}
