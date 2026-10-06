using UnityEngine;

/// <summary>
/// CINEMATIC 2.5D FIGHTING CAMERA (Mortal Kombat / Street Fighter / Tekken style)
///
/// WHAT IT DOES
/// - Frames two fighters: it LOOKS AT the dynamic focal point between them (or at one fighter).
/// - DYNAMIC ZOOM: pulls back when the fighters are far apart, punches in when they close up.
/// - ATTACK SHOT (cinematic focus): when a character attacks, the camera smoothly ORBITS / ZOOMS /
///   ROTATES around that ATTACKER (or around the focal point between the two) and eases back to the
///   neutral framing when the exchange ends. Nothing ever snaps: every transition is damped with
///   Vector3.SmoothDamp (position) and Quaternion.Slerp (rotation).
/// - IMPACT SHAKE for hits, and stage clamping so the orbit can never push the camera off the arena.
///
/// INPUTS
/// - Two character Transforms: targetLeft (player 1) and targetRight (player 2).
/// - Attack triggers, both kept in sync:
///     * OnCharacterAttack(Transform attacker)  -- Transform based, new API.
///     * BeginAttackFocus(PlayerUI.Side) / EndAttackFocus() -- side based, legacy API retained so the
///       existing GameManager call sites keep compiling and behaving the same.
///
/// HOW TO HOOK IT UP
/// 1) ANIMATION EVENTS: on the attack clip, add an AnimationEvent at the impact frame whose function
///    is OnCharacterAttack and drag the ATTACKER's root Transform into its object-reference slot (the
///    method takes a Transform, so it is a valid AnimationEvent receiver). Add a second event at the
///    end of the clip calling OnCharacterAttackFinished.
/// 2) STATE MACHINE BEHAVIOUR: in OnStateEnter of your AttackState call
///    MortalKombatCamera.Instance?.OnCharacterAttack(animator.transform); and in OnStateExit call
///    MortalKombatCamera.Instance?.OnCharacterAttackFinished(animator.transform);
/// 3) CODE: call the same two methods around your attack routine (this is what GameManager does).
/// 4) CINEMACHINE: if you would rather drive a virtual camera, see the CINEMACHINE NOTES block near
///    the bottom of this file.
/// </summary>
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(12000)]
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
    [Tooltip("Auto-detect targets from the CharacterCombat fighters if null")]
    public bool autoFindTargets = true;

    [Header("Position & Framing")]
    [Tooltip("Offset applied to the midpoint between fighters (Y = vertical centering). RAISE the Y to lift the whole camera up; this is the main height control.")]
    public Vector3 midpointOffset = new Vector3(0f, 1.5f, 0f);

    [Tooltip("Camera pitch angle in degrees. Positive looks DOWN at the fighters -- raise this for a more 3D, over-the-shoulder feel.")]
    [Range(-20f, 60f)]
    public float pitchAngle = 18f;

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
    [Tooltip("Smooth time used when easing into / out of an attack focus shot. Keep this SHORT: the attack clips here run ~0.5s, so a long ease means the shot is still gliding into place after the hit has already landed.")]
    [Range(0.01f, 0.6f)]
    public float focusSmoothTime = 0.07f;
    [Tooltip("Safety valve: an attack shot is force-released after this many seconds even if the 'attack finished' callback never arrives (an interrupted turn would otherwise leave the camera stuck on one side).")]
    public float maxAttackFocusSeconds = 8f;

    [Header("Attack Shot (simple zoom-in only)")]
    [Tooltip("When true the attack shot is a PLAIN ZOOM-IN: the camera stays on exactly the same axis it uses between turns (same yaw / pitch / height / side) and only moves a little closer while the attack plays, then eases straight back to where it was. No orbit, no rotation, no swinging round behind the attacker.\n\nThis is the default. Turn it OFF only if you want the old cinematic orbit / over-the-shoulder behaviour back -- that shot then needs the parameters in the two sections below to be re-enabled too.")]
    public bool attackZoomOnly = true;

    [Tooltip("How much CLOSER the camera moves while an attack is playing, in world units. This is the whole attack shot: a small value (0.4-0.8) reads as a subtle push-in.")]
    [Range(0f, 6f)]
    public float attackZoomIn = 0.8f;

    [Header("Cinematic Attack Shot (orbit / rotate around the attacker)")]
    [Tooltip("How much the camera swings AROUND the attacker during an attack shot, in degrees. 0 = the attack shot only zooms/leans. A moderate value (25-40) frames the attacker quickly; very wide sweeps take too long to land and read as the camera lagging behind the action.\n\nZEROED by default because attackZoomOnly is on: an orbit would rotate the view away from the neutral framing, which the plain zoom-in deliberately avoids.")]
    [Range(0f, 120f)]
    public float attackOrbitDegrees = 0f;

    [Tooltip("Extra camera distance during an attack shot. NEGATIVE = punch IN toward the attacker (the usual cinematic close-up); POSITIVE = pull out for a wider 'showcase' shot. More negative = tighter close-up.\n\nZEROED by default because attackZoomOnly is on and attackZoomIn owns the push-in; keeping both would zoom in twice.")]
    public float attackOrbitDistance = 0f;

    [Tooltip("Extra downward pitch (degrees) applied during an attack shot, so the camera drops toward an over-the-shoulder angle. 0 = keep the neutral pitch. Used as the default when the dynamic shot angle below is OFF.\n\nZEROED by default: the plain zoom-in keeps the neutral pitch so the shot does not change angle.")]
    [Range(-30f, 40f)]
    public float attackOrbitPitch = 0f;

    [Tooltip("Camera height offset added to the focal point during an attack shot, so the shot lifts slightly off the ground.\n\nZEROED by default: the plain zoom-in keeps the camera at its neutral height.")]
    public float attackOrbitHeight = 0f;

    [Tooltip("Seconds for the camera to swing into the attack orbit. Lower = snappier cut, higher = a slow cinematic glide. Keep this SHORT so the shot is framed before the attack clip reaches its impact frame.")]
    [Range(0.05f, 1.5f)]
    public float attackOrbitBlendTime = 0.18f;

    [Tooltip("When true the attack shot also ROTATES the view (Quaternion.Slerp) toward the attacker, instead of only sliding the look-at point.\n\nOFF by default: the attack shot must keep the SAME view axis and just zoom, which means the camera's aim never turns during an attack.")]
    public bool rotateViewDuringAttack = false;

    [Header("Dynamic Shot Angles (varied high / low / diagonal attack angles)")]
    [Tooltip("When true the attack shot PICKS a shot angle at random from the list below (high diagonal, low diagonal, over-the-shoulder, ...) instead of always using the same pitch. This is what makes the camera feel flexible: each attack can be framed from a different height/angle. Turn OFF to always use attackOrbitPitch.\n\nOFF by default: with attackZoomOnly on, every attack must keep the SAME angle and only change distance.")]
    public bool useDynamicShotAngles = false;

    [Tooltip("Extra seconds the SAME angle is kept before the camera is allowed to roll a new one, so consecutive attacks do not flicker between angles. Set to 0 to re-roll on every attack. Keep it short so each attack can get its own framing.")]
    [Range(0f, 10f)]
    public float minSecondsBetweenAngleChanges = 0.4f;

    [Tooltip("When true the camera never repeats the previous shot angle back-to-back (it always rolls a different one), which reads as a deliberate variety of shots rather than random noise.")]
    public bool avoidRepeatingLastAngle = true;

    /// <summary>
    /// One selectable cinematic attack angle. pitch = camera pitch offset (positive looks DOWN from
    /// above, negative looks UP from below), yaw = extra swing added on top of the base orbit, and
    /// distance = extra push-in (negative) / pull-out (positive) for this specific shot.
    /// </summary>
    [System.Serializable]
    public struct ShotAngle
    {
        [Tooltip("Name shown in the Inspector, e.g. 'High Diagonal' or 'Low Hero Shot'.")]
        public string name;
        [Tooltip("Camera pitch offset in degrees. POSITIVE = chéo từ TRÊN xuống (high angle), NEGATIVE = chéo từ DƯỚI lên (low hero angle).")]
        [Range(-40f, 60f)]
        public float pitch;
        [Tooltip("Extra yaw added on top of attackOrbitDegrees, for a wider/looser diagonal on this shot.")]
        [Range(-60f, 60f)]
        public float yaw;
        [Tooltip("Extra distance for this shot. NEGATIVE = closer (punchier), POSITIVE = wider.")]
        public float distance;
        [Tooltip("Relative weight when rolling a random angle. Higher = picked more often. 0 disables the angle.")]
        [Range(0f, 5f)]
        public float weight;
    }

    [Tooltip("The pool of shot angles the camera rolls from during an attack. Defaults are filled in on Awake when this list is empty: high diagonal (from above), low hero shot (from below), over-the-shoulder and a flat dramatic side angle.")]
    public ShotAngle[] shotAngles;

    [Tooltip("How fast the camera eases into the chosen shot angle (seconds). Lower = snappier cut to the new angle, higher = a slower, smoother tilt. Keep SHORT so the angle lands before the attack's impact frame.")]
    [Range(0.05f, 1.5f)]
    public float shotAngleBlendTime = 0.12f;

    [Header("Behind-The-Back Attack Shot (camera follows the attacker's facing)")]
    [Tooltip("When true, the attack shot is built from the ATTACKER'S OWN FACING instead of a fixed world-side angle: the camera swings round BEHIND the attacker (over-the-shoulder, looking at its back) and then orbits around it. This is what stops the camera from always standing in front of the character.\n\nOFF by default: the camera must NOT travel round behind the attacker. Attack shots are a plain zoom-in on the neutral axis.")]
    public bool attackFromBehind = false;

    [Tooltip("How far the camera sits BEHIND the attacker, as a fraction of the shot distance, when the attack shot starts. 1 = exactly behind (looking at the attacker's back), 0 = at the attacker's side. Values around 0.75-1 read as a proper over-the-shoulder shot.")]
    [Range(0f, 1f)]
    public float behindStartBlend = 0.95f;

    [Tooltip("How far the camera orbits AWAY from directly-behind during the shot, in degrees. 0 = it stays locked behind the attacker; 30-50 sweeps round the attacker's back to a 3/4 rear angle without taking long enough to read as lag.")]
    [Range(0f, 120f)]
    public float behindOrbitSweep = 40f;

    [Tooltip("Height of the camera relative to the attacker when shooting from behind. Positive = above the attacker looking slightly down its back (the usual over-the-shoulder). RAISE this to lift the behind-the-back shot.")]
    public float behindHeight = 0.9f;

    [Tooltip("Extra pull-in toward the attacker for the behind shot. NEGATIVE = tighter close-up over the shoulder; POSITIVE = further back so more of the attacker is in frame.")]
    public float behindDistance = -0.6f;

    [Tooltip("Seconds the camera takes to travel round behind the attacker. Higher = a slower, more cinematic sweep; lower = a snappier move. Keep SHORT so the camera is already behind the attacker when the swing starts.")]
    [Range(0.05f, 1.5f)]
    public float behindBlendTime = 0.18f;

    [Tooltip("Seconds the camera keeps sweeping around the attacker's back after it has arrived behind, so the shot is never static. The sweep runs along behindOrbitSweep. 0 = park behind and hold still.")]
    [Range(0f, 4f)]
    public float behindSweepDuration = 0.55f;

    [Tooltip("Extra distance the camera drifts during the moving sweep, so the shot zooms while it rotates instead of only orbiting. NEGATIVE = pushes in during the sweep.")]
    public float behindSweepDolly = -0.5f;
    [Tooltip("Retained for compatibility; the idle-based auto-release is disabled (the queue releases the shot when both fighters are back in Idle).")]
    public float minAttackFocusSeconds = 0.5f;
    [Tooltip("DEPRECATED / no longer used. The queue now releases the attack shot itself once BOTH fighters are back in Idle, so the camera must not release early here.")]
    public bool releaseFocusWhenAttackerIdle = false;

    [Header("Smooth Damping")]
    [Tooltip("Smooth time for camera movement (lower = snappier, higher = smoother). This is the FINAL position damping, applied on top of every other blend, so a large value here adds lag to the whole shot.")]
    [Range(0.01f, 0.5f)]
    public float smoothTime = 0.03f;

    [Tooltip("Multiplier on smoothTime while the camera is ENTERING an attack shot. Lower = the camera snaps onto the attacker's framing almost immediately (dứt khoát), then settles to the normal smoothTime once the shot is established. 1 = no snap.")]
    [Range(0.05f, 1f)]
    public float shotEntrySmoothScale = 0.2f;

    [Tooltip("Smooth time for zoom transitions. Short = the push-in reads as a deliberate snap onto the attacker instead of a slow creep.")]
    [Range(0.01f, 0.5f)]
    public float zoomSmoothTime = 0.05f;

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

    // CINEMATIC ORBIT state: 0 = neutral framing, 1 = fully in the attack orbit around the attacker.
    // SmoothDamped in UpdateFocus so the swing in/out is a glide, never a snap. _attackOrbitVelocity is
    // its damping velocity, and _lastAttacker keeps the Transform form of whoever is attacking so the
    // orbit can be built around that specific character (not just a left/right side).
    private float _attackOrbitBlend;
    private float _attackOrbitVelocity;
    private Transform _lastAttacker;
    // True when an attack shot is held for an attacker that is NOT one of the two framed fighters
    // (no side could be resolved). In that case the shot still orbits, but on the focal point between
    // the fighters instead of a side, and it is released the same way via OnCharacterAttackFinished.
    private bool _hasUnmappedAttackFocus;

    // DYNAMIC SHOT ANGLE state: the angle currently rolled for the attack shot, the smoothed values
    // actually applied (pitch / yaw / distance), and a timestamp so the camera does not re-roll between
    // two attacks that happen back-to-back.
    private ShotAngle _currentShotAngle;
    private float _shotAnglePitch;      // smoothed pitch offset applied to the camera
    private float _shotAnglePitchVel;
    private float _shotAngleYaw;        // smoothed extra yaw applied on top of the orbit
    private float _shotAngleYawVel;
    private float _shotAngleDistance;   // smoothed extra distance for this specific shot
    private float _shotAngleDistanceVel;
    private float _lastShotAngleTime = -999f;
    private int _lastShotAngleIndex = -1;
    private bool _shotAngleChosen;

    // BEHIND-THE-BACK shot state: a 0..1 blend into the behind framing, plus the running sweep phase
    // (0..1 over behindSweepDuration) that keeps the shot moving instead of parking it behind the back.
    private float _behindBlend;
    private float _behindBlendVelocity;
    private float _behindSweep;

    // The attacker's facing captured when the attack shot started, so the behind-the-back framing is
    // built from a STABLE direction (the attacker's own forward) instead of a fixed world side.
    private Vector3 _attackFacingDir = Vector3.forward;
    private bool _attackFacingCaptured;
    // Smoothed camera rotation. The desired look rotation is interpolated toward with Quaternion.Slerp
    // so an attack shot ROTATES the view smoothly instead of cutting to a new angle.
    private Quaternion _currentRotation = Quaternion.identity;

    /// <summary>True while the camera is holding an attack shot on a specific side.</summary>
    public bool IsFocusingAttack => _attackFocusSide.HasValue;

    /// <summary>The character Transform the current attack shot is framed on (null when neutral).</summary>
    public Transform AttackFocusTarget => _lastAttacker;

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

        // PLAIN ZOOM-IN ATTACK SHOT: re-asserted from code (there is no "constant" form for a bool).
        //
        // The attack shot must be "same camera axis, slightly closer" and NOTHING else. Every parameter
        // that would swing the camera round behind the attacker, rotate the view, or vary the shot angle
        // is forced OFF here, because a value stored in the SCENE wins over a code default and would
        // otherwise keep the old cinematic shot alive no matter what the field defaults say -- the exact
        // trap that has already bitten the spacing and move-duration values elsewhere in this project.
        attackZoomOnly = true;
        attackFromBehind = false;
        rotateViewDuringAttack = false;
        useDynamicShotAngles = false;
        attackOrbitDegrees = 0f;
        attackOrbitDistance = 0f;
        attackOrbitPitch = 0f;
        attackOrbitHeight = 0f;
        // The attack shot must not slide the camera sideways either: it stays put and only zooms.
        lateralOffsetLeftAttacker = 0f;
        lateralOffsetRightAttacker = 0f;

        // Fill in the default shot-angle pool when the Inspector list is empty, so the camera has a
        // varied set of cinematic angles (high diagonal from above, low hero shot from below, over the
        // shoulder, flat dramatic side) out of the box. Any angles you DO author in the Inspector are
        // left untouched.
        if (shotAngles == null || shotAngles.Length == 0)
        {
            shotAngles = new ShotAngle[]
            {
                // chéo từ TRÊN xuống: camera cao, nhìn xuống nhân vật đang ra đòn.
                new ShotAngle { name = "High Diagonal",    pitch =  22f, yaw =  15f, distance = -0.3f, weight = 1f },
                // chéo từ DƯỚI lên: góc anh hùng, camera thấp nhìn lên.
                new ShotAngle { name = "Low Hero Shot",    pitch = -14f, yaw = -10f, distance = -0.6f, weight = 1f },
                // qua vai: ngang tầm, nghiêng nhiều.
                new ShotAngle { name = "Over The Shoulder", pitch =  10f, yaw =  30f, distance = -0.9f, weight = 1f },
                // ngang kịch tính: gần như phẳng nhưng xoay mạnh.
                new ShotAngle { name = "Flat Dramatic",    pitch =   2f, yaw = -22f, distance = -0.5f, weight = 0.7f },
                // từ trên cao hẳn: nhìn chếch xuống gần như vuông góc.
                new ShotAngle { name = "Top Down Tilt",    pitch =  34f, yaw =   8f, distance =  0.2f, weight = 0.6f },
            };
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
        // Fighters are driven by CharacterCombat now (one per side). Pick the left/right by
        // world X so the camera frames them consistently regardless of scene ordering.
        var bridges = Object.FindObjectsByType<CharacterCombat>(FindObjectsSortMode.None);
        CharacterCombat left = null, right = null;
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
        _lastAttacker = null;
        // The behind-the-back sweep is one-shot per attack: reset it so the NEXT attacker starts its
        // framing from directly behind its own back instead of wherever the last sweep stopped.
        _behindSweep = 0f;
        _attackFacingCaptured = false;
    }

    // ------------------------------------------------------------------
    // Transform-based attack API (the "OnCharacterAttack(Transform)" entry points)
    // ------------------------------------------------------------------

    /// <summary>
    /// Call this the moment a character EXECUTES AN ATTACK. The camera smoothly transitions into a
    /// cinematic shot: it orbits / zooms / rotates around that specific ATTACKER (or around the focal
    /// point between the two fighters when the attacker cannot be resolved to a side) and eases back to
    /// neutral when <see cref="OnCharacterAttackFinished"/> arrives.
    ///
    /// ANIMATION EVENT: this signature is directly usable as an AnimationEvent receiver -- in the attack
    /// clip add an event at the impact frame, choose "OnCharacterAttack" and drag the ATTACKER's root
    /// Transform into the Transform parameter slot.
    ///
    /// STATE MACHINE BEHAVIOUR: call it from OnStateEnter, e.g.
    ///     MortalKombatCamera.Instance?.OnCharacterAttack(animator.transform);
    /// and call <see cref="OnCharacterAttackFinished"/> from OnStateExit.
    /// </summary>
    public void OnCharacterAttack(Transform attacker)
    {
        if (attacker == null) return;

        _lastAttacker = attacker;

        // Capture the attacker's FACING now, at the moment the shot starts. The behind-the-back framing
        // is built from this direction, so the camera swings round the attacker's OWN back rather than
        // standing at a fixed world side (which put it in front of the character).
        CaptureAttackFacing(attacker);

        // Resolve the attacker to a side so the side-based framing (lateral swing / focus bias) and the
        // Transform-based orbit stay in agreement. Unknown attackers (neither target) fall back to a
        // pure focal-point shot, which is why this is not treated as an error.
        PlayerUI.Side? side = SideOf(attacker);
        if (side.HasValue)
        {
            BeginAttackFocus(side.Value);
        }
        else
        {
            // Not one of the two framed fighters: still take an attack shot, but centred on the focal
            // point between the fighters (BeginAttackFocus(null) is not expressible with a side, so the
            // orbit is driven by _lastAttacker only).
            _attackFocusSide = null;
            _attackFocusElapsed = 0f;
            _hasUnmappedAttackFocus = true;
        }
    }

    /// <summary>
    /// Call this when the attacking character's attack is OVER (end of the attack clip / OnStateExit).
    /// The camera eases back to the neutral framing on the focal point between the two fighters.
    /// Passing null releases whatever attack shot is currently held.
    /// </summary>
    public void OnCharacterAttackFinished(Transform attacker)
    {
        // Ignore a late "finished" from a previous attacker while a newer one holds the shot, so an
        // overlapping exchange cannot release the current shot early.
        if (attacker != null && _lastAttacker != null && attacker != _lastAttacker) return;

        _hasUnmappedAttackFocus = false;
        EndAttackFocus();
    }

    /// <summary>
    /// Records the direction the attacker is facing when the attack shot starts, so the behind-the-back
    /// framing can be built relative to the ATTACKER instead of a fixed world side.
    ///
    /// The direction is flattened onto the ground plane and, when a victim is known, aimed from the
    /// attacker TOWARD that victim: an attack always plays toward the opponent, so the attacker's back
    /// is reliably the opposite of that vector even if a clip left the model's yaw slightly off.
    /// </summary>
    private void CaptureAttackFacing(Transform attacker)
    {
        Vector3 dir = Vector3.zero;

        // Prefer "from the attacker toward its opponent": that is the direction the attack travels, so
        // the camera goes behind the attacker and looks along the strike.
        Transform victim = null;
        if (attacker == targetLeft) victim = targetRight;
        else if (attacker == targetRight) victim = targetLeft;

        if (victim != null)
            dir = victim.position - attacker.position;

        // Fall back to the model's own forward when there is no known opponent.
        if (dir.sqrMagnitude < 0.0001f)
            dir = attacker.forward;

        dir.y = 0f;
        _attackFacingDir = dir.sqrMagnitude >= 0.0001f ? dir.normalized : Vector3.forward;
        _attackFacingCaptured = true;
    }

    /// <summary>
    /// Returns which framed side a character Transform belongs to, or null when it is neither fighter.
    /// Matches the transform itself, any of its parents, and any of its children, so an AnimationEvent
    /// wired to a limb / a child animator object still resolves to the right fighter.
    /// </summary>
    public PlayerUI.Side? SideOf(Transform character)
    {
        if (character == null) return null;

        if (targetLeft != null && IsSameFighter(character, targetLeft)) return PlayerUI.Side.Left;
        if (targetRight != null && IsSameFighter(character, targetRight)) return PlayerUI.Side.Right;
        return null;
    }

    /// <summary>True when the two transforms refer to the same fighter (same hierarchy, either direction).</summary>
    private static bool IsSameFighter(Transform a, Transform b)
    {
        if (a == null || b == null) return false;
        return a == b || a.IsChildOf(b) || b.IsChildOf(a);
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
                _lastAttacker = null;
                _hasUnmappedAttackFocus = false;
                _behindSweep = 0f;
                _attackFacingCaptured = false;
            }
        }

        // Smoothly blend the persistent attack shot in/out so the cut is never abrupt.
        bool attackShotHeld = (focusAttackerDuringAttack && (_attackFocusSide.HasValue || _hasUnmappedAttackFocus));

        // PLAIN ZOOM-IN ONLY (attackZoomOnly).
        //
        // The attack shot must be a simple push-in on the SAME axis the camera already uses between
        // turns -- no view rotation, no orbit, no swing round behind the attacker. Everything that would
        // move the camera sideways or change its angle is therefore hard-disabled here, in ONE place, so a
        // value left over in the SCENE cannot quietly re-enable it. Only _attackFocusBlend (the zoom
        // driver below) and the attack-focus state machine keep running; the orbit, shot-angle and
        // behind-the-back blends are all driven to 0 and stay there, which makes every attack shot end by
        // easing straight back to exactly the neutral framing.
        bool cinematicShotEnabled = !attackZoomOnly;
        bool shotBlendTarget = attackShotHeld && cinematicShotEnabled;

        float targetBlend = attackShotHeld ? 1f : 0f;
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

        // CINEMATIC ORBIT blend: a separate, slower ramp than the focus bias above. The bias slides the
        // look-at point toward the attacker quickly; this slower value drives the ORBIT around the
        // attacker (swing angle + extra zoom + view rotation), so the camera eases into a real cinematic
        // arc instead of just leaning sideways. Driven by the same 'attack shot held' flag so an unmapped
        // attacker (no side) still gets the orbit.
        //
        // DISABLED under attackZoomOnly (see above): the blend is driven to 0 so no orbit yaw / pitch /
        // height is ever applied.
        float orbitTargetBlend = shotBlendTarget ? 1f : 0f;
        if (deltaTime > 0f)
        {
            _attackOrbitBlend = Mathf.SmoothDamp(_attackOrbitBlend, orbitTargetBlend,
                                                 ref _attackOrbitVelocity, attackOrbitBlendTime,
                                                 Mathf.Infinity, deltaTime);
        }
        else
        {
            _attackOrbitBlend = orbitTargetBlend;
        }

        // DYNAMIC SHOT ANGLE: roll a new cinematic angle when a shot STARTS (rising edge of the blend),
        // then ease the pitch / yaw / distance of that angle in and out. This is what gives the camera its
        // varied framing: one attack comes in high and diagonal from above, the next rises from below.
        //
        // Fed the CINEMATIC flag, not the raw 'shot held' flag: under attackZoomOnly this eases the
        // applied pitch / yaw / distance to zero and keeps them there, so no angle is ever rolled and the
        // camera's angle stays exactly as it is between turns.
        UpdateShotAngle(shotBlendTarget, deltaTime);

        // BEHIND-THE-BACK blend + moving sweep. The blend brings the camera round behind the attacker;
        // the sweep then keeps it travelling around the attacker's back for behindSweepDuration, so the
        // shot rotates and dollies instead of parking in one static position.
        //
        // Also fed the CINEMATIC flag, so under attackZoomOnly the behind blend stays at 0 and the camera
        // never travels round behind the attacker.
        UpdateBehindShot(shotBlendTarget, deltaTime);
    }

    /// <summary>
    /// Eases the camera in and out of the behind-the-back framing, and advances the sweep that keeps the
    /// shot moving. While a shot is held the sweep ramps 0 -> 1 over <see cref="behindSweepDuration"/>;
    /// when the shot ends everything unwinds so the neutral framing is untouched.
    /// </summary>
    private void UpdateBehindShot(bool attackShotHeld, float deltaTime)
    {
        float targetBlend = (attackFromBehind && attackShotHeld) ? 1f : 0f;

        if (deltaTime > 0f)
        {
            _behindBlend = Mathf.SmoothDamp(_behindBlend, targetBlend,
                                            ref _behindBlendVelocity, behindBlendTime,
                                            Mathf.Infinity, deltaTime);
        }
        else
        {
            _behindBlend = targetBlend;
        }

        // The sweep only advances while a shot is held; it resets when the shot is released so the next
        // attack starts the arc from directly behind the attacker again.
        if (attackShotHeld && behindSweepDuration > 0f)
        {
            _behindSweep = Mathf.Clamp01(_behindSweep + deltaTime / behindSweepDuration);
        }
        else if (!attackShotHeld)
        {
            _behindSweep = 0f;
        }
    }

    /// <summary>
    /// Chooses (on the rising edge of an attack shot) which cinematic angle to use, then smooth-damps the
    /// applied pitch / yaw / distance toward that angle. When the shot ends everything eases back to 0, so
    /// the neutral framing is exactly what it was before.
    /// </summary>
    private void UpdateShotAngle(bool attackShotHeld, float deltaTime)
    {
        if (attackShotHeld && !_shotAngleChosen)
        {
            // Only re-roll once the previous angle has been held long enough, otherwise a rapid exchange
            // of attacks would flicker the camera between angles on every hit.
            if (Time.time - _lastShotAngleTime >= minSecondsBetweenAngleChanges)
            {
                _currentShotAngle = PickShotAngle();
                _lastShotAngleTime = Time.time;
            }
            _shotAngleChosen = true;
        }
        else if (!attackShotHeld)
        {
            _shotAngleChosen = false;
        }

        // Target values for this frame: the rolled angle while a shot is held, zero when neutral.
        float targetPitch = attackShotHeld ? _currentShotAngle.pitch : 0f;
        float targetYaw = attackShotHeld ? _currentShotAngle.yaw : 0f;
        float targetDistance = attackShotHeld ? _currentShotAngle.distance : 0f;

        if (deltaTime > 0f)
        {
            _shotAnglePitch = Mathf.SmoothDamp(_shotAnglePitch, targetPitch,
                                               ref _shotAnglePitchVel, shotAngleBlendTime,
                                               Mathf.Infinity, deltaTime);
            _shotAngleYaw = Mathf.SmoothDamp(_shotAngleYaw, targetYaw,
                                             ref _shotAngleYawVel, shotAngleBlendTime,
                                             Mathf.Infinity, deltaTime);
            _shotAngleDistance = Mathf.SmoothDamp(_shotAngleDistance, targetDistance,
                                                  ref _shotAngleDistanceVel, shotAngleBlendTime,
                                                  Mathf.Infinity, deltaTime);
        }
        else
        {
            _shotAnglePitch = targetPitch;
            _shotAngleYaw = targetYaw;
            _shotAngleDistance = targetDistance;
        }
    }

    /// <summary>
    /// Rolls a shot angle from the weighted pool, avoiding an immediate repeat of the previous one when
    /// <see cref="avoidRepeatingLastAngle"/> is on. Falls back to a zero angle (i.e. no extra tilt) when
    /// the pool is empty or every weight is zero, so the camera can never get stuck without an angle.
    /// </summary>
    private ShotAngle PickShotAngle()
    {
        ShotAngle fallback = new ShotAngle { name = "Default", pitch = attackOrbitPitch, yaw = 0f, distance = 0f, weight = 1f };
        if (shotAngles == null || shotAngles.Length == 0) return fallback;

        // Sum the weights first so the roll is a proper weighted pick. An entry with weight <= 0 is
        // treated as disabled.
        float total = 0f;
        for (int i = 0; i < shotAngles.Length; i++)
        {
            float w = Mathf.Max(0f, shotAngles[i].weight);
            if (avoidRepeatingLastAngle && shotAngles.Length > 1 && i == _lastShotAngleIndex) continue;
            total += w;
        }

        if (total <= 0f)
        {
            // Nothing eligible (all disabled, or the only entry was excluded as a repeat): ignore the
            // repeat-avoidance rule rather than returning a broken angle.
            for (int i = 0; i < shotAngles.Length; i++)
            {
                if (shotAngles[i].weight > 0f) { _lastShotAngleIndex = i; return shotAngles[i]; }
            }
            return fallback;
        }

        float roll = Random.value * total;
        for (int i = 0; i < shotAngles.Length; i++)
        {
            float w = Mathf.Max(0f, shotAngles[i].weight);
            if (avoidRepeatingLastAngle && shotAngles.Length > 1 && i == _lastShotAngleIndex) continue;
            if (w <= 0f) continue;
            roll -= w;
            if (roll <= 0f)
            {
                _lastShotAngleIndex = i;
                return shotAngles[i];
            }
        }

        return fallback;
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

        CharacterCombat bridge = t.GetComponent<CharacterCombat>();
        if (bridge == null) bridge = t.GetComponentInParent<CharacterCombat>();
        if (bridge == null) return false;

        try { return bridge.IsIdleAndSettled; }
        catch { return false; }
    }

    private void UpdateCameraPosition(float deltaTime)
    {
        Vector3 leftPos = VisualGroundPosition(targetLeft != null ? targetLeft : targetRight);
        Vector3 rightPos = VisualGroundPosition(targetRight != null ? targetRight : targetLeft);

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
        //     on the midpoint between both fighters. The focal point uses the ATTACKER TRANSFORM when one
        //     was supplied (OnCharacterAttack), so the shot frames that exact character; otherwise it
        //     falls back to the side's fighter, and finally to the midpoint (unmapped attacker).
        if ((_attackFocusSide.HasValue || _hasUnmappedAttackFocus) && _attackFocusBlend > 0.001f)
        {
            Vector3 attackerPos = ResolveAttackerPosition(leftPos, rightPos);
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

        // CINEMATIC ATTACK ORBIT: extra zoom for the shot. A negative attackOrbitDistance pulls the
        // camera IN toward the attacker, a positive one widens out. Scaled by the orbit blend so the
        // push-in/out glides with the rest of the shot. The rolled shot angle adds its own per-shot
        // distance on top, so 'high diagonal' and 'low hero' shots can each sit at a different range.
        //
        // Under attackZoomOnly the orbit blend and the shot-angle extras are both held at 0, so these two
        // lines contribute nothing and attackZoomIn above owns the entire push-in.
        targetDist = Mathf.Max(minDistance, targetDist + attackOrbitDistance * _attackOrbitBlend);
        targetDist = Mathf.Max(minDistance, targetDist + _shotAngleDistance);

        // THE attack shot under attackZoomOnly: the ONLY thing an attack changes is the DISTANCE. The
        // camera stays on the exact axis it uses between turns (same yaw / pitch / height / side) and just
        // moves a little closer while the attack plays, then eases straight back out when it ends.
        targetDist = Mathf.Max(minDistance, targetDist - attackZoomIn * _attackFocusBlend);

        if (deltaTime > 0f)
        {
            // SNAP-ON-ENTRY for the zoom too: while the attack shot is just starting, the distance is
            // damped much faster so the push-in reads as a deliberate move onto the attacker rather than
            // a slow creep that only finishes after the hit. Once the shot is established it returns to
            // zoomSmoothTime, which keeps the hold steady.
            float zoomEntryT = Mathf.Clamp01(_attackOrbitBlend);
            float activeZoomSmoothTime = Mathf.Lerp(zoomSmoothTime * shotEntrySmoothScale, zoomSmoothTime, zoomEntryT);
            _currentDistance = Mathf.SmoothDamp(_currentDistance, targetDist, ref _currentZoomVelocity, activeZoomSmoothTime, Mathf.Infinity, deltaTime);
        }
        else
        {
            _currentDistance = targetDist;
        }

        // 3. 3D PLACEMENT.
        //
        //    NEUTRAL FRAMING (nobody attacking): the camera stands at the fixed world side chosen by
        //    viewSide + yawAngle + pitchAngle. That is the usual 3/4 side view of the two fighters.
        //
        //    ATTACK SHOT: when attackFromBehind is on, the framing is instead built from the ATTACKER'S
        //    OWN FACING captured at the start of the shot. The camera travels from its neutral side round
        //    to a point BEHIND the attacker and then keeps sweeping around its back, so the shot reads as
        //    "over the attacker's shoulder, looking at its back" and is never static. This is what fixes
        //    the camera always ending up in front of the attacking character.
        float baseYaw = viewSide == ViewSide.PositiveZ ? 0f : 180f;
        float orbitSign = _attackFocusSide.HasValue && _attackFocusSide.Value == PlayerUI.Side.Right ? -1f : 1f;
        float attackOrbitYaw = attackOrbitDegrees * orbitSign * _attackOrbitBlend;
        // PITCH: the base pitch is only blended in with the orbit blend, then the rolled shot angle's
        // pitch is added. This is what lets the camera come in chéo từ TRÊN xuống (positive pitch) on one
        // attack and chéo từ DƯỚI lên (negative pitch) on the next, while the neutral framing keeps the
        // untouched pitchAngle.
        float attackOrbitPitch = this.attackOrbitPitch * _attackOrbitBlend;
        float shotPitch = _shotAnglePitch;

        // --- Neutral placement (world-side framing) ---------------------------------------------
        Quaternion neutralOrbit = Quaternion.Euler(pitchAngle + attackOrbitPitch + shotPitch,
                                                   baseYaw + yawAngle + attackOrbitYaw + _shotAngleYaw, 0f);
        Vector3 neutralPosition = midpoint + (neutralOrbit * Vector3.forward) * _currentDistance;

        // --- Behind-the-back placement (relative to the attacker's facing) ----------------------
        // Built in the attacker's own frame:
        //   * -_attackFacingDir puts the camera BEHIND the attacker (its back is opposite its facing),
        //   * the sweep rotates it around that back so the shot keeps moving,
        //   * behindHeight lifts it for an over-the-shoulder read,
        //   * behindDistance + behindSweepDolly pull it in as the sweep progresses (zoom while rotating).
        Vector3 attackerFocusPos = ResolveAttackerPosition(leftPos, rightPos);

        // SAFETY: if a shot is somehow held without a captured facing (e.g. a legacy side-based
        // BeginAttackFocus call, which does not pass a Transform), derive the facing from the attacker
        // toward the opponent now. Without this the behind shot would be built off a stale/default
        // direction and could point the camera anywhere.
        if (_behindBlend > 0.001f && !_attackFacingCaptured)
        {
            Transform attackerT = _lastAttacker;
            if (attackerT == null && _attackFocusSide.HasValue)
                attackerT = _attackFocusSide.Value == PlayerUI.Side.Left ? targetLeft : targetRight;
            if (attackerT != null) CaptureAttackFacing(attackerT);
        }

        float sweepAngle = behindOrbitSweep * _behindSweep;
        Quaternion sweepRot = Quaternion.AngleAxis(sweepAngle, Vector3.up);
        Vector3 behindDir = sweepRot * (-_attackFacingDir);

        // Blend the standing height in so the camera rises off the ground plane rather than orbiting flat.
        Vector3 behindPosition = attackerFocusPos
                                 + behindDir * _currentDistance
                                 + Vector3.up * behindHeight;

        // Combine: at _behindBlend = 0 this is exactly the neutral position; at 1 it is fully behind.
        Vector3 desiredPosition = Vector3.Lerp(neutralPosition, behindPosition, _behindBlend);

        // The behind shot pulls in/out on its own terms, on top of the normal zoom, so it is never static.
        desiredPosition += behindDir * (behindDistance + behindSweepDolly * _behindSweep) * _behindBlend;

        // Lift the shot slightly while orbiting so the arc reads as a cinematic move around the fighter.
        desiredPosition.y += attackOrbitHeight * _attackOrbitBlend;

        // LATERAL SWING: the side offset follows WHO is attacking -- right attacker swings the camera to
        // -3, left attacker swings to +3, and back to the neutral offset when nobody is attacking.
        //
        // DISABLED under attackZoomOnly: a sideways slide would change the camera's position, not just its
        // distance, and the attack shot must be a pure zoom. The swing is forced to 0 for the whole shot,
        // so the camera keeps the neutral offset from the first frame to the last.
        //
        // (It was also already faded out during the behind-the-back shot: a hard sideways offset would
        // shove the camera off the attacker's back and back toward the front, which is exactly the framing
        // that shot was moving away from.)
        float targetLateral = lateralOffset;
        if (!attackZoomOnly && _attackFocusSide.HasValue && _attackFocusBlend > 0.001f)
        {
            float sideOffset = _attackFocusSide.Value == PlayerUI.Side.Right
                ? lateralOffsetRightAttacker
                : lateralOffsetLeftAttacker;
            targetLateral = Mathf.Lerp(lateralOffset, sideOffset, _attackFocusBlend);
        }
        targetLateral *= (1f - _behindBlend);

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
            // SNAP-ON-ENTRY: when an attack shot is only just starting (the blend is low but rising), the
            // camera uses a much SHORTER smooth time so it reaches the shot framing while the attack clip
            // is still swinging. A single long damping time for both entry and exit made the camera trail
            // the action: it was still gliding into place after the hit had already landed. As the blend
            // approaches 1 the camera settles back to the normal smoothTime, so the hold stays stable.
            float entryT = Mathf.Clamp01(_attackOrbitBlend);
            float activeSmoothTime = Mathf.Lerp(smoothTime * shotEntrySmoothScale, smoothTime, entryT);
            newPos = Vector3.SmoothDamp(transform.position - _shakeOffset, desiredPosition, ref _currentVelocity, activeSmoothTime, Mathf.Infinity, deltaTime);
        }
        else
        {
            newPos = desiredPosition;
        }

        // Apply shake offset
        transform.position = newPos + _shakeOffset;

        // 6. Camera rotation: LOOK AT the focus point rather than a fixed angle, so the fighters stay
        //    centred even though the camera is now offset laterally / vertically.
        //
        //    BEHIND-THE-BACK: while the shot sits behind the attacker, the camera looks at the ATTACKER
        //    itself (slightly above its centre) instead of the midpoint between the two fighters. That is
        //    what makes the frame read as "over the attacker's shoulder": its back fills the near side of
        //    the shot and the opponent is seen beyond it. Looking at the midpoint from behind would put
        //    the camera staring at the attacker's face again -- the exact problem being fixed.
        //
        //    The look target is lerped by the same behind blend as the position, so the aim swings round
        //    with the camera instead of snapping.
        //
        //    During an attack shot the desired look rotation is SMOOTHED with Quaternion.Slerp instead of
        //    being applied directly, so the view ROTATES around the attacker as a cinematic glide. With
        //    rotateViewDuringAttack OFF (or no attack held) the look-at is applied directly, which keeps
        //    the neutral framing locked to the fighters exactly as before.
        Vector3 lookTarget = Vector3.Lerp(midpoint, attackerFocusPos + Vector3.up * behindHeight, _behindBlend);
        Quaternion desiredRotation = Quaternion.LookRotation(lookTarget - transform.position, Vector3.up);
        if (rotateViewDuringAttack && (_attackOrbitBlend > 0.001f || _behindBlend > 0.001f))
        {
            // The slerp factor is derived from the blend and the frame time so the rotation tracks the
            // same easing curve as the position swing.
            float t = deltaTime > 0f ? Mathf.Clamp01(deltaTime / attackOrbitBlendTime) : 1f;
            _currentRotation = Quaternion.Slerp(_currentRotation, desiredRotation, t);
            transform.rotation = _currentRotation;
        }
        else
        {
            _currentRotation = desiredRotation;
            transform.rotation = desiredRotation;
        }
    }

    /// <summary>
    /// World position of the character the attack shot is framed on. Prefers the ATTACKER TRANSFORM
    /// supplied via <see cref="OnCharacterAttack"/>; falls back to the attacker's side fighter, and
    /// finally to the focal point between the two fighters when the attacker is unknown.
    /// </summary>
    private Vector3 ResolveAttackerPosition(Vector3 leftPos, Vector3 rightPos)
    {
        if (_lastAttacker != null) return VisualGroundPosition(_lastAttacker);

        if (_attackFocusSide.HasValue)
            return _attackFocusSide.Value == PlayerUI.Side.Left ? leftPos : rightPos;

        // Unmapped attacker: frame the focal point between the two fighters.
        return (leftPos + rightPos) * 0.5f;
    }

    private static Vector3 VisualGroundPosition(Transform fighter)
    {
        Vector3 position = fighter.position;
        var combat = fighter.GetComponent<CharacterCombat>();
        if (combat != null && combat.SourcePlayback != null &&
            (combat.SourcePlayback.Playing || combat.IsDead))
            return combat.SourcePlayback.CameraPosition(combat);
        var rig = combat != null ? combat.weaponRig : null;
        if (rig != null && rig.ActiveDriver != null)
        {
            // Include authored jumps as well as lateral travel, relative to standing hip height.
            position = rig.CameraGroundPosition;
        }
        return position;
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

        // BEHIND-THE-BACK preview: for each fighter, draws the direction the camera will travel to get
        // behind it (green = directly behind, magenta = the far end of the sweep) joined by an arc. This
        // is the framing the attack shot uses, so it can be checked without entering play mode.
        if (attackFromBehind)
        {
            DrawBehindGizmo(targetLeft, targetRight);
            DrawBehindGizmo(targetRight, targetLeft);
        }

        if (clampHorizontal)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(new Vector3(minStageX, 0f, 0f), new Vector3(minStageX, 5f, 0f));
            Gizmos.DrawLine(new Vector3(maxStageX, 0f, 0f), new Vector3(maxStageX, 5f, 0f));
        }

        // ATTACK ORBIT preview: draws the arc the camera sweeps while an attack shot is held, around the
        // attacker (or the focal point when no attacker Transform is known). When dynamic shot angles are
        // on, one arc is drawn PER angle in the pool, so you can see the high / low / diagonal variety
        // before entering play mode. Handy for tuning attackOrbitDegrees / attackOrbitDistance.
        Vector3 focus = _lastAttacker != null
            ? _lastAttacker.position
            : (targetLeft != null && targetRight != null
                ? (targetLeft.position + targetRight.position) * 0.5f + midpointOffset
                : transform.position);

        float baseYawGizmo = viewSide == ViewSide.PositiveZ ? 0f : 180f;
        int steps = 16;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);

        // When the pool is populated, preview each angle separately; otherwise preview the single
        // attackOrbitPitch shot.
        if (useDynamicShotAngles && shotAngles != null && shotAngles.Length > 0)
        {
            for (int a = 0; a < shotAngles.Length; a++)
            {
                if (shotAngles[a].weight <= 0f) continue;
                DrawOrbitArc(focus, baseYawGizmo, shotAngles[a].pitch, shotAngles[a].yaw,
                             shotAngles[a].distance, steps);
            }
        }
        else
        {
            DrawOrbitArc(focus, baseYawGizmo, attackOrbitPitch, 0f, 0f, steps);
        }
    }

    /// <summary>
    /// Draws, for one attacker, the camera positions the behind-the-back shot sweeps through: the start
    /// (directly behind the attacker, green) and the end of the sweep (magenta), joined by an arc. The
    /// attacker's facing is taken as "from the attacker toward its opponent", matching CaptureAttackFacing.
    /// </summary>
    private void DrawBehindGizmo(Transform attacker, Transform victim)
    {
        if (attacker == null) return;

        Vector3 facing = victim != null ? victim.position - attacker.position : attacker.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;
        facing.Normalize();

        Vector3 origin = attacker.position;
        float dist = Mathf.Max(minDistance, baseDistance + attackOrbitDistance + behindDistance);

        // Directly behind the attacker.
        Vector3 behindDir = -facing;
        Gizmos.color = Color.green;
        Gizmos.DrawLine(origin, origin + behindDir * dist);

        // The far end of the sweep.
        Vector3 sweepDir = Quaternion.AngleAxis(behindOrbitSweep, Vector3.up) * behindDir;
        Gizmos.color = Color.magenta;
        Gizmos.DrawLine(origin, origin + sweepDir * dist);

        // Arc between the two, so the travelling shot is visible.
        int arcSteps = 12;
        Vector3 prevArc = Vector3.zero;
        for (int i = 0; i <= arcSteps; i++)
        {
            Vector3 dir = Quaternion.AngleAxis(behindOrbitSweep * (i / (float)arcSteps), Vector3.up) * behindDir;
            Vector3 p = origin + dir * dist;
            if (i > 0) Gizmos.DrawLine(prevArc, p);
            prevArc = p;
        }
    }

    /// <summary>
    /// Draws one gizmo arc showing the path the camera sweeps for a given shot angle (from neutral to
    /// the fully-orbited position), so the angle pool can be tuned without playing the game.
    /// </summary>
    private void DrawOrbitArc(Vector3 focus, float baseYawGizmo, float anglePitch, float angleYaw,
                              float angleDistance, int steps)
    {
        Vector3 prev = Vector3.zero;
        for (int i = 0; i <= steps; i++)
        {
            float k = i / (float)steps;
            float yaw = baseYawGizmo + yawAngle + Mathf.Lerp(0f, attackOrbitDegrees, k) + Mathf.Lerp(0f, angleYaw, k);
            float pitch = pitchAngle + Mathf.Lerp(0f, attackOrbitPitch + anglePitch, k);
            float dist = Mathf.Max(minDistance,
                                   baseDistance + Mathf.Lerp(0f, attackOrbitDistance + angleDistance, k));
            Vector3 p = focus + (Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward) * dist;
            if (i > 0) Gizmos.DrawLine(prev, p);
            prev = p;
        }
    }
}

// ---------------------------------------------------------------------------------------------
// CINEMACHINE NOTES (only if you would rather drive a virtual camera than this transform)
// ---------------------------------------------------------------------------------------------
// This component already implements the interpolation itself with Vector3.SmoothDamp (position) and
// Quaternion.Slerp (rotation), so it needs NO extra package. If you prefer Cinemachine instead:
//
//   1. Add a CinemachineVirtualCamera (or CinemachineCamera in CM 3.x) targeting a "CombatTarget"
//      empty that sits at the focal point, and give it a Framing Transposer + a slight Dutch/Orbit.
//   2. Replace the body of UpdateCameraPosition with code that moves that empty, e.g.
//          combatTarget.position = Vector3.Lerp(midpoint, attackerPos + midpointOffset,
//                                               _attackFocusBlend * attackFocusBias);
//      and let Cinemachine handle the damping, or feed the values into a CinemachineOrbitalTransposer
//      and set its m_XAxis.Value toward the attacker's yaw for the orbit.
//   3. Drive the zoom with the vcam's Lens.FieldOfView (or the Transposer's camera distance) instead of
//      _currentDistance, using the same Mathf.SmoothDamp calls used here.
//   4. Call OnCharacterAttack / OnCharacterAttackFinished exactly the same way (AnimationEvent or
//      StateMachineBehaviour); only the code that consumes the focus state changes.
//
// Everything else in this file (attack-shot state, auto-release safety valve, screen shake, stage
// clamping, gizmos) is engine-agnostic and can stay as-is.
