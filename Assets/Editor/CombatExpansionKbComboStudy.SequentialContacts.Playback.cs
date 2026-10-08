using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        sealed class SequentialPlayer : IDisposable
        {
            readonly SourceSession session;
            readonly SourceRecord[] sources;
            readonly SequentialCase record;
            readonly Transform[] nodes;
            readonly Vector3[] positions, scales;
            readonly Quaternion[] rotations;
            readonly SkinnedMeshRenderer[] visibleSkins;
            Tracks tracks;
            public FrankBattlePairPlayback Pair { get; private set; }
            public CharacterCombat[] Fighters => session.Fighters;
            public CharacterCombat Source => Fighters[record.assignment];
            public CharacterCombat Target => Fighters[1 - record.assignment];

            public SequentialPlayer(SourceRecord[] sources, SequentialCase record)
            {
                this.sources = sources;
                this.record = record;
                session = new SourceSession();
                try
                {
                    typeof(CombatPositioningController).GetProperty("Instance",
                        BindingFlags.Public | BindingFlags.Static).SetValue(null, null);
                    foreach (var fighter in Fighters)
                    {
                        fighter.battleSfx = null;
                        fighter.battleVfx = null;
                        fighter.hitEffect = null;
                        if (!fighter.Initialize())
                            throw new InvalidOperationException("Cannot initialize " + fighter.name);
                    }
                    nodes = Fighters.SelectMany(f => f.GetComponentsInChildren<Transform>(true)).Distinct().ToArray();
                    positions = nodes.Select(n => n.localPosition).ToArray();
                    rotations = nodes.Select(n => n.localRotation).ToArray();
                    scales = nodes.Select(n => n.localScale).ToArray();
                    visibleSkins = Fighters.SelectMany(f =>
                        f.Animator.GetComponentsInChildren<SkinnedMeshRenderer>()).Where(s =>
                        s.enabled && s.gameObject.activeInHierarchy).ToArray();
                }
                catch
                {
                    session.Dispose();
                    throw;
                }
            }

            // Pending strikes are strictly beyond all search windows. Missed strikes are omitted.
            public void Rebuild(int nextStrike)
            {
                if (Pair)
                    Pair.Cancel();
                Pair = null;
                tracks?.Dispose();
                foreach (var fighter in Fighters)
                    fighter.ResetCombat();
                for (int index = 0; index < nodes.Length; index++)
                {
                    nodes[index].SetLocalPositionAndRotation(positions[index], rotations[index]);
                    nodes[index].localScale = scales[index];
                }
                tracks = new Tracks(sources);
                for (int index = 0; index < 3; index++)
                {
                    tracks.attacks.steps[index].sourceEndSeconds = sources[index].length;
                    if (index > 0)
                        tracks.attacks.steps[index].blendSeconds = record.linkBlendSeconds;
                }
                var reactions = new List<FrankReactionTrack.Segment>();
                for (int index = 0; index < 3; index++)
                {
                    if (index < nextStrike && record.onsets[index] < 0)
                        continue;
                    var segment = tracks.reactions.segments[index];
                    segment.seconds = index < nextStrike ? record.onsets[index] : 2 + index;
                    segment.terminal = false;
                    reactions.Add(segment);
                }
                // The runtime requires one reaction. This disabled sentinel is beyond the captured attack.
                if (reactions.Count == 0)
                    reactions.Add(new FrankReactionTrack.Segment
                    {
                        strikeId = "disabled_no_contact", clip = sources[3].clip, seconds = 2,
                        blendSeconds = .035f, lethalHoldSeconds = -1
                    });
                reactions[reactions.Count - 1].terminal = true;
                tracks.reactions.segments = reactions.ToArray();
                var move = Move(Source, Target, sources, tracks, record.spacing);
                move.sourcePair.entryBlendSeconds = record.entryBlendSeconds;
                var rotation = Quaternion.LookRotation(Vector3.right * record.lane);
                Source.transform.position = Vector3.left * record.lane * record.spacing / 2;
                Target.transform.position = Vector3.right * record.lane * record.spacing / 2;
                Source.Animator.transform.SetPositionAndRotation(Source.transform.position, rotation);
                Target.Animator.transform.SetPositionAndRotation(Target.transform.position,
                    rotation * Quaternion.Euler(0, 180, 0));
                if (!Source.ExecuteAttack(move, Target, false) || !Source.SourcePlayback.Playing)
                    throw new InvalidOperationException("Rejected sequential candidate " + record.name);
                Pair = Source.SourcePlayback;
                Pair.AttackerActor.Pose.transferFingers = true;
                Pair.ReceiverActor.Pose.transferFingers = true;
                Pair.EvaluateAt(0);
                CheckEquipment(Pair, Fighters);
                record.duration = Mathf.Max(tracks.attacks.Duration,
                    record.onsets.Max() >= 0 ? record.onsets.Max() + sources[4].length : 0);
                record.attacks = tracks.attacks.steps.Select((step, i) => new TrackRecord
                {
                    id = step.stepId, guid = sources[i].guid, localId = sources[i].localId,
                    seconds = step.seconds, sourceStartSeconds = step.sourceStartSeconds,
                    sourceEndSeconds = step.sourceEndSeconds, blendSeconds = step.blendSeconds
                }).ToArray();
                record.reactions = tracks.reactions.segments.Select(segment => new TrackRecord
                {
                    id = segment.strikeId, guid = ReactionGuid,
                    localId = segment.clip == sources[4].clip ? sources[4].localId : sources[3].localId,
                    seconds = segment.seconds, sourceEndSeconds = segment.clip.length,
                    blendSeconds = segment.blendSeconds, terminal = segment.terminal
                }).ToArray();
            }

            public SequentialFloor Sample(float seconds)
            {
                Pair.EvaluateAt(seconds);
                // CPU preview proxies hide originals after each rendered frame. Floor selection must
                // still use the originally visible body, and restore renderer state before rendering.
                var enabled = visibleSkins.Select(s => s.enabled).ToArray();
                try
                {
                    foreach (var skin in visibleSkins)
                        skin.enabled = true;
                    return GroundCurrentPose();
                }
                finally
                {
                    for (int index = 0; index < visibleSkins.Length; index++)
                        visibleSkins[index].enabled = enabled[index];
                }
            }

            SequentialFloor GroundCurrentPose()
            {
                var floor = new SequentialFloor
                {
                    attackerRaw = BattlePresentationContactSetup.MeasureGroundClearance(Source),
                    receiverRaw = BattlePresentationContactSetup.MeasureGroundClearance(Target)
                };
                floor.attackerLift = Lift(Source, floor.attackerRaw);
                floor.receiverLift = Lift(Target, floor.receiverRaw);
                floor.attackerAfter = BattlePresentationContactSetup.MeasureGroundClearance(Source);
                floor.receiverAfter = BattlePresentationContactSetup.MeasureGroundClearance(Target);
                if (!float.IsFinite(floor.attackerAfter) || !float.IsFinite(floor.receiverAfter) ||
                    floor.attackerAfter < .009f || floor.receiverAfter < .009f)
                    throw new InvalidOperationException("Instantaneous ground clearance verification failed.");
                return floor;
            }

            static float Lift(CharacterCombat fighter, float minimum)
            {
                if (!float.IsFinite(minimum))
                    throw new InvalidOperationException("Nonfinite visible-body floor clearance.");
                float lift = Mathf.Max(0, .01f - minimum);
                if (lift > .2f)
                    throw new InvalidOperationException("Required instantaneous floor lift exceeds .2m: " + lift);
                var hips = fighter.Animator.GetBoneTransform(HumanBodyBones.Hips);
                if (!hips)
                    throw new InvalidOperationException("Missing grounding hips.");
                hips.position += Vector3.up * lift;
                return lift;
            }

            public void Dispose()
            {
                try
                {
                    if (Pair)
                        Pair.Cancel();
                }
                finally
                {
                    tracks?.Dispose();
                    session.Dispose();
                }
            }
        }
    }
}
