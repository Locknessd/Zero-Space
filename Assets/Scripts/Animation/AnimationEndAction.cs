using UnityEngine;

public class AnimationEndAction : StateMachineBehaviour
{
    private CharacterCombat _combat;
    private int _playbackId;
    private bool _reported;

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        _combat = animator.GetComponent<CharacterCombat>();
        if (_combat == null) _combat = animator.GetComponentInParent<CharacterCombat>();
        _playbackId = _combat != null ? _combat.PlaybackId : 0;
        _reported = false;
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        if (stateInfo.normalizedTime >= 1f) Report(stateInfo, true);
    }

    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        // A transition/restart can exit before the clip ends. Do not report that as
        // a successful attack or advance a hit sequence to GetUp prematurely.
        Report(stateInfo, stateInfo.normalizedTime >= 1f);
    }

    private void Report(AnimatorStateInfo stateInfo, bool completed)
    {
        if (_reported || _combat == null) return;
        _reported = true;
        _combat.NotifyAnimationEnded(stateInfo.fullPathHash, _playbackId, completed);
    }
}
