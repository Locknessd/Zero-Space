using UnityEngine;

namespace FrankRetarget
{
    public sealed partial class FrankBattlePairPlayback
    {
        BattleSfxBank.Move slapProfile;

        void ApplySlapContact()
        {
            if (slapProfile?.cues == null || !attackActor || !hitActor)
                return;
            foreach (var cue in slapProfile.cues)
            {
                if (cue == null || cue.group != "light_hit" || cue.avatarContacts == null)
                    continue;
                float weight = 1 - Mathf.SmoothStep(0, 1, Mathf.Abs(SampleTime - cue.seconds) / .18f);
                if (weight <= 0)
                    continue;
                bool calibrated = false;
                foreach (var anchor in cue.avatarContacts)
                    if (anchor != null && anchor.avatar == receiver.Animator.avatar &&
                        anchor.bone == HumanBodyBones.Head)
                        calibrated = true;
                if (!calibrated || !cue.TryContactPosition(receiver.Animator, out var contact))
                    continue;
                Transform hand;
                if (cue.contactSource == "LeftHand")
                    hand = attacker.Animator.GetBoneTransform(HumanBodyBones.LeftHand);
                else if (cue.contactSource == "RightHand")
                    hand = attacker.Animator.GetBoneTransform(HumanBodyBones.RightHand);
                else
                    continue;
                foreach (var limb in attackActor.Pose.limbs)
                {
                    if (limb.end != hand || !limb.targetKnuckle || !limb.targetFingerJoint)
                        continue;
                    // The anchor follows the receiver's animated face. Blend the approach and
                    // release around the authored impact; every evaluation starts from the source pose.
                    limb.maximumStretch = Mathf.Max(limb.maximumStretch, 1.45f);
                    var palm = Vector3.Lerp(limb.end.position, limb.targetKnuckle.position, .7f);
                    limb.Solve(limb.end.position + (contact - palm) * weight);
                }
            }
        }
    }
}
