using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget
{
    public sealed partial class FrankBattlePairPlayback
    {
        RecoveryPose attackRecoveryPose;
        RecoveryPose hitRecoveryPose;

        void CaptureRecoveryPoses()
        {
            if (pair.recoveryBlendSeconds <= 0 && !pair.recoveryGrounding) return;
            if (pair.attackerGetUp)
                attackRecoveryPose = new RecoveryPose(attacker, pair.attackerGetUp, attackPlaybackId);
            if (!lethal && RecoveryClip)
                hitRecoveryPose = new RecoveryPose(receiver, RecoveryClip, hitPlaybackId);
        }

        void RebaseRecoveryPoses()
        {
            attackRecoveryPose?.Rebase();
            hitRecoveryPose?.Rebase();
        }

        // Manual probes call this before Animator.Update; runtime calls it from Update.
        // Only our last output is removed, and only while the original playback owns it.
        public void PrepareRecoveryPoseEvaluation()
        {
            attackRecoveryPose?.Restore(this);
            hitRecoveryPose?.Restore(this);
        }

        // Call after controller evaluation. The controller state is the only recovery clock.
        public void EvaluateRecoveryPose()
        {
            if (!IsRecovering || pair == null) return;
            attackRecoveryPose?.Evaluate(this, false);
            hitRecoveryPose?.Evaluate(this, true);
        }

        void ClearRecoveryPoses()
        {
            PrepareRecoveryPoseEvaluation();
            attackRecoveryPose = null;
            hitRecoveryPose = null;
        }

        sealed class RecoveryPose
        {
            readonly CharacterCombat fighter;
            readonly Animator animator;
            readonly AnimationClip clip;
            readonly int playbackId;
            readonly Transform hips;
            readonly Transform[] bones;
            readonly Quaternion[] sourceRotations;
            readonly Quaternion[] rawRotations;
            readonly Quaternion[] outputRotations;
            readonly Vector3 sourceHipsWorld;
            readonly RecoveryArm leftArm;
            readonly RecoveryArm rightArm;
            Vector3 sourceHipsLocal;
            Vector3 rawHipsLocal;
            Vector3 outputHipsLocal;
            float appliedNormalizedTime;
            bool applied;

            public RecoveryPose(CharacterCombat fighter, AnimationClip clip, int playbackId)
            {
                this.fighter = fighter;
                this.clip = clip;
                this.playbackId = playbackId;
                animator = fighter.Animator;
                hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                sourceHipsWorld = hips.position;
                leftArm = new RecoveryArm(animator, true);
                rightArm = new RecoveryArm(animator, false);
                var mapped = new List<Transform>();
                for (int index = 0; index < (int)HumanBodyBones.LastBone; index++)
                {
                    var bone = animator.GetBoneTransform((HumanBodyBones)index);
                    if (bone && !mapped.Contains(bone)) mapped.Add(bone);
                }
                bones = mapped.ToArray();
                sourceRotations = new Quaternion[bones.Length];
                rawRotations = new Quaternion[bones.Length];
                outputRotations = new Quaternion[bones.Length];
                for (int index = 0; index < bones.Length; index++)
                    sourceRotations[index] = bones[index].localRotation;
            }

            public void Rebase()
            {
                // The controller root has already absorbed the source displacement.
                // Recompute this coordinate now, rather than moving the endpoint twice.
                sourceHipsLocal = animator.transform.InverseTransformPoint(sourceHipsWorld);
                leftArm.Rebase();
                rightArm.Rebase();
            }

            bool Owns(FrankBattlePairPlayback owner) => fighter && animator && hips &&
                fighter.SourcePlayback == owner && fighter.PlaybackId == playbackId;

            bool OutputStillPresent()
            {
                if ((hips.localPosition - outputHipsLocal).sqrMagnitude > 1e-12f) return false;
                for (int index = 0; index < bones.Length; index++)
                    if (bones[index] && Quaternion.Angle(bones[index].localRotation,
                        outputRotations[index]) > .001f) return false;
                return true;
            }

            public void Restore(FrankBattlePairPlayback owner)
            {
                if (!applied) return;
                // A fresh Animator sample or a new owner must never be rolled back.
                if (Owns(owner) &&
                    animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.GetUp") &&
                    animator.GetCurrentAnimatorStateInfo(0).normalizedTime == appliedNormalizedTime &&
                    OutputStillPresent())
                {
                    for (int index = 0; index < bones.Length; index++)
                        if (bones[index]) bones[index].localRotation = rawRotations[index];
                    hips.localPosition = rawHipsLocal;
                }
                applied = false;
            }

            public void Evaluate(FrankBattlePairPlayback owner, bool receiverRole)
            {
                if (!Owns(owner)) return;
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (!state.IsName("Base Layer.GetUp"))
                {
                    Restore(owner);
                    return;
                }
                float normalized = state.normalizedTime;
                if (!float.IsFinite(normalized)) return;
                if (applied && normalized == appliedNormalizedTime && OutputStillPresent()) return;
                // Changed clock means the controller has provided a new pose. Repeated
                // late evaluations at the same clock instead restore our own last edit.
                if (applied && normalized == appliedNormalizedTime) Restore(owner);
                applied = false;
                rawHipsLocal = hips.localPosition;
                for (int index = 0; index < bones.Length; index++)
                    if (bones[index]) rawRotations[index] = bones[index].localRotation;
                float seconds = Mathf.Clamp01(normalized) * clip.length;
                float lift = GroundingLift(owner.pair.recoveryGrounding, receiverRole, seconds);
                hips.position += Vector3.up * lift;
                float duration = owner.pair.recoveryBlendSeconds;
                float blend = duration > 0 ? Mathf.SmoothStep(0, 1, seconds / duration) : 1;
                if (blend < 1)
                {
                    leftArm.CaptureController();
                    rightArm.CaptureController();
                    for (int index = 0; index < bones.Length; index++)
                        if (bones[index]) bones[index].localRotation = Quaternion.Slerp(
                            sourceRotations[index], rawRotations[index], blend);
                    // Translate the pelvis as a unit. All other bone offsets and scales
                    // remain those of the fresh humanoid pose, preserving bone lengths.
                    hips.position = Vector3.Lerp(animator.transform.TransformPoint(sourceHipsLocal),
                        hips.position, blend);
                    leftArm.Blend(blend);
                    rightArm.Blend(blend);
                }
                // Match the pair's final depth constraint before remembering our
                // output, so that constraint cannot look like a new controller pose.
                if (owner.lightDepthLocked)
                    SetHipsDepth(animator, receiverRole ? owner.lockedHitHipsDepth : owner.lockedAttackHipsDepth);
                outputHipsLocal = hips.localPosition;
                for (int index = 0; index < bones.Length; index++)
                    if (bones[index]) outputRotations[index] = bones[index].localRotation;
                appliedNormalizedTime = normalized;
                applied = true;
            }

            float GroundingLift(FrankPairGrounding grounding, bool receiverRole, float seconds)
            {
                if (!grounding || grounding.tracks == null) return 0;
                foreach (var track in grounding.tracks)
                    if (track != null && track.avatar == animator.avatar && track.receiver == receiverRole &&
                        track.lift != null && Mathf.Abs(track.duration - clip.length) < .001f)
                    {
                        float lift = track.At(seconds);
                        return float.IsFinite(lift) ? Mathf.Max(0, lift) : 0;
                    }
                return 0;
            }
        }
    }
}
