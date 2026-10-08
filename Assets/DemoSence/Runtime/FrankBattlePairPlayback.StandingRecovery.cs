using UnityEngine;

namespace FrankRetarget
{
    public sealed partial class FrankBattlePairPlayback
    {
        bool attackStandingRecovery;
        bool hitStandingRecovery;

        void ResetStandingRecovery()
        {
            attackStandingRecovery = false;
            hitStandingRecovery = false;
        }

        void ObserveStandingRecovery()
        {
            if (!IsRecovering || pair == null) return;
            if (attackStandingRecovery && attackRecoveryPending)
                ObserveStandingRole(attacker, attackPlaybackId, ref attackRecoveryPending);
            if (hitStandingRecovery && hitRecoveryPending)
                ObserveStandingRole(receiver, hitPlaybackId, ref hitRecoveryPending);
        }

        void ObserveStandingRole(CharacterCombat fighter, int playbackId, ref bool pending)
        {
            if (!fighter || !fighter.Animator || fighter.IsDead || fighter.SourcePlayback != this ||
                fighter.PlaybackId != playbackId || !fighter.IsBusy || !fighter.idleAnim)
            {
                recoveryFailed = true;
                return;
            }
            var state = fighter.Animator.GetCurrentAnimatorStateInfo(0);
            float elapsed = state.normalizedTime * fighter.idleAnim.length;
            if (!state.IsName("Base Layer.Idle") || !float.IsFinite(elapsed))
            {
                // Keep pending set so LateUpdate cancels on its next validity pass.
                recoveryFailed = true;
                return;
            }
            if (elapsed >= pair.standingRecoverySeconds) pending = false;
        }
    }
}
