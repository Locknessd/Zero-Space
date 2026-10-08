using UnityEngine;

public partial class CharacterCombat
{
    public bool BeginSourceIdleRecovery(FrankRetarget.FrankBattlePairPlayback owner, int playbackId)
    {
        if (!owner || !owner.Playing || SourcePlayback != owner || PlaybackId != playbackId ||
            !IsBusy || IsDead || !isActiveAndEnabled || !idleAnim ||
            !float.IsFinite(idleAnim.length) || idleAnim.length <= 0 || !Initialize()) return false;
        int idleState = UnityEngine.Animator.StringToHash("Base Layer.Idle");
        if (!animator.isActiveAndEnabled || animator.runtimeAnimatorController != _overrideController ||
            !animator.HasState(0, idleState)) return false;
        var idleSlot = ResolveSlot(null, "IdlePlaceholder");
        if (!idleSlot || _overrideController[idleSlot] != idleAnim) return false;
        // Observe Idle from the pair's controller clock; no Animator callback may
        // complete this role or release its original sequence ownership early.
        _expectedState = 0;
        LastSequenceSucceeded = false;
        animator.speed = 1f;
        animator.SetFloat("Speed", 0f);
        animator.ResetTrigger("TriggerAttack");
        animator.ResetTrigger("TriggerHit");
        animator.ResetTrigger("TriggerVictory");
        animator.Play(idleState, 0, 0f);
        animator.Update(0f);
        return true;
    }
}
