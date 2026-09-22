using UnityEngine;

/// <summary>
/// Attach to an Animator STATE (StateMachineBehaviour) to signal when that state's clip has finished.
///
/// WHY THIS EXISTS
/// The queue used to GUESS how long a clip would take (state length, idle fallback, safety caps). That
/// is why a getup could fire while a hit reaction was still on screen, and why a 6s heavy hit was read
/// as ~1.0s. This behaviour reports the ACTUAL end of the state, so the queue advances on a real event
/// instead of a timer.
///
/// USAGE (add to the attack / hit / getup states in the Animator):
///   1. Select a state in the Animator window.
///   2. Add Behaviour -> "Animation End Action".
///   3. Nothing else -- it finds the CharacterAnimatorBridge on the same object and notifies it.
///
/// NOTE: OnStateExit also fires when the state is INTERRUPTED by a transition. That is intentional: an
/// interrupted clip is over as far as the queue is concerned, and waiting longer would stall the flow.
/// </summary>
public class AnimationEndAction : StateMachineBehaviour
{
    [Tooltip("Log every state enter/exit (verbose debugging). Messages are prefixed with 'Animation'.")]
    public bool verboseLogging = false;

    // Cached so the per-state path stays allocation-free.
    private CharacterAnimatorBridge _bridge;

    private CharacterAnimatorBridge ResolveBridge(Animator animator)
    {
        if (_bridge != null || animator == null) return _bridge;

        _bridge = animator.GetComponent<CharacterAnimatorBridge>();
        if (_bridge == null) _bridge = animator.GetComponentInParent<CharacterAnimatorBridge>();
        return _bridge;
    }

    override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        var bridge = ResolveBridge(animator);
        if (bridge != null) bridge.NotifyAnimationStateEntered(stateInfo.shortNameHash);

        if (verboseLogging)
            Debug.Log($"Animation [AnimationEndAction] state ENTER hash={stateInfo.shortNameHash} on '{animator?.name}'", animator);
    }

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        var bridge = ResolveBridge(animator);
        if (bridge != null) bridge.NotifyAnimationStateExited(stateInfo.shortNameHash);

        if (verboseLogging)
            Debug.Log($"Animation [AnimationEndAction] state EXIT hash={stateInfo.shortNameHash} on '{animator?.name}'", animator);
    }
}