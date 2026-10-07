using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FrankRetarget
{
    public sealed partial class FrankTestActor
    {
        AnimationMixerPlayable continuationMixer;
        AnimationClipPlayable continuationPlayable;
        AnimationClip continuationClip;
        float continuationStart, continuationBlend;

        // Both graph inputs are sampled by Evaluate using the existing pair clock.
        // Keeping the entry input makes arbitrary preview sampling deterministic.
        public void SetContinuation(AnimationClip next, float seconds, float blend)
        {
            if (!graph.IsValid()) return;
            if (continuationPlayable.IsValid())
            {
                continuationMixer.DisconnectInput(1);
                graph.DestroyPlayable(continuationPlayable);
            }
            continuationClip = next;
            if (!next) return;
            if (!continuationMixer.IsValid())
            {
                continuationMixer = AnimationMixerPlayable.Create(graph, 2);
                graph.Connect(playable, 0, continuationMixer, 0);
                var output = (AnimationPlayableOutput)graph.GetOutput(0);
                output.SetSourcePlayable(continuationMixer);
            }
            continuationPlayable = AnimationClipPlayable.Create(graph, next);
            continuationPlayable.SetApplyFootIK(false);
            continuationPlayable.SetApplyPlayableIK(false);
            continuationPlayable.SetSpeed(0);
            graph.Connect(continuationPlayable, 0, continuationMixer, 1);
            continuationStart = seconds;
            continuationBlend = Mathf.Max(.001f, blend);
        }

        void SampleContinuation(float time)
        {
            if (!continuationMixer.IsValid()) return;
            float weight = continuationClip
                ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((time - continuationStart) / continuationBlend)) : 0;
            continuationMixer.SetInputWeight(0, 1 - weight);
            continuationMixer.SetInputWeight(1, weight);
            if (!continuationClip) return;
            playable.SetTime(Mathf.Clamp(Mathf.Min(time, continuationStart), 0, clip.length - .00001f));
            continuationPlayable.SetTime(Mathf.Clamp(time - continuationStart, 0, continuationClip.length - .00001f));
        }
    }
}
