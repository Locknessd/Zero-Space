using UnityEngine;

/// <summary>
/// 2.5D Fighting Game Camera (Mortal Kombat / Street Fighter / Tekken style)
/// Tracks two fighters, calculates dynamic midpoint, zooms based on fighter distance,
/// applies smooth damping, stage clamping, and supports hit/impact camera shake.
/// </summary>
[RequireComponent(typeof(Camera))]
public class MortalKombatCamera : MonoBehaviour
{
    /// <summary>Which side of the arena the camera sits on. It always looks IN at the fighters.</summary>
    public enum ViewSide
    {
        /// <summary>Camera stands on +Z and looks toward -Z. The usual "outside looking in" shot.</summary>
        PositiveZ,
        /// <summary>Camera stands on -Z and looks toward +Z.</summary>
        NegativeZ
    }

    public static MortalKombatCamera Instance { get; private set; }

    [Header("Targets")]
    [Tooltip("Left / Player 1 fighter transform")]
    public Transform targetLeft;
    [Tooltip("Right / Player 2 fighter transform")]
    public Transform targetRight;
    [Tooltip("Auto-detect targets from the CharacterAnimatorBridge fighters if null")]
    public bool autoFindTargets = true;

    [Header("Position & Framing")]
    [Tooltip("Offset applied to the midpoint between fighters (Y = vertical centering)")]
    public Vector3 midpointOffset = new Vector3(0f, 1.1f, 0f);

    [Tooltip("Camera pitch angle in degrees. Positive looks DOWN at the fighters -- raise this for a more 3D, over-the-shoulder feel.")]
    [Range(-20f, 45f)]
    public float pitchAngle = 12f;

    [Tooltip("Which side of the arena the camera sits on. The camera looks IN at the fighters from this side. Flip it if the view is coming from inside the stage looking out.")]
    public ViewSide viewSide = ViewSide.PositiveZ;

    [Tooltip("Camera YAW offset in degrees away from straight-on (0 = flat side view). 20-30 gives a 3/4 perspective view instead of a flat 2D profile.")]
    [Range(-60f, 60f)]
    public float yawAngle = 22f;

    [Tooltip("Lateral (side) offset applied when NO ONE is attacking (neutral framing), along the world X axis.")]
    public float lateralOffset = 0f;

    [Tooltip("Lateral offset used while the RIGHT fighter attacks (camera swings to the right side).")]
    public float lateralOffsetRightAttacker = -3f;

    [Tooltip("Lateral offset used while the LEFT fighter attacks (camera swings to the left side).")]
    public float lateralOffsetLeftAttacker = 3f;

    [Tooltip("Smooth time used when the lateral offset swings between sides, so the camera glides instead of snapping.")]
    [Range(0.01f, 1f)]
    public float lateralOffsetSmoothTime = 0.25f;

    [Tooltip("Base distance of the camera from the fighters (larger = further back / wider).")]
    public float baseDistance = 5.2f;

    [Tooltip("Flip which side of the stage the camera stands on AT RUNTIME (e.g. if the stage geometry sits on the opposite side).")]
    public bool flipViewSideOnStart = false;

    [Header("Dynamic Zoom")]
    [Tooltip("Enable dynamic zooming based on distance between fighters")]
    public bool enableDynamicZoom = true;

    [Tooltip("Minimum camera distance when fighters are closest (close-up combat)")]
    public float minDistance = 3.6f;

    [Tooltip("Maximum camera distance when fighters are far apart")]
    public float maxDistance = 7.5f;

    [Tooltip("Reference fighter distance at which base zoom is applied")]
    public float referenceFighterDistance = 1.5f;

    [Tooltip("Zoom sensitivity multiplier based on fighter distance")]
    public float zoomFactor = 0.85f;

    [Header("Attack Focus")]
    [Tooltip("While an attack is playing, bias the camera toward the ATTACKER; when nothing is attacking it sits on the midpoint between the two fighters.")]
    public bool focusAttackerDuringAttack = true;
    [Tooltip("How far the camera slides from the midpoint toward the attacker (0 = none, 1 = fully on the attacker).")]
    [Range(0f, 1f)]
    public float attackFocusBias = 0.45f;
    [Tooltip("Extra zoom-in (world units closer) while focusing on an attack, for a punchier shot.")]
    public float attackFocusZoomIn = 0.5f;
    [Tooltip("Smooth time used when easing into / out of an attack focus shot.")]
    [Range(0.01f, 0.6f)]
    public float focusSmoothTime = 0.18f;
    [Tooltip("Safety valve: an attack shot is force-released after this many seconds even if the 'attack finished' callback never arrives (an interrupted turn would otherwise leave the camera stuck on one side).")]
    public float maxAttackFocusSeconds = 8f;
    [Tooltip("Retained for compatibility; the idle-based auto-release is disabled (the queue releases the shot when both fighters are back in Idle).")]
    public float minAttackFocusSeconds = 0.5f;
    [Tooltip("DEPRECATED / no longer used. The queue now releases the attack shot itself once BOTH fighters are back in Idle, so the camera must not release early here.")]
    public bool releaseFocusWhenAttackerIdle = false;

    [Header("Smooth Damping")]
    [Tooltip("Smooth time for camera movement (lower = snappier, higher = smoother)")]
    [Range(0.01f, 0.5f)]
    public float smoothTime = 0.08f;

    [Tooltip("Smooth time for zoom transitions")]
    [Range(0.01f, 0.5f)]
    public float zoomSmoothTime = 0.12f;

    [Header("Stage Boundaries (Clamping)")]
    [Tooltip("Clamp camera X to remain inside the battle stage")]
    public bool clampHorizontal = true;
    public float minStageX = -12f;
    public float maxStageX = 12f;

    [Tooltip("Clamp camera Y position")]
    public bool clampVertical = true;
    public float minStageY = 0.5f;
    public float maxStageY = 4f;

    [Header("Screen Shake")]
    [Tooltip("Enable hit / impact camera shake")]
    public bool enableScreenShake = true;
    public float defaultShakeIntensity = 0.15f;
    public float defaultShakeDuration = 0.2f;

    // Runtime state
    private Camera _cam;
    private Vector3 _currentVelocity;
    private float _currentZoomVelocity;
    private float _currentDistance;
    private Vector3 _shakeOffset;
    private float _shakeTimeRemaining;
    private float _currentShakeIntensity;
    // Focus bias state: temporarily bias camera midpoint toward one fighter (attacker)
    private PlayerUI.Side? _focusSide = null;
    private float _focusBias = 0f; // 0..1 how far toward fighter
    private float _focusTimeRemaining = 0f;
    private float _focusDuration = 0.4f;

    // Persistent ATTACK focus: set when an attack starts and cleared when it ends. While active the
    // camera biases toward the attacker; otherwise it returns to the midpoint between both fighters.
    private PlayerUI.Side? _attackFocusSide = null;
    private float _attackFocusBlend = 0f; // 0..1 smoothed blend into the attack shot
    private float _attackFocusBlendVelocity = 0f;
    private float _attackFocusElapsed = 0f; // seconds the current attack shot has been held
    // Smoothed lateral offset actually applied to the camera. The TARGET depends on who is attacking
    // (see UpdateCameraPosition), so this eases between the two sides instead of jumping.
    private float _currentLateralOffset;
    private float _lateralOffsetVelocity;

    /// <summary>True while the camera is holding an attack shot on a specific side.</summary>
    public bool IsFocusingAttack => _attackFocusSide.HasValue;

    void Awake()
    {
        Instance = this;
        _cam = GetComponent<Camera>();
        _currentDistance = baseDistance;

        // Quick escape hatch: flip the side the camera stands on without re-picking the enum in the
        // Inspector (handy when the stage geometry lies on the opposite side of the fighters).
        if (flipViewSideOnStart)
        {
            viewSide = viewSide == ViewSide.PositiveZ ? ViewSide.NegativeZ : ViewSide.PositiveZ;
        }
    }

    void Start()
    {
        if (autoFindTargets && (targetLeft == null || targetRight == null))
        {
            FindFighterTargets();
        }

        // Snap immediately to position on start
        SnapToTarget();
    }

    /// <summary>
    /// Searches for active fighter targets in the scene.
    /// </summary>
    public void FindFighterTargets()
    {
        // Fighters are driven by CharacterAnimatorBridge now (one per side). Pick the left/right by
        // world X so the camera frames them consistently regardless of scene ordering.
        var bridges = Object.FindObjectsByType<CharacterAnimatorBridge>(FindObjectsSortMode.None);
        CharacterAnimatorBridge left = null, right = null;
        foreach (var b in bridges)
        {
            if (b == null || b.Animator == null) continue;
            if (left == null) { left = b; continue; }
            if (b.Animator.transform.position.x < left.Animator.transform.position.x)
            {
                right = left;
                left = b;
            }
            else
            {
                right = b;
            }
        }

        if (left != null && targetLeft == null) targetLeft = left.Animator.transform;
        if (right != null && targetRight == null) targetRight = right.Animator.transform;

        // Fallback: look for named objects
        if (targetLeft == null)
        {
            var p1 = GameObject.Find("Trump_Atk") ?? GameObject.Find("Player_Left") ?? GameObject.FindWithTag("Player");
            if (p1 != null) targetLeft = p1.transform;
        }

        if (targetRight == null)
        {
            var p2 = GameObject.Find("Trump_Victim") ?? GameObject.Find("Player_Right") ?? GameObject.FindWithTag("Enemy");
            if (p2 != null) targetRight = p2.transform;
        }
    }

    void LateUpdate()
    {
        // Re-check targets if missing
        if (targetLeft == null || targetRight == null)
        {
            if (autoFindTargets) FindFighterTargets();
            if (targetLeft == null && targetRight == null) return;
        }

        UpdateFocus(Time.deltaTime);
        UpdateCameraPosition(Time.deltaTime);
        UpdateScreenShake(Time.deltaTime);
    }

    /// <summary>
    /// Starts an attack shot: the camera biases toward <paramref name="side"/> until
    /// <see cref="EndAttackFocus"/> is called. Used so the attacker is framed during the attack while
    /// the camera returns to the midpoint between turns.
    /// </summary>
    public void BeginAttackFocus(PlayerUI.Side side)
    {
        _attackFocusSide = side;
        _attackFocusElapsed = 0f;
    }

    /// <summary>Ends the attack shot; the camera eases back to the midpoint between the fighters.</summary>
    public void EndAttackFocus()
    {
        _attackFocusSide = null;
        _attackFocusElapsed = 0f;
    }

    private void UpdateFocus(float deltaTime)
    {
        if (_focusTimeRemaining > 0f)
        {
            _focusTimeRemaining -= deltaTime;
            if (_focusTimeRemaining <= 0f)
            {
                _focusSide = null;
                _focusBias = 0f;
                _focusDuration = 0.4f;
            }
        }

        // AUTO-RELEASE the attack shot. Without this the camera stayed parked on whichever side last
        // attacked (the framing leaned toward the left fighter permanently) whenever the explicit
        // "attack finished" callback did not arrive -- an interrupted / skipped exchange, a cleared
        // queue, or a KO path that never reached the normal completion step.
        if (_attackFocusSide.HasValue)
        {
            _attackFocusElapsed += deltaTime;

            bool timedOut = maxAttackFocusSeconds > 0f && _attackFocusElapsed >= maxAttackFocusSeconds;

            // NOTE: the idle-based auto-release is DISABLED. The queue owns the attack shot: it releases
            // it only once BOTH fighters are genuinely back in Idle (see the attack-finished hook), so
            // the camera no longer snaps to centre while a hit reaction / attack is still playing.
            //
            // The old idle check here was the cause of the "camera jumps to the middle mid-attack":
            // the shot is requested from the exchange's APPLY step, and for the first frames the animator
            // still reports Idle, so this released almost immediately at minAttackFocusSeconds.
            // Only the timeout remains, as a safety valve for a missing callback.
            if (timedOut)
            {
                _attackFocusSide = null;
                _attackFocusElapsed = 0f;
            }
        }

        // Smoothly blend the persistent attack shot in/out so the cut is never abrupt.
        float targetBlend = (focusAttackerDuringAttack && _attackFocusSide.HasValue) ? 1f : 0f;
        if (deltaTime > 0f)
        {
            _attackFocusBlend = Mathf.SmoothDamp(_attackFocusBlend, targetBlend,
                                                 ref _attackFocusBlendVelocity, focusSmoothTime,
                                                 Mathf.Infinity, deltaTime);
        }
        else
        {
            _attackFocusBlend = targetBlend;
        }
    }

    /// <summary>
    /// True when the given side's fighter is standing idle (no attack/hit clip playing). Used to release
    /// an attack focus shot automatically once the attacker has settled.
    /// Returns false when there is no target, so an unknown side is never treated as idle.
    /// </summary>
    private bool IsSideIdle(PlayerUI.Side side)
    {
        Transform t = side == PlayerUI.Side.Left ? targetLeft : targetRight;
        if (t == null) return false;

        CharacterAnimatorBridge bridge = t.GetComponent<CharacterAnimatorBridge>();
        if (bridge == null) bridge = t.GetComponentInParent<CharacterAnimatorBridge>();
        if (bridge == null) return false;

        try { return bridge.IsIdleAndSettled; }
        catch { return false; }
    }

    private void UpdateCameraPosition(float deltaTime)
    {
        Vector3 leftPos = targetLeft != null ? targetLeft.position : targetRight.position;
        Vector3 rightPos = targetRight != null ? targetRight.position : targetLeft.position;

        // 1. Calculate midpoint between fighters
        Vector3 midpoint = (leftPos + rightPos) * 0.5f + midpointOffset;

        // 1b. Apply temporary focus bias toward attacker if requested
        if (_focusTimeRemaining > 0f && _focusSide.HasValue)
        {
            // compute attacker position
            Vector3 attackerPos = _focusSide == PlayerUI.Side.Left ? leftPos : rightPos;
            // vector from midpoint to attacker
            Vector3 toAtt = attackerPos - midpoint;
            // bias amount (ease out over remaining time)
            float t = Mathf.Clamp01(_focusTimeRemaining / _focusDuration);
            float ease = 1f - Mathf.Pow(1f - t, 2f); // ease-out
            float bias = _focusBias * ease;
            midpoint += toAtt * bias;
        }

        // 1c. PERSISTENT ATTACK FOCUS: while an attack is playing, slide the look-at point toward the
        //     attacker (smoothed). When nothing is attacking the blend falls back to 0 and the camera sits
        //     on the midpoint between both fighters.
        if (_attackFocusSide.HasValue && _attackFocusBlend > 0.001f)
        {
            Vector3 attackerPos = _attackFocusSide.Value == PlayerUI.Side.Left ? leftPos : rightPos;
            midpoint = Vector3.Lerp(midpoint, attackerPos + midpointOffset, _attackFocusBlend * attackFocusBias);
        }

        // 2. Calculate dynamic distance based on fighter spread
        float fighterDist = Vector3.Distance(leftPos, rightPos);
        float targetDist = baseDistance;

        if (enableDynamicZoom)
        {
            float distDiff = (fighterDist - referenceFighterDistance) * zoomFactor;
            targetDist = Mathf.Clamp(baseDistance + distDiff, minDistance, maxDistance);
        }

        // Punch in a little while holding an attack shot.
        targetDist = Mathf.Max(minDistance, targetDist - attackFocusZoomIn * _attackFocusBlend);

        if (deltaTime > 0f)
        {
            _currentDistance = Mathf.SmoothDamp(_currentDistance, targetDist, ref _currentZoomVelocity, zoomSmoothTime, Mathf.Infinity, deltaTime);
        }
        else
        {
            _currentDistance = targetDist;
        }

        // 3. 3D PLACEMENT: orbit the camera around the look-at point using the pitch + yaw angles, so the
        //    view reads as a 3/4 perspective shot instead of a flat side-on 2D framing. The lateral offset
        //    nudges the camera sideways for extra depth.
        //
        //    The base yaw selects WHICH SIDE of the arena the camera stands on:
        //      - PositiveZ: camera sits on +Z and looks IN toward -Z (the usual "outside in" shot).
        //      - NegativeZ: camera sits on -Z and looks IN toward +Z (the old behaviour).
        //    Both look AT the fighters; only the side differs.
        float baseYaw = viewSide == ViewSide.PositiveZ ? 0f : 180f;
        Quaternion orbit = Quaternion.Euler(pitchAngle, baseYaw + yawAngle, 0f);
        Vector3 desiredPosition = midpoint + (orbit * Vector3.forward) * _currentDistance;

        // LATERAL SWING: the side offset follows WHO is attacking -- right attacker swings the camera to
        // -3, left attacker to +3, and back to the neutral offset when nobody is attacking. Smoothed so
        // the camera glides across instead of snapping when turns alternate.
        float targetLateral = lateralOffset;
        if (_attackFocusSide.HasValue && _attackFocusBlend > 0.001f)
        {
            float sideOffset = _attackFocusSide.Value == PlayerUI.Side.Right
                ? lateralOffsetRightAttacker
                : lateralOffsetLeftAttacker;
            targetLateral = Mathf.Lerp(lateralOffset, sideOffset, _attackFocusBlend);
        }

        if (deltaTime > 0f)
        {
            _currentLateralOffset = Mathf.SmoothDamp(_currentLateralOffset, targetLateral,
                                                     ref _lateralOffsetVelocity, lateralOffsetSmoothTime,
                                                     Mathf.Infinity, deltaTime);
        }
        else
        {
            _currentLateralOffset = targetLateral;
        }

        desiredPosition.x += _currentLateralOffset;

        // 4. Clamping (applied to the FINAL camera position, so the orbit cannot push it off-stage).
        if (clampHorizontal)
        {
            desiredPosition.x = Mathf.Clamp(desiredPosition.x, minStageX, maxStageX);
        }
        if (clampVertical)
        {
            desiredPosition.y = Mathf.Clamp(desiredPosition.y, minStageY, maxStageY);
        }

        // 5. Smooth Damp to desired position
        Vector3 newPos;
        if (deltaTime > 0f)
        {
            newPos = Vector3.SmoothDamp(transform.position - _shakeOffset, desiredPosition, ref _currentVelocity, smoothTime, Mathf.Infinity, deltaTime);
        }
        else
        {
            newPos = desiredPosition;
        }

        // Apply shake offset
        transform.position = newPos + _shakeOffset;

        // 6. Camera rotation: LOOK AT the (biased) focus point rather than a fixed angle, so the fighters
        //    stay centred even though the camera is now offset laterally / vertically.
        transform.rotation = Quaternion.LookRotation(midpoint - transform.position, Vector3.up);
    }

    /// <summary>
    /// Snaps camera immediately to target without smoothing (e.g. at round start).
    /// </summary>
    public void SnapToTarget()
    {
        _currentVelocity = Vector3.zero;
        _currentZoomVelocity = 0f;
        UpdateCameraPosition(0f);
    }

    /// <summary>
    /// Triggers screen shake with custom intensity and duration.
    /// </summary>
    public void TriggerShake(float intensity, float duration)
    {
        if (!enableScreenShake) return;
        _currentShakeIntensity = intensity;
        _shakeTimeRemaining = duration;
    }

    /// <summary>
    /// Triggers default impact shake (for punches, kicks, hits).
    /// </summary>
    public void TriggerImpactShake()
    {
        TriggerShake(defaultShakeIntensity, defaultShakeDuration);
    }

    /// <summary>
    /// Temporarily bias camera to focus toward a given side (attacker).
    /// bias: 0..1 fraction toward attacker from midpoint. duration in seconds.
    /// </summary>
    public void FocusOnSide(PlayerUI.Side side, float bias = 0.35f, float duration = 0.45f)
    {
        _focusSide = side;
        _focusBias = Mathf.Clamp01(bias);
        _focusDuration = Mathf.Max(0.01f, duration);
        _focusTimeRemaining = _focusDuration;
    }

    /// <summary>
    /// Triggers heavy impact shake (for critical hits, K.O., special moves).
    /// </summary>
    public void TriggerHeavyShake()
    {
        TriggerShake(defaultShakeIntensity * 2.2f, defaultShakeDuration * 1.5f);
    }

    private void UpdateScreenShake(float deltaTime)
    {
        if (_shakeTimeRemaining > 0f)
        {
            _shakeTimeRemaining -= deltaTime;
            float damper = _shakeTimeRemaining / defaultShakeDuration;
            float rx = (Random.value * 2f - 1f) * _currentShakeIntensity * damper;
            float ry = (Random.value * 2f - 1f) * _currentShakeIntensity * damper;
            _shakeOffset = new Vector3(rx, ry, 0f);

            if (_shakeTimeRemaining <= 0f)
            {
                _shakeOffset = Vector3.zero;
            }
        }
        else
        {
            _shakeOffset = Vector3.zero;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (targetLeft != null && targetRight != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(targetLeft.position, targetRight.position);
            Vector3 mid = (targetLeft.position + targetRight.position) * 0.5f + midpointOffset;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(mid, 0.25f);
        }

        if (clampHorizontal)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(new Vector3(minStageX, 0f, 0f), new Vector3(minStageX, 5f, 0f));
            Gizmos.DrawLine(new Vector3(maxStageX, 0f, 0f), new Vector3(maxStageX, 5f, 0f));
        }
    }
}
