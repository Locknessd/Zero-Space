using UnityEngine;

namespace FrankRetarget
{
    public sealed partial class FrankBattlePairPlayback
    {
        float standingSpacingExpansion;
        float standingSpacingDirection;
        float standingAttackRootX;
        float standingHitRootX;

        void CaptureStandingRecoverySpacing()
        {
            standingSpacingExpansion = 0;
            // A defeated receiver retains its authored terminal pose. Only two
            // standing survivors widen their spacing during their shared recovery.
            if (lethal || !attackStandingRecovery || !hitStandingRecovery || pair.standingRecoverySpacing <= 0)
                return;
            standingAttackRootX = attacker.Animator.transform.position.x;
            standingHitRootX = receiver.Animator.transform.position.x;
            float separation = standingHitRootX - standingAttackRootX;
            standingSpacingDirection = separation < 0 ? -1 : 1;
            standingSpacingExpansion = Mathf.Max(0, pair.standingRecoverySpacing - Mathf.Abs(separation));
        }

        void EvaluateStandingRecoverySpacing()
        {
            if (standingSpacingExpansion <= 0 || pair.standingRecoverySeconds <= 0) return;
            var attackState = attacker.Animator.GetCurrentAnimatorStateInfo(0);
            var hitState = receiver.Animator.GetCurrentAnimatorStateInfo(0);
            if (!attackState.IsName("Base Layer.Idle") || !hitState.IsName("Base Layer.Idle")) return;
            float attackSeconds = attackState.normalizedTime * attacker.idleAnim.length;
            float hitSeconds = hitState.normalizedTime * receiver.idleAnim.length;
            float elapsed = Mathf.Min(attackSeconds, hitSeconds);
            if (!float.IsFinite(elapsed)) return;
            float progress = Mathf.SmoothStep(0, 1, elapsed / pair.standingRecoverySeconds);
            float offset = standingSpacingDirection * standingSpacingExpansion * .5f * progress;
            // Absolute coordinates make repeated evaluation and paused clocks safe.
            // Keep the pair midpoint, floor height and depth lane unchanged.
            SetStandingRootX(attacker.Animator.transform, standingAttackRootX - offset);
            SetStandingRootX(receiver.Animator.transform, standingHitRootX + offset);
        }

        static void SetStandingRootX(Transform root, float x)
        {
            Vector3 position = root.position;
            position.x = x;
            root.position = position;
        }
    }
}
