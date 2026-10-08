using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget
{
    [CreateAssetMenu(menuName = "Frank Retarget/Attack Track")]
    public sealed class FrankAttackTrack : ScriptableObject
    {
        [Serializable]
        public sealed class Step
        {
            public string stepId;
            public AnimationClip clip;
            [Min(0)] public float seconds;
            [Min(0)] public float sourceStartSeconds;
            [Min(0)] public float sourceEndSeconds;
            [Min(0)] public float blendSeconds;
        }

        public Step[] steps = Array.Empty<Step>();

        public float Duration
        {
            get
            {
                if (steps == null || steps.Length == 0) return 0;
                var last = steps[steps.Length - 1];
                return last == null ? 0 : last.seconds + (last.sourceEndSeconds - last.sourceStartSeconds);
            }
        }

        public bool TryValidate(out string error)
        {
            error = null;
            if (steps == null || steps.Length == 0)
                return Invalid("An attack track needs at least one step.", out error);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            float latestEnd = 0;
            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                if (step == null)
                    return Invalid($"Attack step {i} is missing.", out error);
                if (string.IsNullOrWhiteSpace(step.stepId) || !identities.Add(step.stepId))
                    return Invalid($"Attack step {i} needs a unique identity.", out error);
                if (!step.clip || step.clip.legacy || !step.clip.isHumanMotion ||
                    !float.IsFinite(step.clip.length) || step.clip.length <= 0)
                    return Invalid($"Attack step {i} needs a nonempty native Humanoid clip.", out error);
                if (!float.IsFinite(step.seconds) || step.seconds < 0 ||
                    !float.IsFinite(step.sourceStartSeconds) || !float.IsFinite(step.sourceEndSeconds) ||
                    step.sourceStartSeconds < 0 || step.sourceEndSeconds <= step.sourceStartSeconds ||
                    step.sourceEndSeconds > step.clip.length)
                    return Invalid($"Attack step {i} needs a finite time and a valid source trim.", out error);
                float length = step.sourceEndSeconds - step.sourceStartSeconds;
                float end = step.seconds + length;
                if (!float.IsFinite(end) || !float.IsFinite(step.blendSeconds) ||
                    step.blendSeconds < 0 || step.blendSeconds > length)
                    return Invalid($"Attack step {i} blend must fit within its trimmed clip.", out error);
                if (i == 0 && (step.seconds != 0 || step.blendSeconds != 0))
                    return Invalid("The first attack step must start at zero without a blend.", out error);
                if (i > 0)
                {
                    var previous = steps[i - 1];
                    float previousLength = previous.sourceEndSeconds - previous.sourceStartSeconds;
                    float previousEnd = previous.seconds + previousLength;
                    if (step.seconds <= previous.seconds)
                        return Invalid($"Attack step {i} must start strictly after the previous step.", out error);
                    if (step.seconds > previousEnd)
                        return Invalid($"Attack step {i} leaves a gap after the previous step.", out error);
                    if (step.blendSeconds > previousLength || step.seconds + step.blendSeconds >= previousEnd)
                        return Invalid($"Attack step {i} must finish blending before the previous trim ends.", out error);
                }
                if (i == steps.Length - 1 && end < latestEnd)
                    return Invalid("The last attack step cannot end before an earlier trimmed step.", out error);
                latestEnd = Mathf.Max(latestEnd, end);
            }
            return true;
        }

        static bool Invalid(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
