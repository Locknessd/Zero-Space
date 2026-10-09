using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FrankRetarget
{
    public sealed partial class FrankTestActor
    {
        void CalibrateAttackOffsets()
        {
            // The first trim keeps its native origin. Later trims inherit the
            // preceding pose's horizontal travel at their authored start time.
            for (int i = 1; i < attackSteps.Length; i++)
            {
                float start = attackSteps[i].seconds;
                Vector3 carried = SampleAttacks(start, i);
                EvaluateAttackGraph();
                Vector3 prior = transform.InverseTransformPoint(Pose.sourceHips.position) + carried;
                attackMixers[i].SetInputWeight(0, 0);
                attackMixers[i].SetInputWeight(1, 1);
                EvaluateAttackGraph();
                Vector3 origin = transform.InverseTransformPoint(Pose.sourceHips.position);
                Vector3 offset = prior - origin;
                offset.y = 0;
                attackOffsets[i] = offset;
            }
        }

        Vector3 SampleAttacks(float time, int count)
        {
            playable.SetTime(0);
            Vector3 offset = Vector3.zero;
            for (int i = 0; i < attackSteps.Length; i++)
            {
                var step = attackSteps[i];
                float elapsed = Mathf.Max(0, time - step.seconds);
                float end = Mathf.Max(step.sourceStartSeconds, step.sourceEndSeconds - .00001f);
                double sourceTime = Mathf.Clamp(step.sourceStartSeconds + elapsed, step.sourceStartSeconds, end);
                attackPlayables[i].SetTime(sourceTime);
                float weight = i >= count || time < step.seconds ? 0 : step.blendSeconds == 0
                    ? 1 : Mathf.SmoothStep(0, 1, Mathf.Clamp01(elapsed / step.blendSeconds));
                attackMixers[i].SetInputWeight(0, 1 - weight);
                attackMixers[i].SetInputWeight(1, weight);
                offset = Vector3.Lerp(offset, attackOffsets[i], weight);
            }
            return offset;
        }

        void EvaluateAttackGraph()
        {
            // As with reactions, prime native root motion at each clip's zero
            // before restoring the captured source and seeking to the trim sample.
            // Priming preserves the same mixer weights as the muscle evaluation.
            for (int i = 0; i < attackPlayables.Length; i++)
            {
                attackSampleTimes[i] = attackPlayables[i].GetTime();
                attackPlayables[i].SetTime(0);
            }
            graph.Evaluate(0);
            attackSourcePose.Restore();
            for (int i = 0; i < attackPlayables.Length; i++)
                attackPlayables[i].SetTime(attackSampleTimes[i]);
            graph.Evaluate(0);
        }

        void EvaluateAttacks(float time)
        {
            if (reactionSegments != null || continuationMixer.IsValid())
                throw new InvalidOperationException("Attack tracks cannot share reaction or grapple continuation tracks.");
            var output = (AnimationPlayableOutput)graph.GetOutput(0);
            output.SetSourcePlayable(attackMixers[attackMixers.Length - 1]);
            Vector3 offset = SampleAttacks(time, attackSteps.Length);
            EvaluateAttackGraph();
            attackTargetPose.Restore();
            // Apply continuity once, after native evaluation, to the entire source.
            // Retargeting and socket weapons then consume the same translated pose.
            // The next absolute sample restores this root before native evaluation.
            activeDriver.transform.position += transform.TransformVector(offset);
        }
    }
}
