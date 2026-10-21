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
            public bool replacesReceiverFloorLift;

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

        public bool ReplacesReceiverFloorLift(Avatar avatar)
        {
            if (!avatar || tracks == null) return false;
            foreach (var track in tracks)
                if (track != null && track.avatar == avatar && track.receiver &&
                    track.replacesReceiverFloorLift && track.duration > 0 && track.lift != null && track.lift.Length > 1)
                    return true;
            return false;
        }

        public void Apply(float seconds, Animator attacker, Animator receiver, FrankTestActor attackerSource = null)
        {
            float attackLift = ApplyActor(seconds, attacker, false);
            ApplyActor(seconds, receiver, true);
            if (attackerSource) attackerSource.ApplySourceWeaponGrounding(attackLift);
        }

        float ApplyActor(float seconds, Animator actor, bool receiver)
        {
            if (!actor)
                return 0;
            foreach (var track in tracks)
            {
                if (track.avatar != actor.avatar || track.receiver != receiver)
                    continue;
                var hips = actor.GetBoneTransform(HumanBodyBones.Hips);
                float lift = track.At(seconds);
                if (hips)
                    hips.position += Vector3.up * lift;
                return lift;
            }
            return 0;
        }
    }
}
