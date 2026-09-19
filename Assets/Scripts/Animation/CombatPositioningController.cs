using System.Collections;
using UnityEngine;

/// <summary>
/// PROJECT ARCHITECTURE: Presentation / Animation Layer
/// ROLE: Drives the "step to the attack spot" choreography around a hit exchange.
///
/// RESPONSIBILITIES:
/// - After a hit animation resolves, compute the next ATTACK SPOT: a point placed directly
///   in FRONT of the fighter that was hit (the victim), on the victim's facing axis.
/// - Move the ATTACKER to that spot so the next attack lines up naturally.
/// - Recompute the spot after every getup (the victim's facing can change when it stands up).
/// - Cooperate with AnimationController's anchor-recovery system so the two do not fight each
///   other over the fighter transforms.
///
/// AI NOTE:
/// AnimationController.LateUpdate() continuously Lerps both fighters back to fixed anchors
/// (EnablePositionRecovery / managePositions). That system and this one both write transform
/// .position, so whichever runs later wins. This component therefore does NOT move fighters by
/// itself in Update; it moves them and then PAUSES the anchor recovery for the duration of the
/// move (see AnimationController.BeginExternalPositionControl). Without that handshake, the
/// anchor Lerp would drag the attacker back mid-step and the choreography would visibly stutter.
///
/// The attack spot is expressed in the VICTIM's local space (in front of it, at a set distance),
/// so if the victim rotates while getting up, recomputing yields a new world point.
/// </summary>
[DisallowMultipleComponent]
public class CombatPositioningController : MonoBehaviour
{
    public static CombatPositioningController Instance { get; private set; }

    [Header("References")]
    [Tooltip("The AnimationController that owns the fighter animators and the anchor-recovery system. Auto-found when left empty.")]
    [SerializeField] private AnimationController animationController;

    [Header("Attack Spot")]
    [Tooltip("Distance in front of the victim (along the victim's forward axis) where the attacker should stand to land the next hit.")]
    [SerializeField] private float attackSpotDistance = 0.9f;
    [Tooltip("Extra world-space offset applied on top of the computed spot (X = lateral nudge, Y = height, Z = depth). Usually left at zero.")]
    [SerializeField] private Vector3 attackSpotOffset = Vector3.zero;

    [Header("Movement")]
    [Tooltip("Seconds the attacker takes to step to the attack spot.")]
    [SerializeField] private float moveDuration = 0.35f;
    [Tooltip("Easing curve for the step. Defaults to a smooth ease-in-out when left empty.")]
    [SerializeField] private AnimationCurve moveEase;
    [Tooltip("When true the attacker also turns to face the victim while stepping.")]
    [SerializeField] private bool rotateAttackerToFaceVictim = true;
    [Tooltip("Seconds the attacker takes to turn toward the victim.")]
    [SerializeField] private float rotateDuration = 0.25f;

    [Header("Getup Recompute")]
    [Tooltip("After a getup completes, wait this long before recomputing the attack spot (lets the getup clip settle and the victim settle its facing).")]
    [SerializeField] private float getupRecomputeDelay = 0.1f;

    [Header("Diagnostics")]
    [SerializeField] private bool verboseLogging = false;

    // Current choreography state.
    private Vector3 _currentAttackSpot;
    private bool _hasAttackSpot;

    /// <summary>The most recently computed attack spot (world space). Valid only when HasAttackSpot is true.</summary>
    public Vector3 CurrentAttackSpot => _currentAttackSpot;
    /// <summary>True once an attack spot has been computed at least once.</summary>
    public bool HasAttackSpot => _hasAttackSpot;

    private void Awake()
    {
        Instance = this;
        if (animationController == null) animationController = FindSceneAnimationController();
        if (moveEase == null || moveEase.length == 0)
        {
            // Smooth ease-in-out so the step accelerates then settles (reads as a deliberate step, not a teleport).
            moveEase = new AnimationCurve(
                new Keyframe(0f, 0f, 0f, 0f),
                new Keyframe(0.5f, 0.5f, 2f, 2f),
                new Keyframe(1f, 1f, 0f, 0f));
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private AnimationController FindSceneAnimationController()
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<AnimationController>();
#else
        return Object.FindObjectOfType<AnimationController>();
#endif
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Computes the attack spot in front of the victim, without moving anyone.
    /// Use this to inspect the spot or to drive your own movement.
    /// </summary>
    public Vector3 ComputeAttackSpot(Transform victim)
    {
        if (victim == null) return Vector3.zero;

        // "In front of the victim" = along the victim's forward axis. The victim faces its
        // opponent, so this lands the attacker squarely on the victim's front side.
        Vector3 forward = victim.forward;
        forward.y = 0f;                 // keep the step on the ground plane
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 spot = victim.position + forward * attackSpotDistance;

        // Keep the ground height of the victim so fighters never sink or float.
        spot.y = victim.position.y + attackSpotOffset.y;
        spot += new Vector3(attackSpotOffset.x, 0f, attackSpotOffset.z);

        _currentAttackSpot = spot;
        _hasAttackSpot = true;

        if (verboseLogging)
            Debug.Log($"[CombatPositioning] Attack spot for victim '{victim.name}' = {spot}");

        return spot;
    }

    /// <summary>
    /// Full choreography: recompute the attack spot in front of the victim and step the
    /// attacker to it. Call this after a hit animation resolves.
    /// </summary>
    public void MoveAttackerToSpot(Transform attacker, Transform victim)
    {
        if (attacker == null || victim == null)
        {
            Debug.LogWarning($"{nameof(CombatPositioningController)}: MoveAttackerToSpot called with a null attacker/victim.", this);
            return;
        }

        Vector3 spot = ComputeAttackSpot(victim);
        StartCoroutine(StepToSpot(attacker, victim, spot));
    }

    /// <summary>
    /// Recomputes the attack spot after a getup. The victim's facing may have changed while it
    /// stood up (e.g. it got up facing the other way), so the spot must be derived again rather
    /// than reused. The attacker is then walked to the refreshed spot.
    /// </summary>
    public void RecomputeAfterGetup(Transform attacker, Transform victim)
    {
        if (attacker == null || victim == null) return;

        if (getupRecomputeDelay > 0f)
        {
            // Deferred: the getup clip needs a moment to settle before the victim's facing is final.
            StartCoroutine(RecomputeAfterDelay(attacker, victim, getupRecomputeDelay));
            return;
        }

        if (verboseLogging) Debug.Log("[CombatPositioning] Recomputing attack spot after getup.");
        MoveAttackerToSpot(attacker, victim);
    }

    private IEnumerator RecomputeAfterDelay(Transform attacker, Transform victim, float delay)
    {
        yield return new WaitForSeconds(delay);

        // Entities may have been destroyed during the wait.
        if (attacker == null || victim == null) yield break;

        if (verboseLogging) Debug.Log("[CombatPositioning] Recomputing attack spot after getup (delayed).");
        MoveAttackerToSpot(attacker, victim);
    }

    // ------------------------------------------------------------------
    // Movement
    // ------------------------------------------------------------------

    private IEnumerator StepToSpot(Transform attacker, Transform victim, Vector3 spot)
    {
        // Take over positioning from the anchor-recovery system for the duration of the step,
        // otherwise its LateUpdate Lerp would drag the attacker back mid-move.
        animationController?.BeginExternalPositionControl();

        Vector3 startPos = attacker.position;
        Quaternion startRot = attacker.rotation;
        Quaternion targetRot = rotateAttackerToFaceVictim ? LookRotationToward(attacker, victim) : startRot;

        float duration = Mathf.Max(0.01f, moveDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = moveEase.Evaluate(t);

            if (attacker == null) break; // destroyed mid-step
            attacker.position = Vector3.Lerp(startPos, spot, eased);

            if (rotateAttackerToFaceVictim)
            {
                float rt = rotateDuration > 0f ? Mathf.Clamp01(elapsed / Mathf.Max(0.01f, rotateDuration)) : 1f;
                attacker.rotation = Quaternion.Slerp(startRot, targetRot, rt);
            }

            yield return null;
        }

        if (attacker != null)
        {
            // Land exactly on the spot so the next attack is pixel-accurate.
            attacker.position = spot;
            if (rotateAttackerToFaceVictim) attacker.rotation = targetRot;
        }

        if (verboseLogging)
            Debug.Log($"[CombatPositioning] '{attacker?.name}' stepped to attack spot {spot}.");

        // Hand positioning back to the anchor system. Give it the freshly-computed spot as the
        // transient target so the handover is seamless (no snap back to the old anchor).
        animationController?.EndExternalPositionControl(attacker, spot);
    }

    /// <summary>
    /// Rotation that makes the attacker look at the victim, flattened to the ground plane so
    /// fighters never tilt onto their backs.
    /// </summary>
    private static Quaternion LookRotationToward(Transform attacker, Transform victim)
    {
        Vector3 dir = victim.position - attacker.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return attacker.rotation;
        return Quaternion.LookRotation(dir.normalized, Vector3.up);
    }
}
