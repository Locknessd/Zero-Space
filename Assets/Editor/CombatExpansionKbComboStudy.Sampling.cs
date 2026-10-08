using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        static CandidateRecord CaptureCandidate(CharacterCombat[] fighters, SourceRecord[] sources,
            int assignment, int lane, float spacing)
        {
            var source = fighters[assignment];
            var target = fighters[1 - assignment];
            using var tracks = new Tracks(sources);
            var move = Move(source, target, sources, tracks, spacing);
            var rotation = Quaternion.LookRotation(Vector3.right * lane);
            source.transform.position = Vector3.left * lane * spacing / 2;
            target.transform.position = Vector3.right * lane * spacing / 2;
            source.Animator.transform.SetPositionAndRotation(source.transform.position, rotation);
            target.Animator.transform.SetPositionAndRotation(target.transform.position,
                rotation * Quaternion.Euler(0, 180, 0));
            string stem = "PROVISIONAL_" + source.name + "_Lane" + (lane > 0 ? "Positive" : "Negative") +
                "_Range" + Mathf.RoundToInt(spacing * 100) + "cm";
            var record = new CandidateRecord
            {
                name = stem, attacker = source.name, receiver = target.name, laneSign = lane, spacing = spacing,
                attackerDriver = AssetDatabase.GetAssetPath(move.sourcePair.attackerDriver),
                receiverDriver = AssetDatabase.GetAssetPath(move.sourcePair.receiverDriver),
                trajectory = stem + ".csv", seams = stem + "_Seams.csv",
                nativeEquivalence = stem + "_NativeEquivalence.csv",
                attacks = tracks.attacks.steps.Select((s, i) => new TrackRecord
                {
                    id = s.stepId, guid = sources[i].guid, localId = sources[i].localId,
                    seconds = s.seconds, sourceStartSeconds = s.sourceStartSeconds,
                    sourceEndSeconds = s.sourceEndSeconds, blendSeconds = s.blendSeconds,
                    candidateContactSeconds = tracks.reactions.segments[i].seconds
                }).ToArray(),
                reactions = tracks.reactions.segments.Select((s, i) => new TrackRecord
                {
                    id = s.strikeId, guid = ReactionGuid, localId = sources[i == 2 ? 4 : 3].localId,
                    seconds = s.seconds, sourceEndSeconds = s.clip.length, blendSeconds = s.blendSeconds,
                    candidateContactSeconds = s.seconds, terminal = s.terminal
                }).ToArray()
            };
            FrankBattlePairPlayback pair = null;
            try
            {
                if (!source.ExecuteAttack(move, target, false) || !source.SourcePlayback.Playing)
                    throw new InvalidOperationException("Runtime rejected " + stem);
                pair = source.SourcePlayback;
                pair.AttackerActor.Pose.transferFingers = true;
                pair.ReceiverActor.Pose.transferFingers = true;
                pair.EvaluateAt(0);
                CheckEquipment(pair, fighters);
                record.duration = pair.Duration;
                record.sheetSeconds = new[] { 0f, .05f, .1f, .12f, .2f, .35f, .399f, .4f, .44f, .48f,
                    .525f, .6f, .72f, .799f, .8f, .84f, .88f, 1.08f, 1.35f, 1.51f, 1.56f, 1.68f,
                    tracks.attacks.Duration, pair.Duration }.Distinct().OrderBy(t => t).ToArray();
                var nodes = Nodes(pair, source, target);
                var times = Enumerable.Range(0, Mathf.CeilToInt(pair.Duration * 120) + 1)
                    .Select(i => Mathf.Min(i / 120f, pair.Duration)).Concat(record.sheetSeconds)
                    .Concat(new[] { .399f, .4f, .401f, .799f, .8f, .801f, .135f, .56f, 1.545f })
                    .Concat(tracks.attacks.steps.SelectMany(s => new[]
                    { s.seconds, s.seconds + s.blendSeconds, s.seconds + s.sourceEndSeconds }))
                    .Concat(tracks.reactions.segments.SelectMany(s => new[]
                    { s.seconds, s.seconds + s.blendSeconds, s.seconds + s.clip.length }))
                    .Where(t => t <= pair.Duration).Distinct().OrderBy(t => t).ToArray();
                var rows = new StringBuilder("seconds,pairClock,role,rig,bone,x,y,z,qx,qy,qz,qw," +
                    "attackGuid,attackId,attackSeconds,attackPreviousId,attackPreviousSeconds,attackWeight," +
                    "reactionGuid,reactionId,reactionSeconds,reactionPreviousId,reactionPreviousSeconds,reactionWeight\n");
                var nativeRows = new StringBuilder("seconds,step,bone,localRotationErrorDegrees," +
                    "worldPositionErrorAfterHorizontalCarryMetres,carryX,carryY,carryZ\n");
                var bounds = new Bounds(source.Animator.transform.position, Vector3.zero);
                using (var native = new NativeReference(pair.AttackerActor, sources, record))
                {
                    foreach (float seconds in times)
                    {
                        pair.EvaluateAt(seconds);
                        CheckEquipment(pair, fighters);
                        string clocks = SourceClocks(seconds, record);
                        foreach (var node in nodes)
                        {
                            var p = node.node.position;
                            var q = node.node.rotation;
                            if (!Finite(p) || !float.IsFinite(q.x) || !float.IsFinite(q.y) ||
                                !float.IsFinite(q.z) || !float.IsFinite(q.w))
                                throw new InvalidOperationException("Nonfinite KB pose at " + seconds);
                            if (node.rig == "fighter")
                                bounds.Encapsulate(p);
                            rows.AppendLine(FormattableString.Invariant(
                                $"{seconds:R},{pair.SampleTime:R},{node.role},{node.rig},{node.bone},{p.x:R},{p.y:R},{p.z:R},{q.x:R},{q.y:R},{q.z:R},{q.w:R},{clocks}"));
                        }
                        native.Compare(seconds, nativeRows);
                    }
                }
                record.sampleCount = times.Length;
                File.WriteAllText(Output + "/" + record.trajectory, rows.ToString());
                File.WriteAllText(Output + "/" + record.nativeEquivalence, nativeRows.ToString());
                var saved = ReadCsv(Output + "/" + record.trajectory, nodes);
                WriteSeams(Output + "/" + record.seams, saved, nodes);
                Action<float> verify = seconds => Verify(pair, fighters, nodes, saved[seconds], seconds, record);
                foreach (float seconds in times.Reverse())
                {
                    verify(seconds);
                    record.backwardsSeekSamples++;
                }
                bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
                bounds.Expand(.45f);
                using var rendering = new CandidateRendering(source.gameObject.scene, fighters, pair);
                record.sheets = rendering.Write(Output + "/" + stem, bounds, record, verify);
                File.WriteAllText(Output + "/" + stem + ".json", JsonUtility.ToJson(record, true));
                return record;
            }
            finally
            {
                if (pair)
                    pair.Cancel();
                else if (source.SourcePlayback)
                    source.SourcePlayback.Cancel();
            }
        }

        static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);

        static string SourceClocks(float seconds, CandidateRecord record)
        {
            return Clock(seconds, record.attacks, false) + "," + Clock(seconds, record.reactions, true);
        }

        static string Clock(float seconds, TrackRecord[] tracks, bool reaction)
        {
            int index = 0;
            while (index + 1 < tracks.Length && seconds >= tracks[index + 1].seconds)
                index++;
            var active = tracks[index];
            float time = Mathf.Clamp(seconds - active.seconds, 0, active.sourceEndSeconds - .00001f);
            float weight = active.blendSeconds == 0 ? 1 :
                Mathf.SmoothStep(0, 1, Mathf.Clamp01((seconds - active.seconds) / active.blendSeconds));
            var prior = index > 0 ? tracks[index - 1] : active;
            float priorTime = reaction && index == 0 ? 0 :
                Mathf.Clamp(seconds - prior.seconds, 0, prior.sourceEndSeconds - .00001f);
            return FormattableString.Invariant(
                $"{active.guid},{active.localId},{time:R},{prior.localId},{priorTime:R},{weight:R}");
        }
    }
}
