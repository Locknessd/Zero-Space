using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FrankRetarget
{
    public sealed partial class FrankTestActor
    {
        FrankReactionTrack.Segment[] reactionSegments;
        AnimationClipPlayable[] reactionPlayables;
        AnimationMixerPlayable[] reactionMixers;
        Vector3[] reactionOffsets;
        double[] reactionSampleTimes;
        ReactionSamplingPose reactionSourcePose;
        ReactionSamplingPose reactionTargetPose;
        bool reactionLethal;

        sealed class ReactionSamplingPose
        {
            readonly Transform[] bones;
            readonly Vector3[] positions;
            readonly Quaternion[] rotations;
            readonly Vector3[] scales;
            readonly Transform excludedRoot;

            public ReactionSamplingPose(Transform root, bool includeRoot)
            {
                bones = root.GetComponentsInChildren<Transform>(true);
                positions = new Vector3[bones.Length];
                rotations = new Quaternion[bones.Length];
                scales = new Vector3[bones.Length];
                excludedRoot = includeRoot ? null : root;
                for (int i = 0; i < bones.Length; i++)
                {
                    positions[i] = bones[i].localPosition;
                    rotations[i] = bones[i].localRotation;
                    scales[i] = bones[i].localScale;
                }
            }

            public void Restore()
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i];
                    if (!bone || bone == excludedRoot) continue;
                    bone.SetLocalPositionAndRotation(positions[i], rotations[i]);
                    bone.localScale = scales[i];
                }
            }
        }

        public void SetReactionTrack(FrankReactionTrack track, bool lethal)
        {
            if (track && !track.TryValidate(out string error))
                throw new ArgumentException(error, nameof(track));
            if (track && (!activeDriver || !graph.IsValid()))
                throw new InvalidOperationException("Configure the receiver before assigning its reaction track.");
            if (track && continuationMixer.IsValid())
                throw new InvalidOperationException("Reaction tracks cannot share a grapple continuation clock.");
            ClearReactionTrack();
            if (!track) return;

            int count = track.segments.Length;
            reactionSegments = new FrankReactionTrack.Segment[count];
            reactionPlayables = new AnimationClipPlayable[count];
            reactionMixers = new AnimationMixerPlayable[count];
            reactionOffsets = new Vector3[count];
            reactionSampleTimes = new double[count];
            reactionLethal = lethal;
            try
            {
                // Capture before calibration visits other clips. Restoring these
                // inputs prevents unwritten translations and IK adjustments from
                // becoming the next sample's bind pose. Actor placement is retained.
                reactionSourcePose = new ReactionSamplingPose(activeDriver.transform, true);
                reactionTargetPose = new ReactionSamplingPose(character.transform, false);
                Playable previous = playable;
                for (int i = 0; i < count; i++)
                {
                    var authored = track.segments[i];
                    // Snapshot authoring data once; evaluation allocates no arrays or playables.
                    reactionSegments[i] = new FrankReactionTrack.Segment
                    {
                        strikeId = authored.strikeId,
                        clip = authored.clip,
                        seconds = authored.seconds,
                        blendSeconds = authored.blendSeconds,
                        terminal = authored.terminal,
                        lethalHoldSeconds = authored.lethalHoldSeconds
                    };
                    var input = AnimationClipPlayable.Create(graph, authored.clip);
                    reactionPlayables[i] = input;
                    input.SetApplyFootIK(false);
                    input.SetApplyPlayableIK(false);
                    input.SetSpeed(0);
                    var mixer = AnimationMixerPlayable.Create(graph, 2);
                    reactionMixers[i] = mixer;
                    graph.Connect(previous, 0, mixer, 0);
                    graph.Connect(input, 0, mixer, 1);
                    previous = mixer;
                }
                var output = (AnimationPlayableOutput)graph.GetOutput(0);
                output.SetSourcePlayable(previous);
                CalibrateReactionOffsets();
                Evaluate(0);
            }
            catch
            {
                ClearReactionTrack();
                throw;
            }
        }

        void CalibrateReactionOffsets()
        {
            for (int i = 0; i < reactionSegments.Length; i++)
            {
                float contact = reactionSegments[i].seconds;
                Vector3 carried = SampleReactions(contact, i);
                EvaluateReactionGraph();
                Vector3 prior = transform.InverseTransformPoint(Pose.sourceHips.position) + carried;
                // Isolate the incoming clip at native time zero in the same graph.
                reactionMixers[i].SetInputWeight(0, 0);
                reactionMixers[i].SetInputWeight(1, 1);
                EvaluateReactionGraph();
                Vector3 origin = transform.InverseTransformPoint(Pose.sourceHips.position);
                Vector3 offset = prior - origin;
                offset.y = 0;
                reactionOffsets[i] = offset;
            }
        }

        Vector3 SampleReactions(float time, int count)
        {
            playable.SetTime(0);
            Vector3 offset = Vector3.zero;
            for (int i = 0; i < reactionSegments.Length; i++)
            {
                var segment = reactionSegments[i];
                float elapsed = Mathf.Max(0, time - segment.seconds);
                float sourceTime = elapsed;
                if (reactionLethal && segment.terminal && segment.lethalHoldSeconds >= 0)
                    sourceTime = Mathf.Min(sourceTime, segment.lethalHoldSeconds);
                reactionPlayables[i].SetTime(Mathf.Clamp(sourceTime, 0, segment.clip.length - .00001f));
                float weight = i >= count || time < segment.seconds ? 0 : segment.blendSeconds == 0
                    ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / segment.blendSeconds));
                reactionMixers[i].SetInputWeight(0, 1 - weight);
                reactionMixers[i].SetInputWeight(1, weight);
                offset = Vector3.Lerp(offset, reactionOffsets[i], weight);
            }
            return offset;
        }

        void EvaluateReactionGraph()
        {
            // Humanoid root motion is a delta from the preceding graph sample.
            // Prime every input at its origin before measuring this absolute pose.
            // Keeping mixer weights unchanged makes root displacement blend with
            // the same weights as the muscles, without a history dependent delta.
            for (int i = 0; i < reactionPlayables.Length; i++)
            {
                reactionSampleTimes[i] = reactionPlayables[i].GetTime();
                reactionPlayables[i].SetTime(0);
            }
            graph.Evaluate(0);
            reactionSourcePose.Restore();
            for (int i = 0; i < reactionPlayables.Length; i++)
                reactionPlayables[i].SetTime(reactionSampleTimes[i]);
            graph.Evaluate(0);
        }

        void PrepareReactionTarget()
        {
            reactionTargetPose.Restore();
        }

        void ApplyReactionOffset(Vector3 offset)
        {
            // Never feed continuity offsets into the native Animator. Place the
            // visible hips absolutely from this sample, after solving its limbs.
            // The actor-space offset already includes calibrated source scale.
            Vector3 position = Pose.sourceHips.position + transform.TransformVector(offset);
            if (Pose.constrainTargetHipsDepth) position.z = Pose.targetHips.position.z;
            Pose.targetHips.position = position;
        }

        void ClearReactionTrack()
        {
            if (reactionSegments == null) return;
            if (graph.IsValid())
            {
                var output = (AnimationPlayableOutput)graph.GetOutput(0);
                output.SetSourcePlayable(playable);
                for (int i = reactionMixers.Length - 1; i >= 0; i--)
                    if (reactionMixers[i].IsValid()) graph.DestroyPlayable(reactionMixers[i]);
                for (int i = 0; i < reactionPlayables.Length; i++)
                    if (reactionPlayables[i].IsValid()) graph.DestroyPlayable(reactionPlayables[i]);
            }
            reactionSegments = null;
            reactionPlayables = null;
            reactionMixers = null;
            reactionOffsets = null;
            reactionSampleTimes = null;
            reactionSourcePose = null;
            reactionTargetPose = null;
            reactionLethal = false;
        }
    }
}
