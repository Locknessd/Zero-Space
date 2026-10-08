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
        static void VerifySequentialContacts(SequentialPlayer player, SequentialCase record)
        {
            record.phase = "verify_fixed_onsets";
            for (int index = 0; index < 3; index++)
            {
                float seconds = record.onsets[index];
                if (seconds < 0)
                    continue;
                using var probe = SequentialProbe(player, record, index);
                var previous = MeasureSequential(player, probe, index, seconds - 1 / 240f);
                var sample = MeasureSequential(player, probe, index, seconds);
                ClassifySequential(sample, previous, index, record.lane);
                record.verification.Add(sample);
                // Seek away, then back with a complete raw-pair restore and instantaneous correction.
                player.Sample(record.duration);
                var reverse = MeasureSequential(player, probe, index, seconds);
                record.worstGapReseek = Mathf.Max(record.worstGapReseek,
                    Mathf.Abs(sample.contact.gap - reverse.contact.gap));
                CompareSequentialFloor(record, sample.floor, reverse.floor);
                if (record.worstGapReseek > .0001f)
                    throw new InvalidOperationException("Contact backward seek exceeds .1mm.");
                SaveSequentialCase(record);
            }
            File.WriteAllText(SequentialOutput + "/" + record.name + "_OnsetVerification.csv",
                ContactCsv(record.verification.Select(s => s.contact)));
        }

        static void CaptureSequentialSheets(SequentialPlayer player, SequentialCase record)
        {
            record.phase = "capture_24_frames";
            float[] context = Enumerable.Range(0, 3).Select(index => record.onsets[index] >= 0 ?
                record.onsets[index] : record.strikes[index].minimum.contact.seconds).ToArray();
            float attackEnd = record.attacks.Last().seconds + record.attacks.Last().sourceEndSeconds;
            var times = new[] { 0, .06f, .12f, context[0], .24f, .36f, .399f, .4f, .44f,
                .48f, context[1], .65f, .799f, .8f, .84f, .88f, context[2], 1.12f, 1.3f, 1.5f,
                1.65f, attackEnd, record.duration - .025f, record.duration }.OrderBy(t => t).ToArray();
            var candidate = new CandidateRecord
            {
                name = record.name, attacker = record.attacker, receiver = record.receiver,
                spacing = record.spacing, laneSign = record.lane, duration = record.duration,
                sheetSeconds = times, attacks = record.attacks, reactions = record.reactions
            };
            record.sheetSeconds = times;
            var nodes = Nodes(player.Pair, player.Source, player.Target);
            var poses = new Dictionary<float, PoseSample[]>();
            var floors = new Dictionary<float, SequentialFloor>();
            var bounds = new Bounds(player.Source.Animator.transform.position, Vector3.zero);
            var rows = new StringBuilder("seconds,role,rig,bone,x,y,z,qx,qy,qz,qw\n");
            var floorRows = new StringBuilder("seconds,attackerRaw,receiverRaw,attackerLift,receiverLift," +
                "attackerAfter,receiverAfter\n");
            var nativeRows = new StringBuilder("seconds,step,bone,localRotationErrorDegrees," +
                "worldPositionErrorAfterHorizontalCarryMetres,carryX,carryY,carryZ\n");
            using (var native = new NativeReference(player.Pair.AttackerActor, record.sources, candidate))
            {
                foreach (float seconds in times.Distinct())
                {
                    floors[seconds] = player.Sample(seconds);
                    CheckEquipment(player.Pair, player.Fighters);
                    native.Compare(seconds, nativeRows);
                    poses[seconds] = nodes.Select(node => new PoseSample(node.node.position,
                        node.node.rotation)).ToArray();
                    foreach (var node in nodes)
                    {
                        var p = node.node.position;
                        var q = node.node.rotation;
                        if (!Finite(p) || !float.IsFinite(q.x) || !float.IsFinite(q.y) ||
                            !float.IsFinite(q.z) || !float.IsFinite(q.w))
                            throw new InvalidOperationException("Nonfinite sequential sheet pose.");
                        bounds.Encapsulate(p);
                        rows.AppendLine(FormattableString.Invariant(
                            $"{seconds:R},{node.role},{node.rig},{node.bone},{p.x:R},{p.y:R},{p.z:R},") +
                            FormattableString.Invariant($"{q.x:R},{q.y:R},{q.z:R},{q.w:R}"));
                    }
                    var floor = floors[seconds];
                    floorRows.AppendLine(FormattableString.Invariant(
                        $"{seconds:R},{floor.attackerRaw:R},{floor.receiverRaw:R},") +
                        FormattableString.Invariant($"{floor.attackerLift:R},{floor.receiverLift:R},") +
                        FormattableString.Invariant($"{floor.attackerAfter:R},{floor.receiverAfter:R}"));
                    File.WriteAllText(SequentialOutput + "/" + record.name + "_Poses.csv", rows.ToString());
                    File.WriteAllText(SequentialOutput + "/" + record.name + "_Floor.csv", floorRows.ToString());
                    File.WriteAllText(SequentialOutput + "/" + record.name + "_Native.csv", nativeRows.ToString());
                }
            }
            var reverseRows = new StringBuilder("seconds,worstPoseMetres,worstPoseDegrees,worstLiftReseek\n");
            Action<float> verify = seconds =>
            {
                var actual = player.Sample(seconds);
                CompareSequentialFloor(record, floors[seconds], actual);
                CheckEquipment(player.Pair, player.Fighters);
                var saved = poses[seconds];
                for (int index = 0; index < nodes.Count; index++)
                {
                    record.worstPoseMetres = Mathf.Max(record.worstPoseMetres,
                        Vector3.Distance(nodes[index].node.position, saved[index].position));
                    record.worstPoseDegrees = Mathf.Max(record.worstPoseDegrees,
                        Quaternion.Angle(nodes[index].node.rotation, saved[index].rotation));
                }
                if (record.worstPoseMetres > .001f || record.worstPoseDegrees > .1f)
                    throw new InvalidOperationException("Sequential pose backward seek exceeds 1mm/.1degree.");
            };
            foreach (float seconds in times.Reverse())
            {
                verify(seconds);
                record.reverseSamples++;
                reverseRows.AppendLine(FormattableString.Invariant(
                    $"{seconds:R},{record.worstPoseMetres:R},{record.worstPoseDegrees:R},") +
                    FormattableString.Invariant($"{record.worstLiftReseek:R}"));
                File.WriteAllText(SequentialOutput + "/" + record.name + "_Backward.csv", reverseRows.ToString());
            }
            bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
            bounds.Expand(.45f);
            using var rendering = new CandidateRendering(player.Source.gameObject.scene, player.Fighters, player.Pair);
            record.sheets = rendering.Write(SequentialOutput + "/" + record.name, bounds, candidate, verify);
            File.WriteAllText(SequentialOutput + "/" + record.name + "_Presentation.json",
                JsonUtility.ToJson(candidate, true));
            SaveSequentialCase(record);
        }

        static void CompareSequentialFloor(SequentialCase record, SequentialFloor saved, SequentialFloor actual)
        {
            record.worstLiftReseek = Mathf.Max(record.worstLiftReseek,
                Mathf.Abs(saved.attackerLift - actual.attackerLift),
                Mathf.Abs(saved.receiverLift - actual.receiverLift));
            if (record.worstLiftReseek > .0001f)
                throw new InvalidOperationException("Instantaneous grounding accumulated or changed on backward seek.");
        }
    }
}
