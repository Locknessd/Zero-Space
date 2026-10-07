using System;
using UnityEngine;

namespace FrankRetarget
{
    // Authored from evaluated skin surfaces; root motion remains owned by the paired player.
    public sealed class FrankPairGrounding : ScriptableObject
    {
        [Serializable]
        public sealed class Track
        {
            public Avatar avatar;
            public bool receiver;
            public float duration;
            public float[] lift = Array.Empty<float>();

            public float At(float seconds)
            {
                if (lift.Length == 0 || duration <= 0)
                    return 0;
                float frame = Mathf.Clamp01(seconds / duration) * (lift.Length - 1);
                int index = Mathf.Min((int)frame, lift.Length - 1);
                return Mathf.Lerp(lift[index], lift[Mathf.Min(index + 1, lift.Length - 1)], frame - index);
            }
        }

        public Track[] tracks = Array.Empty<Track>();

        public void Apply(float seconds, Animator attacker, Animator receiver)
        {
            ApplyActor(seconds, attacker, false);
            ApplyActor(seconds, receiver, true);
        }

        void ApplyActor(float seconds, Animator actor, bool receiver)
        {
            if (!actor)
                return;
            foreach (var track in tracks)
            {
                if (track.avatar != actor.avatar || track.receiver != receiver)
                    continue;
                var hips = actor.GetBoneTransform(HumanBodyBones.Hips);
                if (hips)
                    hips.position += Vector3.up * track.At(seconds);
                return;
            }
        }
    }
}
