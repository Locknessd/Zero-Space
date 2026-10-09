using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FrankRetarget
{
    public sealed partial class FrankTestActor
    {
        FrankAttackTrack.Step[] attackSteps;
        AnimationClipPlayable[] attackPlayables;
        AnimationMixerPlayable[] attackMixers;
        Vector3[] attackOffsets;
        double[] attackSampleTimes;
        ReactionSamplingPose attackSourcePose;
        ReactionSamplingPose attackTargetPose;

        public void SetAttackTrack(FrankAttackTrack track)
        {
            if (track && !track.TryValidate(out string error))
                throw new ArgumentException(error, nameof(track));
            if (track && (!activeDriver || !graph.IsValid()))
                throw new InvalidOperationException("Configure the attacker before assigning its attack track.");
            if (track && (reactionSegments != null || continuationMixer.IsValid()))
                throw new InvalidOperationException("Attack tracks cannot share reaction or grapple continuation tracks.");
            ClearAttackTrack();
            if (!track) return;

            int count = track.steps.Length;
            attackSteps = new FrankAttackTrack.Step[count];
            attackPlayables = new AnimationClipPlayable[count];
            attackMixers = new AnimationMixerPlayable[count];
            attackOffsets = new Vector3[count];
            attackSampleTimes = new double[count];
            try
            {
                ResetSourceWeaponGrounding();
                attackSourcePose = new ReactionSamplingPose(activeDriver.transform, true);
                attackTargetPose = new ReactionSamplingPose(character.transform, false);
                Playable previous = playable;
                for (int i = 0; i < count; i++)
                {
                    var authored = track.steps[i];
                    attackSteps[i] = new FrankAttackTrack.Step
                    {
                        stepId = authored.stepId,
                        clip = authored.clip,
                        seconds = authored.seconds,
                        sourceStartSeconds = authored.sourceStartSeconds,
                        sourceEndSeconds = authored.sourceEndSeconds,
                        blendSeconds = authored.blendSeconds
                    };
                    var input = AnimationClipPlayable.Create(graph, authored.clip);
                    attackPlayables[i] = input;
                    input.SetApplyFootIK(false);
                    input.SetApplyPlayableIK(false);
                    input.SetSpeed(0);
                    var mixer = AnimationMixerPlayable.Create(graph, 2);
                    attackMixers[i] = mixer;
                    graph.Connect(previous, 0, mixer, 0);
                    graph.Connect(input, 0, mixer, 1);
                    previous = mixer;
                }
                var output = (AnimationPlayableOutput)graph.GetOutput(0);
                output.SetSourcePlayable(previous);
                CalibrateAttackOffsets();
                Evaluate(0);
            }
            catch
            {
                ClearAttackTrack();
                throw;
            }
        }

        void ClearAttackTrack()
        {
            if (attackSteps == null) return;
            if (graph.IsValid())
            {
                var output = (AnimationPlayableOutput)graph.GetOutput(0);
                output.SetSourcePlayable(playable);
                for (int i = attackMixers.Length - 1; i >= 0; i--)
                    if (attackMixers[i].IsValid()) graph.DestroyPlayable(attackMixers[i]);
                for (int i = 0; i < attackPlayables.Length; i++)
                    if (attackPlayables[i].IsValid()) graph.DestroyPlayable(attackPlayables[i]);
                attackSourcePose?.Restore();
            }
            attackSteps = null;
            attackPlayables = null;
            attackMixers = null;
            attackOffsets = null;
            attackSampleTimes = null;
            attackSourcePose = null;
            attackTargetPose = null;
        }
    }
}
