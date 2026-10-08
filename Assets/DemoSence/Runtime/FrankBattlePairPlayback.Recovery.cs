using UnityEngine;

namespace FrankRetarget
{
    public sealed partial class FrankBattlePairPlayback
    {
        public bool IsRecovering => Playing && waitingForGetUp;
        bool waitingForGetUp;
        bool attackRecoveryPending, hitRecoveryPending, recoveryFailed;
        bool attackRecoveryStarted, hitRecoveryStarted;
        int attackPlaybackId = -1, hitPlaybackId = -1;

        void ResetRecovery()
        {
            ClearRecoveryPoses();
            waitingForGetUp = false;
            attackRecoveryPending = hitRecoveryPending = recoveryFailed = false;
            attackRecoveryStarted = hitRecoveryStarted = false;
            attackPlaybackId = hitPlaybackId = -1;
        }

        bool ParticipantsValid() => attacker && receiver && attacker.isActiveAndEnabled &&
            receiver.isActiveAndEnabled && attacker.Animator && receiver.Animator &&
            attacker.PlaybackId == attackPlaybackId && receiver.PlaybackId == hitPlaybackId &&
            attacker.SourcePlayback == this && receiver.SourcePlayback == this &&
            !attacker.IsDead && (lethal || !receiver.IsDead) &&
            (waitingForGetUp || attackActor && attackActor.isActiveAndEnabled &&
                hitActor && hitActor.isActiveAndEnabled) &&
            (!attackRecoveryStarted || attacker.Animator.isActiveAndEnabled) &&
            (!hitRecoveryStarted || receiver.Animator.isActiveAndEnabled);

        void Update()
        {
            // Unity evaluates both controllers using the existing presentation clock.
            if (!IsRecovering) return;
            PrepareRecoveryPoseEvaluation();
            float rate = Application.isPlaying && Time.timeScale <= 0 ? 0 : PresentationRate;
            if (attackRecoveryStarted && attacker && attacker.Animator) attacker.Animator.speed = rate;
            if (hitRecoveryStarted && receiver && receiver.Animator) receiver.Animator.speed = rate;
        }

        // Latch controller results here; restore models and publish completion from
        // LateUpdate, outside the Animator callback. Failed exits never become success.
        public bool NotifySourceRecoveryEnded(CharacterCombat fighter, int playbackId, bool succeeded)
        {
            if (!IsRecovering || !fighter || fighter.SourcePlayback != this ||
                fighter.PlaybackId != playbackId) return false;
            if (fighter == attacker && playbackId == attackPlaybackId && attackRecoveryPending)
                attackRecoveryPending = false;
            else if (fighter == receiver && playbackId == hitPlaybackId && hitRecoveryPending)
                hitRecoveryPending = false;
            else return false;
            if (!succeeded) recoveryFailed = true;
            return true;
        }

        void CompleteSourceMotion()
        {
            CaptureRecoveryPoses();
            bool hasAttackX = TryGetHipsX(attackActor, out float attackX);
            bool hasHitX = TryGetHipsX(hitActor, out float hitX);
            ClearActor(ref attackActor);
            attackModel?.Restore();
            attackModel = null;
            if (hasAttackX) PreserveRootX(attacker ? attacker.Animator : null, attackX);
            if (!lethal)
            {
                ClearActor(ref hitActor);
                hitModel?.Restore();
                hitModel = null;
                if (hasHitX) PreserveRootX(receiver ? receiver.Animator : null, hitX);
            }
            // Register both requested recoveries before entering either controller.
            // BeginSourceGetUp retains the original participant playback IDs.
            attackRecoveryPending = pair.attackerGetUp;
            hitRecoveryPending = !lethal && RecoveryClip;
            waitingForGetUp = attackRecoveryPending || hitRecoveryPending;
            if (attackRecoveryPending)
            {
                attackRecoveryStarted = attacker && attacker.BeginSourceGetUp(pair.attackerGetUp);
                if (attackRecoveryPose != null && hasAttackX)
                    PreserveRootX(attacker ? attacker.Animator : null, attackX);
                if (!attackRecoveryStarted) recoveryFailed = true;
            }
            if (hitRecoveryPending)
            {
                hitRecoveryStarted = receiver && receiver.BeginSourceGetUp(RecoveryClip);
                if (hitRecoveryPose != null && hasHitX)
                    PreserveRootX(receiver ? receiver.Animator : null, hitX);
                if (!hitRecoveryStarted) recoveryFailed = true;
            }
            if (recoveryFailed)
            {
                Cancel();
                return;
            }
            if (waitingForGetUp)
            {
                if (battleSfx) battleSfx.BeginRecovery(this, attackRecoveryStarted);
                CombatPositioningController.Instance?.ConstrainDepthNow();
                ConstrainLightHipsDepth();
                RebaseRecoveryPoses();
                Update();
                EvaluateRecoveryPose();
                return;
            }
            CompleteNow();
        }

        void CompleteNow()
        {
            ClearRecoveryPoses();
            Playing = false;
            waitingForGetUp = false;
            if (battleSfx) battleSfx.EndSequence(this);
            if (battleVfx) battleVfx.EndSequence(this);
            RestoreRecoverySpeeds();
            ReleaseEquipment();
            attackRecoveryStarted = hitRecoveryStarted = false;
            PublishPairCompletion(true);
            // Retain only an accepted lethal receiver's source actor/model. The
            // existing SourcePlayback reference lets reset restore that terminal pose.
        }

        void RestoreRecoverySpeeds()
        {
            if (attacker && attacker.Animator && attacker.SourcePlayback == this &&
                attacker.PlaybackId == attackPlaybackId)
                attacker.Animator.speed = attacker.IsDead ? 0 : 1;
            if (receiver && receiver.Animator && receiver.SourcePlayback == this &&
                receiver.PlaybackId == hitPlaybackId)
                receiver.Animator.speed = receiver.IsDead ? 0 : 1;
        }

        void PublishPairCompletion(bool succeeded)
        {
            // Make both participants available before either queue callback runs.
            // Snapshot IDs/references because a subscriber may reset or start a pair.
            var source = attacker;
            var target = receiver;
            int sourceId = attackPlaybackId;
            int targetId = hitPlaybackId;
            bool notifySource = source && source.PrepareSourceCompletion(this, sourceId, succeeded);
            bool notifyTarget = target && target.PrepareSourceCompletion(this, targetId, succeeded);
            if (notifySource) source.PublishSourceCompletion(sourceId, succeeded);
            if (notifyTarget) target.PublishSourceCompletion(targetId, succeeded);
        }

        public void Cancel()
        {
            ClearRecoveryPoses();
            if (battleSfx) battleSfx.EndSequence(this, true);
            if (battleVfx) battleVfx.EndSequence(this, true);
            bool interrupted = Playing;
            Playing = false;
            waitingForGetUp = false;
            ClearActor(ref attackActor);
            ClearActor(ref hitActor);
            attackModel?.Restore();
            hitModel?.Restore();
            attackModel = hitModel = null;
            // Source models were already restored when recovery began. Interrupt
            // those controllers too, after disabling ownership callback handling.
            StopRecovery(attacker, attackRecoveryStarted, attackPlaybackId);
            StopRecovery(receiver, hitRecoveryStarted, hitPlaybackId);
            RestoreRecoverySpeeds();
            ReleaseEquipment();
            attackRecoveryPending = hitRecoveryPending = recoveryFailed = false;
            attackRecoveryStarted = hitRecoveryStarted = false;
            if (interrupted) PublishPairCompletion(false);
        }

        void StopRecovery(CharacterCombat fighter, bool started, int playbackId)
        {
            if (!started || !fighter || !fighter.Animator || fighter.SourcePlayback != this ||
                fighter.PlaybackId != playbackId) return;
            // Invalidate GetUp callbacks before Play/Update can exit the state.
            fighter.InvalidateSourceRecovery(this, playbackId);
            fighter.Animator.Play("Base Layer.Idle", 0, 0);
            if (fighter.isActiveAndEnabled && fighter.Animator.isActiveAndEnabled) fighter.Animator.Update(0);
        }

        static bool TryGetHipsX(FrankTestActor actor, out float x)
        {
            x = 0f;
            if (!actor || actor.Pose == null || !actor.Pose.targetHips) return false;
            x = actor.Pose.targetHips.position.x;
            return float.IsFinite(x);
        }

        static void PreserveRootX(Animator animator, float finalHipsX)
        {
            if (!animator || !float.IsFinite(finalHipsX)) return;
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (!hips) return;
            float delta = finalHipsX - hips.position.x;
            if (Mathf.Abs(delta) <= 0.00001f) return;
            Vector3 root = animator.transform.position;
            root.x += delta;
            animator.transform.position = root;
        }
    }
}
