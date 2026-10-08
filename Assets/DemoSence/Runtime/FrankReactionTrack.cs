using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget
{
    [CreateAssetMenu(menuName = "Frank Retarget/Reaction Track")]
    public sealed class FrankReactionTrack : ScriptableObject
    {
        [Serializable]
        public sealed class Segment
        {
            public string strikeId;
            public AnimationClip clip;
            [Min(0)] public float seconds;
            [Min(0)] public float blendSeconds = .05f;
            public bool terminal;
            public float lethalHoldSeconds = -1;
        }

        public Segment[] segments = Array.Empty<Segment>();

        // Earlier clips are interrupted by subsequent contacts. The final clip
        // determines the end of this successful action's reaction sequence.
        public float Duration
        {
            get
            {
                if (segments == null || segments.Length == 0) return 0;
                var last = segments[segments.Length - 1];
                return last != null && last.clip ? last.seconds + last.clip.length : 0;
            }
        }

        public bool TryValidate(out string error)
        {
            error = null;
            if (segments == null || segments.Length == 0)
                return Invalid("A reaction track needs at least one contact.", out error);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            float previous = 0;
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (segment == null)
                    return Invalid($"Reaction segment {i} is missing.", out error);
                if (string.IsNullOrWhiteSpace(segment.strikeId) || !identities.Add(segment.strikeId))
                    return Invalid($"Reaction segment {i} needs a unique strike identity.", out error);
                if (!float.IsFinite(segment.seconds) || segment.seconds < previous)
                    return Invalid($"Reaction segment {i} needs a finite, ordered, nonnegative time.", out error);
                if (!segment.clip || !float.IsFinite(segment.clip.length) || segment.clip.length <= 0)
                    return Invalid($"Reaction segment {i} needs a nonempty animation clip.", out error);
                if (!float.IsFinite(segment.seconds + segment.clip.length))
                    return Invalid($"Reaction segment {i} exceeds the supported time range.", out error);
                if (!float.IsFinite(segment.blendSeconds) || segment.blendSeconds < 0 ||
                    segment.blendSeconds > segment.clip.length)
                    return Invalid($"Reaction segment {i} blend must fit within its clip.", out error);
                if (!float.IsFinite(segment.lethalHoldSeconds) ||
                    segment.lethalHoldSeconds != -1 && (segment.lethalHoldSeconds < 0 ||
                    segment.lethalHoldSeconds > segment.clip.length || !segment.terminal))
                    return Invalid($"Reaction segment {i} lethal hold requires a valid terminal source time.", out error);
                if (segment.terminal && i != segments.Length - 1)
                    return Invalid($"Reaction segment {i} is terminal and cannot be followed by another contact.", out error);
                previous = segment.seconds;
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
