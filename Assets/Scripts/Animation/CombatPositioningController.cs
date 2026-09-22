using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PROJECT ARCHITECTURE: Presentation / Animation Layer
/// ROLE: Owns fighter placement AND the SINGLE serialized queue for the whole match flow.
///
/// FLOW (why this exists):
/// The backend emits gameplay events (attack selected, damage applied, getup, ...). Those events
/// must NOT drive the fighter transforms directly: two events arriving close together would fight
/// over position/animation and the models stutter or slide. Instead every event is ENQUEUED here and
/// drained ONE AT A TIME.
///
/// THIS IS THE ONLY QUEUE. GameManager no longer keeps a second queue: it enqueues each gameplay
/// event (with the work it needs applied) straight into this one. A single work item can carry:
///     - an optional APPLY delegate (GameManager applies the event: UI, state, hit routing, ...),
///     - an optional MOVE (attacker steps to the spot in front of the victim),
///     - optional ANIMATION playback (attacker attack + target hit reaction),
///     - an optional WAIT predicate (advance as soon as it reports done) plus a safety hold.
/// The item runs to completion before the next starts, so the sequence is always
/// "move -> animate -> move -> animate" and never overlaps. One queue means there is no second
/// scheduler that could race this one.
///
/// RESPONSIBILITIES:
/// - Hold the two fighter TRANSFORMS itself (leftFighter / rightFighter), split by SIDE.
/// - Keep both models on the same Z fighting line (locked every frame) and face them at each other.
/// - Bring each model back to its ground height once a clip ends (never leave it floating/sunk).
/// - Provide the attack spot (a point in front of the victim) and step the attacker to it, driving
///   the animator "Speed" float so the model RUNS there and stops on arrival.
/// - Own and run the single event queue that serializes event application, moves and playback.
///
/// RUNTIME LOCKS (enforced every frame in LateUpdate, after all animation writes):
///   - Z: pinned to the captured fighting line, so a clip can never drift the model forward/backward.
///   - Rotation: pinned to the captured facing (yaw), so a clip can never spin/tilt the model while
///     it plays. The facing correction between turns updates the locked yaw (ApplyFacingAfterAnim).
/// X is never locked (step-move axis + lateral nudge). Y is never locked (jump / flying clips).
///
/// POST-ANIM CORRECTION (computed ONCE, after an animation finishes -> queue step 4):
///   - Facing: yaw is turned toward the opponent, pitch/roll cleared (ApplyFacingAfterAnim).
///   - Ground: Y is eased back to the captured height (SnapYToGround), because airborne clips must be
///     free to lift the model while they play and only get grounded once they end.
///
/// ANIMATION AUTHORITY: CharacterAnimatorBridge only. This component is self-contained and does not
/// own playback. Playback is delegated through a delegate supplied by the owner (GameManager), which
/// routes to the CharacterAnimatorBridge animators.
/// </summary>
[DisallowMultipleComponent]
public class CombatPositioningController : MonoBehaviour
{
    public static CombatPositioningController Instance { get; private set; }

    /// <summary>
    /// Callback the owner supplies so this controller can actually play a clip on a side.
    /// Parameters: side, resolved animation id, crossfade seconds. Return the clip length in seconds
    /// (0 when unknown) so the queue can wait for it before advancing.
    /// </summary>
    public delegate float PlayAnimationDelegate(PlayerUI.Side side, string animationId, float crossFade);

    /// <summary>
    /// Callback the owner supplies so this controller can drive a fighter's locomotion blend while it
    /// steps to the attack spot. value is 0..1 (0 = idle/stop, 1 = full run). Routes to the bridge's
    /// Animator "Speed" float, so the run animation plays during the move and stops on arrival.
    /// </summary>
    public delegate void MoveSpeedDelegate(PlayerUI.Side side, float value);

    /// <summary>
    /// Callback the owner supplies so this controller can ask whether a fighter's animator has left
    /// its locomotion (Walk/Run) blend and is safe to fire a one-shot trigger. Returns true when
    /// settled (or when the owner has no better information). Used to avoid firing an attack while the
    /// model is still in Walk, which would swallow the trigger.
    /// </summary>
    public delegate bool LocomotionSettledDelegate(PlayerUI.Side side);

    /// <summary>
    /// Callback the owner supplies so this controller can poll whether a side's currently-playing
    /// clip has finished. Lets the queue wait for the REAL clip end instead of a guessed duration, so
    /// an animation is never cut off by an early advance.
    /// </summary>
    public delegate bool AnimationFinishedDelegate(PlayerUI.Side side);

    [Header("Fighter Transforms (by side)")]
    [Tooltip("Transform (root) of the LEFT-side fighter. Auto-resolved from the scene when left empty.")]
    [SerializeField] private Transform leftFighter;
    [Tooltip("Transform (root) of the RIGHT-side fighter. Auto-resolved from the scene when left empty.")]
    [SerializeField] private Transform rightFighter;

    [Header("Ground / Facing Lock")]
    [Tooltip("Lock each fighter's Z every frame so they stay on the same fighting line and can never drift forward/backward.")]
    [SerializeField] private bool lockDepthZ = true;
    [Tooltip("Reset each fighter's Y to its captured ground height AFTER an animation finishes (not every frame), so jump / flying clips can lift the model freely while playing.")]
    [SerializeField] private bool resetGroundYAfterAnim = true;
    [Tooltip("Lock each fighter's rotation every frame (at runtime) while a clip plays, so an animation can never spin/tilt the model off its captured facing.")]
    [SerializeField] private bool lockRotationRuntime = true;
    [Tooltip("Force both fighters to face EACH OTHER (yaw only, pitch/roll cleared) when correcting between turns so clips cannot leave a model facing the wrong way.")]
    [SerializeField] private bool faceEachOther = true;
    [Tooltip("Seconds to ease a fighter's Y back down to the ground after a clip ends. 0 = snap instantly.")]
    [SerializeField] private float groundResetDuration = 0.15f;

    [Header("Attack Spot")]
    [Tooltip("Distance in front of the victim (along the victim's forward axis) where the attacker should stand to land the next hit.")]
    [SerializeField] private float attackSpotDistance = 0.9f;
    [Tooltip("Distance scale used for a LIGHT attack so the two fighters stand closer together. 0.5 = the attacker stops at half of attackSpotDistance. Heavy attacks always use the full distance.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float lightAttackSpotScale = 0.5f;
    [Tooltip("After an exchange that moved someone, walk BOTH fighters back to their match-start positions so they stay symmetric around the fixed battle centre before the next turn.")]
    [SerializeField] private bool returnHomeAfterExchange = true;
    [Tooltip("Extra world-space offset applied on top of the computed spot (X = lateral nudge, Y = height, Z = depth). Usually left at zero.")]
    [SerializeField] private Vector3 attackSpotOffset = Vector3.zero;

    [Header("Movement")]
    [Tooltip("Seconds the attacker takes to step to the attack spot.")]
    [SerializeField] private float moveDuration = 0.35f;
    [Tooltip("Easing curve for the step. Defaults to a smooth ease-in-out when left empty.")]
    [SerializeField] private AnimationCurve moveEase;
    [Tooltip("Drive the animator 'Speed' float while stepping (1 = run) so the run animation plays during the move and stops (0) on arrival.")]
    [SerializeField] private bool driveMoveSpeed = true;
    [Tooltip("Locomotion 'Speed' value used while stepping (0..1). 1 = full run.")]
    [Range(0f, 1f)]
    [SerializeField] private float moveSpeedValue = 1f;
    [Tooltip("Seconds to wait after the move (Speed back to 0) before firing the attack, so the Animator blends out of Run and back into Idle (Walk->Idle blend is ~0.15s) and the attack trigger is not swallowed. 0 = a single frame.")]
    [SerializeField] private float moveSettleDelay = 0.15f;
    [Tooltip("Safety cap (seconds) for polling until the animator reports it left the locomotion blend before firing the attack.")]
    [SerializeField] private float maxMoveSettleWait = 1.0f;

    [Header("Animation Timing")]
    [Tooltip("Default seconds to wait for a clip when the playback delegate cannot report its length (safety fallback).")]
    [SerializeField] private float defaultAnimationHold = 0.6f;
    [Tooltip("Small extra pause after a clip finishes so consecutive actions do not butt up against each other.")]
    [SerializeField] private float postAnimationSettleDelay = 0.05f;
    [Tooltip("Default crossfade used when firing queued animation playback (seconds).")]
    [SerializeField] private float defaultCrossFade = 0.1f;

    [Header("Spacing (stability over speed)")]
    [Tooltip("Master multiplier applied to EVERY spacing gap below. 1 = the configured values; 2 = everything twice as far apart. Use this to breathe the whole sequence without retuning each field.")]
    [Range(0.1f, 5f)]
    [SerializeField] private float spacingScale = 1f;
    [Tooltip("Extra pause inserted BEFORE an exchange fires its clips, so the step-in fully settles first.")]
    [SerializeField] private float preExchangeSpacing = 0.25f;
    [Tooltip("Extra pause added AFTER a clip finishes and before the next queued item starts. Keeps consecutive animations from blending into each other.")]
    [SerializeField] private float betweenActionsSpacing = 0.4f;
    [Tooltip("Extra pause after the fighters have returned home, before the next turn's exchange begins.")]
    [SerializeField] private float afterReturnHomeSpacing = 0.35f;
    [Tooltip("When true, the queue ALWAYS waits the full estimated clip length instead of advancing as soon as the animator reports the clip done. Slower but the most stable (no early cut-offs).")]
    [SerializeField] private bool alwaysWaitFullClipLength = true;

    [Header("Queue")]
    [Tooltip("Safety cap (seconds) a single queued action may hold the queue before force-advancing.")]
    [SerializeField] private float maxActionHold = 15f;
    [Tooltip("Extra seconds past the minimum hold that the post-attack hold will wait for the victim's hit clip to end. Must exceed the longest hit reaction (the heavy hits here are ~6s), otherwise the getup fires while the victim is still on the ground. A DEAD victim short-circuits this anyway.")]
    [SerializeField] private float maxHoldOvershoot = 8f;

    [Header("Diagnostics")]
    [Tooltip("Log queue steps, moves and playback (verbose debugging). All messages are prefixed with 'Animation'.")]
    [SerializeField] private bool verboseLogging = true;

    // ------------------------------------------------------------------
    // Logging
    // ------------------------------------------------------------------
    // Every log emitted by this controller starts with "Animation" so the whole animation pipeline
    // can be filtered in the console with a single search string.
    private const string LogTag = "Animation";

    /// <summary>Logs an animation message prefixed with the shared "Animation" tag.</summary>
    private void Alog(string message)
    {
        Debug.Log($"{LogTag} [Positioning] {message}", this);
    }

    /// <summary>Logs an animation WARNING prefixed with the shared "Animation" tag.</summary>
    private void AlogWarn(string message)
    {
        Debug.LogWarning($"{LogTag} [Positioning] {message}", this);
    }

    /// <summary>Logs an animation ERROR prefixed with the shared "Animation" tag.</summary>
    private void AlogError(string message)
    {
        Debug.LogError($"{LogTag} [Positioning] {message}", this);
    }

    // ------------------------------------------------------------------
    // Queue (the ONE serialized queue for the whole match flow)
    // ------------------------------------------------------------------

    /// <summary>
    /// One queued unit of work. Everything is optional so the same item type can express a pure
    /// "apply event" step, a "move+animate" combat exchange, or any combination.
    /// </summary>
    private sealed class QueuedAction
    {
        public string label;

        /// <summary>
        /// Backend turn this item belongs to (MemeBattleEvent.turnId, may be null/empty for items that
        /// are not part of a turn). Used as a per-turn barrier: the queue must fully drain every item of
        /// the CURRENT turn before it may start an item of a LATER turn, and it must never run an item of
        /// an OLDER turn (a stale/out-of-order replay).
        /// </summary>
        public string turnId;

        /// <summary>Synchronous work applied when the item starts (GameManager's event application).</summary>
        public System.Action apply;

        // Move (attacker steps to the spot in front of the victim).
        public bool moveAttacker;
        public PlayerUI.Side attackerSide;
        public PlayerUI.Side victimSide;

        // Animation playback (issued after the move).
        public string attackerAnimationId;
        public string victimAnimationId;
        public float crossFade;
        public bool playAnimation;

        /// <summary>
        /// Optional action run IMMEDIATELY after the animation pair of this item finishes, still inside
        /// the same queue step. Used to chain a getup onto a knockout exchange so the victim is back on
        /// its feet BEFORE the next queued turn can play -- without waiting for the (possibly much later)
        /// DAMAGE_APPLIED item that would otherwise own the getup.
        /// </summary>
        public System.Action afterAnimation;
        /// <summary>Minimum seconds to hold BEFORE running <see cref="afterAnimation"/> (lets the hit reaction finish).</summary>
        public float afterAnimationHold;
        /// <summary>Seconds to wait for the CHAINED clip (e.g. the getup) to play through before advancing.</summary>
        public float afterAnimationSettle;

        /// <summary>Safety upper bound to hold the queue (seconds).</summary>
        public float maxHold;

        /// <summary>Optional precise completion predicate; the queue advances as soon as it returns true.</summary>
        public System.Func<bool> isFinished;
    }

    private readonly Queue<QueuedAction> _queue = new Queue<QueuedAction>();
    private Coroutine _queueRunner;
    private bool _isRunningAction;

    // ---- Per-turn barrier ---------------------------------------------------------------------------
    // The queue runs ONE item at a time and each item holds the queue until its exchange is fully
    // settled, so every item of a turn is already drained before the next turn's first item runs. These
    // fields make that guarantee explicit and enforceable:
    //   * _runningTurnId     = turn of the item currently executing (null while idle / between turns),
    //   * _lastCompletedTurn = turn of the last item we finished,
    //   * _turnFirstSeen     = monotonic order of turns as they first appeared (the backend turnId is a
    //                          string, not necessarily sortable, so we compare arrival order instead).
    // An item whose turn was already completed (a stale re-delivery) is dropped instead of run.
    private string _runningTurnId;
    private string _lastCompletedTurnId;
    private readonly System.Collections.Generic.Dictionary<string, int> _turnOrder = new System.Collections.Generic.Dictionary<string, int>();
    private int _turnOrderCounter;

    /// <summary>True while a queued action is executing (used to gate overlapping requests).</summary>
    public bool IsBusy => _isRunningAction || _queue.Count > 0;
    /// <summary>Number of queued actions still waiting to run.</summary>
    public int PendingCount => _queue.Count;

    // ------------------------------------------------------------------
    // State
    // ------------------------------------------------------------------

    // Current choreography state.
    private Vector3 _currentAttackSpot;
    private bool _hasAttackSpot;

    // Lock state: the Z (fighting line) each fighter is pinned to every frame, and the ground Y each
    // fighter returns to once a clip finishes. Captured on the first assignment.
    private bool _groundCaptured;
    private float _leftGroundY;
    private float _rightGroundY;
    private float _leftLockedZ;
    private float _rightLockedZ;
    private float _leftLockedYaw;
    private float _rightLockedYaw;

    // FIXED BATTLE CENTRE, captured ONCE when the fighters are first wired (i.e. at the start of the
    // match). It never moves afterwards, so the arena midpoint stays where the scene placed it instead
    // of drifting with whoever last stepped forward.
    private bool _homeCaptured;
    private Vector3 _homeLeftPos;
    private Vector3 _homeRightPos;

    /// <summary>World-space midpoint between the two fighters as captured at match start (fixed).</summary>
    public Vector3 BattleCenter => _homeCaptured
        ? (_homeLeftPos + _homeRightPos) * 0.5f
        : Vector3.zero;

    /// <summary>The most recently computed attack spot (world space). Valid only when HasAttackSpot is true.</summary>
    public Vector3 CurrentAttackSpot => _currentAttackSpot;
    /// <summary>True once an attack spot has been computed at least once.</summary>
    public bool HasAttackSpot => _hasAttackSpot;

    /// <summary>Root transform of the LEFT-side fighter (the position input).</summary>
    public Transform LeftFighter => leftFighter;
    /// <summary>Root transform of the RIGHT-side fighter (the position input).</summary>
    public Transform RightFighter => rightFighter;

    /// <summary>
    /// Playback hook supplied by the owner (GameManager). Routes to the bridge animators and returns
    /// the clip length so the queue can pace itself. When null, animation steps degrade to a fixed hold.
    /// </summary>
    public PlayAnimationDelegate PlayAnimationHandler { get; set; }

    /// <summary>
    /// Locomotion hook supplied by the owner (GameManager). Drives the fighter's animator "Speed" float
    /// so a run animation plays while stepping and stops on arrival. When null, stepping does not touch
    /// the animator speed.
    /// </summary>
    public MoveSpeedDelegate MoveSpeedHandler { get; set; }

    /// <summary>
    /// Locomotion-settled hook supplied by the owner (GameManager). Polled after a move to know when
    /// the model has left Walk/Run and returned to Idle before firing the attack trigger.
    /// </summary>
    public LocomotionSettledDelegate LocomotionSettledHandler { get; set; }

    /// <summary>
    /// Animation-finished hook supplied by the owner (GameManager). Polled while waiting out a clip so
    /// the queue advances exactly when the clip ends (the duration is only the safety cap).
    /// </summary>
    public AnimationFinishedDelegate AnimationFinishedHandler { get; set; }

    /// <summary>
    /// Invoked immediately BEFORE the attack/hit pair is fired, i.e. at the exact moment the exchange's
    /// clips start. The owner uses this to flush the pending damage UI so the health bar drops with the
    /// swing instead of during the step-in or after the whole clip.
    /// </summary>
    public System.Action AnimationStartingHandler { get; set; }

    /// <summary>
    /// Invoked once an exchange's clips have finished and the fighters are back home. The owner uses
    /// this to release the camera's attack shot so it returns to the midpoint between the fighters.
    /// </summary>
    public System.Action AttackFinishedHandler { get; set; }

    /// <summary>
    /// Non-mutating "is this side still playing its watched action clip?" hook, supplied by the owner.
    /// Unlike <see cref="AnimationFinishedHandler"/> this does not consume the watch state, so it can be
    /// polled repeatedly (e.g. to hold a chained getup until the hit reaction has truly run through).
    /// When null, callers fall back to the finished hook.
    /// </summary>
    public System.Func<PlayerUI.Side, bool> SideActionPlayingHandler { get; set; }

    /// <summary>
    /// Hook the owner supplies to report whether a side is idle AND fully settled (standing idle pose,
    /// not knocked down, no pending getup). The queue polls this to advance IMMEDIATELY once both
    /// fighters are idle, instead of sitting out the remaining safety/timing holds. When null the
    /// fast-path is disabled and the queue falls back to the timed behaviour.
    /// </summary>
    public System.Func<PlayerUI.Side, bool> SideIdleSettledHandler { get; set; }

    /// <summary>
    /// True when BOTH fighters are idle and settled, i.e. nothing is animating any more. Used as an
    /// early-out so a queued item does not wait out its full hold when the work is already visibly done
    /// (this is what made the pause between turns feel too long).
    /// </summary>
    private bool BothFightersIdleSettled()
    {
        if (SideIdleSettledHandler == null) return false;
        try
        {
            bool left = SideIdleSettledHandler(PlayerUI.Side.Left);
            bool right = SideIdleSettledHandler(PlayerUI.Side.Right);
            return left && right;
        }
        catch { return false; }
    }

    /// <summary>True when the side's watched action clip is still playing (falls back to "not finished").</summary>
    private bool IsSideActionPlaying(PlayerUI.Side side)
    {
        if (SideActionPlayingHandler != null)
        {
            try { return SideActionPlayingHandler(side); }
            catch { /* fall through */ }
        }

        // Fallback: treat "not finished" as "playing".
        if (AnimationFinishedHandler == null) return false;
        try { return !AnimationFinishedHandler(side); }
        catch { return false; }
    }

    /// <summary>
    /// True ONLY when the side is permanently DEAD.
    ///
    /// This deliberately does NOT use the general "idle + settled" check any more. A knocked-down
    /// victim is also temporarily "not playing an action", so that check ended the hold after the
    /// minimum beat (~1.8s) while a 6s heavy hit reaction was still running -- the fighter then stood
    /// up mid-clip and was dragged home while still down. Only a real death may cut the hold short;
    /// a survivor must be waited out until the hit clip itself finishes.
    /// </summary>
    private bool IsSideDead(PlayerUI.Side side)
    {
        if (SideDeadHandler == null) return false;
        try { return SideDeadHandler(side); }
        catch { return false; }
    }

    /// <summary>True when the side reports idle+settled (or no handler is wired).</summary>
    private bool IsSideIdleSettled(PlayerUI.Side side)
    {
        if (SideIdleSettledHandler == null) return true;
        try { return SideIdleSettledHandler(side); }
        catch { return true; }
    }

    /// <summary>
    /// Owner-supplied "is this side dead?" hook. Used to end a post-attack hold immediately on a lethal
    /// hit (whose KO pose loops forever and never reports clip-finished).
    /// </summary>
    public System.Func<PlayerUI.Side, bool> SideDeadHandler { get; set; }

    private void Awake()
    {
        Instance = this;
        ResolveFighterTransforms();
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

    /// <summary>Makes sure exactly one runner coroutine is draining the queue.</summary>
    private void EnsureQueueRunning()
    {
        if (_queueRunner == null && _queue.Count > 0)
            _queueRunner = StartCoroutine(RunQueue());
    }

    // ------------------------------------------------------------------
    // Fighter wiring
    // ------------------------------------------------------------------

    /// <summary>
    /// Public entry point so the two fighter transforms can be fed in from outside (e.g. GameManager
    /// resolving the fighters from the backend character ids), split by side. Passing null leaves
    /// the current value. Captures the ground lock on the first assignment.
    /// </summary>
    public void SetFighters(Transform left, Transform right)
    {
        if (left != null) leftFighter = left;
        if (right != null) rightFighter = right;
        CaptureGroundLock();
    }

    /// <summary>Returns the fighter root for a given side (null when that side is not wired).</summary>
    public Transform GetFighter(PlayerUI.Side side)
    {
        return side == PlayerUI.Side.Left ? leftFighter : rightFighter;
    }

    /// <summary>Returns the opposite side. Convenience for "who is my opponent".</summary>
    public static PlayerUI.Side Opposite(PlayerUI.Side side)
    {
        return side == PlayerUI.Side.Left ? PlayerUI.Side.Right : PlayerUI.Side.Left;
    }

    /// <summary>
    /// Fills in any fighter transform that was not assigned in the Inspector. Uses whatever
    /// CharacterAnimatorBridge animators exist in the scene (left = smaller world X on the line).
    /// </summary>
    private void ResolveFighterTransforms()
    {
        if (leftFighter != null && rightFighter != null) { CaptureGroundLock(); return; }

#if UNITY_2023_1_OR_NEWER
        var bridges = FindObjectsByType<CharacterAnimatorBridge>(FindObjectsSortMode.None);
#else
        var bridges = FindObjectsOfType<CharacterAnimatorBridge>();
#endif
        foreach (var bridge in bridges)
        {
            if (bridge == null || bridge.Animator == null) continue;
            var t = bridge.Animator.transform;

            if (leftFighter == null && (rightFighter == null || t.position.x <= rightFighter.position.x))
                leftFighter = t;
            else if (rightFighter == null)
                rightFighter = t;
        }

        if (leftFighter != null || rightFighter != null) CaptureGroundLock();

        if (verboseLogging && (leftFighter != null || rightFighter != null))
            Alog($"Resolved fighters: left='{leftFighter?.name}', right='{rightFighter?.name}'.");
    }

    // ------------------------------------------------------------------
    // Ground / facing lock
    // ------------------------------------------------------------------

    /// <summary>
    /// Records the ground Y each fighter should be pinned to. Called on the first frame a fighter is
    /// available, and again whenever the fighters are (re)assigned, so the lock always reflects the
    /// placement the scene intended rather than a drifting mid-clip pose.
    /// </summary>
    private void CaptureGroundLock()
    {
        if (leftFighter != null)
        {
            _leftGroundY = leftFighter.position.y;
            _leftLockedZ = leftFighter.position.z;
            _leftLockedYaw = leftFighter.eulerAngles.y;
        }
        if (rightFighter != null)
        {
            _rightGroundY = rightFighter.position.y;
            _rightLockedZ = rightFighter.position.z;
            _rightLockedYaw = rightFighter.eulerAngles.y;
        }
        _groundCaptured = leftFighter != null || rightFighter != null;

        // Capture the FIXED home positions exactly once, the first time both fighters are known (match
        // start). Later re-captures of the ground/Z lock must NOT move the home midpoint, otherwise the
        // "centre" would follow whoever last stepped forward.
        if (!_homeCaptured && leftFighter != null && rightFighter != null)
        {
            _homeLeftPos = leftFighter.position;
            _homeRightPos = rightFighter.position;
            _homeCaptured = true;

            if (verboseLogging)
                Alog($"Captured FIXED battle centre = {BattleCenter} (left={_homeLeftPos}, right={_homeRightPos}).");
        }
    }

    /// <summary>Re-captures the Z / ground-height lock at the fighters' CURRENT pose (e.g. after a legitimate reposition).</summary>
    public void RebaseGroundLock()
    {
        CaptureGroundLock();
    }

    /// <summary>
    /// Per-frame lock applied to a single fighter root in LateUpdate:
    /// - Z is pinned to the captured fighting line, so a clip can never drift the model forward/back.
    /// - Rotation is pinned to the captured facing, so a clip can never spin/tilt the model while it
    ///   plays (the facing correction between turns is applied separately, in ApplyFacingAfterAnim).
    /// X and Y are left untouched (X = step move, Y = jump / flying clips).
    /// </summary>
    private void ApplyRuntimeLock(Transform fighter, float lockedZ, float lockedYaw)
    {
        if (fighter == null) return;

        Vector3 pos = fighter.position;
        float z = lockDepthZ ? lockedZ : pos.z;
        fighter.position = new Vector3(pos.x, pos.y, z);

        if (lockRotationRuntime)
            fighter.rotation = Quaternion.Euler(0f, lockedYaw, 0f);
    }

    private void LateUpdate()
    {
        // Enforce Z and (optionally) rotation every frame, after all animation writes, so a running
        // clip can never slide the model off the fighting line or spin/tilt it. Y is untouched so
        // airborne clips can rise and land on their own.
        if (!_groundCaptured) return;
        ApplyRuntimeLock(leftFighter, _leftLockedZ, _leftLockedYaw);
        ApplyRuntimeLock(rightFighter, _rightLockedZ, _rightLockedYaw);
    }

    /// <summary>
    /// Corrects the facing (yaw toward the opponent, pitch/roll cleared) for a fighter. Called AFTER an
    /// animation finishes (queue step 4), not every frame, so a clip is never fought while it plays.
    /// </summary>
    private void ApplyFacingAfterAnim(Transform fighter, Transform opponent)
    {
        if (fighter == null || opponent == null || !faceEachOther) return;

        Vector3 pos = fighter.position;
        Vector3 dir = opponent.position - pos;
        dir.y = 0f;

        float yaw = dir.sqrMagnitude >= 0.0001f
            ? Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles.y
            : fighter.eulerAngles.y; // degenerate: keep current yaw, just flatten pitch/roll
        fighter.rotation = Quaternion.Euler(0f, yaw, 0f);

        // Update the runtime lock so LateUpdate keeps this NEW facing instead of snapping the model
        // back to the pre-correction yaw it captured earlier.
        if (fighter == leftFighter) _leftLockedYaw = yaw;
        else if (fighter == rightFighter) _rightLockedYaw = yaw;
    }

    /// <summary>
    /// Eases a fighter's Y back down to its captured ground height. Called AFTER an animation finishes
    /// so a jump / flying clip that lifted the model is brought back to the floor without fighting the
    /// clip while it plays. Y is also re-locked to the captured value (X/Z/rotation untouched).
    /// </summary>
    private IEnumerator SnapYToGround(Transform fighter, float groundY)
    {
        if (fighter == null) yield break;

        if (groundResetDuration <= 0f)
        {
            fighter.position = new Vector3(fighter.position.x, groundY, fighter.position.z);
            yield break;
        }

        float startY = fighter.position.y;
        float elapsed = 0f;
        while (elapsed < groundResetDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / groundResetDuration);
            if (fighter == null) yield break;
            fighter.position = new Vector3(fighter.position.x, Mathf.Lerp(startY, groundY, t), fighter.position.z);
            yield return null;
        }

        if (fighter != null)
            fighter.position = new Vector3(fighter.position.x, groundY, fighter.position.z);
    }

    /// <summary>Returns the captured ground height for a side (used when resetting Y after a clip).</summary>
    private float GroundYForSide(PlayerUI.Side side)
    {
        return side == PlayerUI.Side.Left ? _leftGroundY : _rightGroundY;
    }

    /// <summary>True when the fighter's Y is already at (or negligibly close to) the given ground height.</summary>
    private static bool IsAtGroundY(Transform fighter, float groundY)
    {
        if (fighter == null) return true;
        return Mathf.Abs(fighter.position.y - groundY) <= 0.001f;
    }

    // ------------------------------------------------------------------
    // Attack spot / movement
    // ------------------------------------------------------------------

    /// <summary>
    /// Computes the attack spot in front of the victim, without moving anyone.
    /// Returns zero when no victim is supplied.
    /// </summary>
    /// <param name="distanceScale">
    /// Multiplier applied to <see cref="attackSpotDistance"/>. 1 = the configured distance; 0.5 = the
    /// attacker stops halfway in (used for light attacks so the two fighters stand closer together).
    /// </param>
    public Vector3 ComputeAttackSpot(Transform victim, float distanceScale = 1f)
    {
        if (victim == null) return Vector3.zero;

        // "In front of the victim" = along the victim's forward axis. The victim faces its
        // opponent (facing lock keeps it that way), so this lands the attacker on the victim's front.
        Vector3 forward = victim.forward;
        forward.y = 0f;                 // keep the step on the ground plane
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        float distance = attackSpotDistance * Mathf.Max(0f, distanceScale);
        Vector3 spot = victim.position + forward * distance;

        // Keep the ground height of the victim so fighters never sink or float.
        spot.y = victim.position.y + attackSpotOffset.y;
        spot += new Vector3(attackSpotOffset.x, 0f, attackSpotOffset.z);

        _currentAttackSpot = spot;
        _hasAttackSpot = true;

        if (verboseLogging)
            Alog($"Attack spot for victim '{victim.name}' = {spot} (distance={distance:0.###}, scale={distanceScale:0.###})");

        return spot;
    }

    /// <summary>
    /// Distance scale to use for an attacker animation id. A LIGHT attack closes to
    /// <see cref="lightAttackSpotScale"/> of the normal distance; everything else uses the full distance.
    /// </summary>
    public float DistanceScaleForAnimation(string attackerAnimationId)
    {
        if (IsLightAnimation(attackerAnimationId)) return lightAttackSpotScale;
        return 1f;
    }

    /// <summary>True when the given animation id is a LIGHT attack (the ones that close in and retreat).</summary>
    private static bool IsLightAnimation(string attackerAnimationId)
    {
        return !string.IsNullOrEmpty(attackerAnimationId) &&
               attackerAnimationId.ToLowerInvariant().Contains("light");
    }

    /// <summary>
    /// Steps the attacker to the attack spot in front of the victim. Returns a coroutine so callers
    /// (the queue) can yield until the move completes.
    /// </summary>
    /// <param name="attackerSide">Side of the attacker (used to drive its animator Speed while moving).</param>
    /// <param name="distanceScale">
    /// Multiplier on <see cref="attackSpotDistance"/>. A light attack passes
    /// <see cref="lightAttackSpotScale"/> so the attacker stops closer to the victim.
    /// </param>
    public IEnumerator StepAttackerToSpot(PlayerUI.Side attackerSide, Transform attacker, Transform victim,
                                          float distanceScale = 1f)
    {
        if (attacker == null || victim == null)
        {
            AlogWarn("StepAttackerToSpot called with a null attacker/victim.");
            yield break;
        }

        Vector3 spot = ComputeAttackSpot(victim, distanceScale);

        // Drive the run locomotion while stepping, then stop (speed 0) on arrival. This is what makes
        // the model actually RUN to the spot instead of sliding there in idle.
        SetMoveSpeed(attackerSide, moveSpeedValue);

        yield return MoveFighterTo(attackerSide, attacker, spot);

        // Remember where the attacker actually stands so the return-home move can be verified.
        _currentAttackSpot = spot;

        if (verboseLogging)
            Alog($"'{attacker.name}' stepped to attack spot {spot} (Speed=0).");
    }

    /// <summary>
    /// Walks a fighter back to the position it occupied at match start, i.e. restores the original
    /// spacing after a close-in light attack. Returns a coroutine so the queue can wait for it.
    /// No-op when the home position was never captured.
    /// </summary>
    public IEnumerator ReturnFighterHome(PlayerUI.Side side, Transform fighter)
    {
        if (fighter == null || !_homeCaptured) yield break;

        Vector3 home = side == PlayerUI.Side.Left ? _homeLeftPos : _homeRightPos;

        // Keep the fighter on the arena Z line / ground height; only X/Y travel is meaningful here.
        home.z = fighter.position.z;

        if (verboseLogging)
            Alog($"returning '{fighter.name}' home to {home}.");

        SetMoveSpeed(side, moveSpeedValue);
        yield return MoveFighterTo(side, fighter, home);

        if (verboseLogging)
            Alog($"'{fighter.name}' returned home {home} (Speed=0).");
    }

    /// <summary>
    /// Walks BOTH fighters back to their match-start positions AT THE SAME TIME, so the pair stays
    /// symmetric around the fixed <see cref="BattleCenter"/> before the next turn. Returns a coroutine
    /// the queue waits for. No-op when the home positions were never captured.
    /// </summary>
    public IEnumerator ResetBothFightersHome()
    {
        if (!_homeCaptured) yield break;

        Transform left = leftFighter;
        Transform right = rightFighter;
        if (left == null && right == null) yield break;

        Vector3 leftHome = _homeLeftPos;
        Vector3 rightHome = _homeRightPos;

        // Keep each fighter on the arena Z line / ground height; only X travel is meaningful here.
        if (left != null) leftHome.z = left.position.z;
        if (right != null) rightHome.z = right.position.z;

        Vector3 leftStart = left != null ? left.position : Vector3.zero;
        Vector3 rightStart = right != null ? right.position : Vector3.zero;

        if (verboseLogging)
            Alog($"returning BOTH fighters home (left->{leftHome}, right->{rightHome}).");

        // Drive the run locomotion for both while they travel back.
        SetMoveSpeed(PlayerUI.Side.Left, moveSpeedValue);
        SetMoveSpeed(PlayerUI.Side.Right, moveSpeedValue);

        float duration = Mathf.Max(0.01f, moveDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = moveEase.Evaluate(t);

            if (left != null) left.position = Vector3.Lerp(leftStart, leftHome, eased);
            if (right != null) right.position = Vector3.Lerp(rightStart, rightHome, eased);

            yield return null;
        }

        // Land exactly on the home positions.
        if (left != null) left.position = leftHome;
        if (right != null) right.position = rightHome;

        SetMoveSpeed(PlayerUI.Side.Left, 0f);
        SetMoveSpeed(PlayerUI.Side.Right, 0f);

        if (verboseLogging)
            Alog("both fighters returned home (Speed=0).");
    }

    /// <summary>
    /// Eases a fighter to <paramref name="target" /> using the configured move ease, driving the run
    /// locomotion while it travels. Shared by the step-in and the return-home move.
    /// </summary>
    private IEnumerator MoveFighterTo(PlayerUI.Side side, Transform fighter, Vector3 target)
    {
        if (fighter == null) { SetMoveSpeed(side, 0f); yield break; }

        Vector3 startPos = fighter.position;
        float duration = Mathf.Max(0.01f, moveDuration);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = moveEase.Evaluate(t);

            if (fighter == null) { SetMoveSpeed(side, 0f); yield break; } // destroyed mid-step
            fighter.position = Vector3.Lerp(startPos, target, eased);
            yield return null;
        }

        if (fighter == null) { SetMoveSpeed(side, 0f); yield break; }

        // Land exactly on the target so the next attack is pixel-accurate. Y is re-pinned by LateUpdate.
        fighter.position = target;

        // Arrived -> stop running. The animator Speed returns to 0 so the model settles into idle.
        SetMoveSpeed(side, 0f);
    }

    /// <summary>
    /// Drives a side's locomotion "Speed" float through the owner's hook (no-op when the hook or the
    /// feature is disabled). Speed 1 = run while stepping, 0 = stopped on arrival.
    /// </summary>
    private void SetMoveSpeed(PlayerUI.Side side, float value)
    {
        if (!driveMoveSpeed || MoveSpeedHandler == null) return;
        try { MoveSpeedHandler(side, value); }
        catch (System.Exception ex) { AlogWarn($"move-speed handler failed for {side}: {ex}"); }
    }

    /// <summary>
    /// Fires an animation on a side through the playback hook and returns how long to wait for it
    /// (clip length, or the fallback hold when the hook cannot report one). Does NOT wait itself.
    /// </summary>
    private float PlayNow(PlayerUI.Side side, string animationId, float crossFade)
    {
        if (string.IsNullOrEmpty(animationId)) return 0f;

        float clipLength = 0f;
        if (PlayAnimationHandler != null)
        {
            try { clipLength = PlayAnimationHandler(side, animationId, crossFade); }
            catch (System.Exception ex) { AlogWarn($"playback handler failed for '{animationId}': {ex}"); }
        }

        float hold = clipLength > 0f ? clipLength : defaultAnimationHold;
        hold = Mathf.Min(hold, maxActionHold);

        if (verboseLogging)
            Alog($"Fired '{animationId}' on {side}; hold {hold:0.00}s.");

        return hold;
    }

    /// <summary>
    /// Fires BOTH sides of an exchange together (attacker + victim) and yields until the LONGER clip
    /// is done. Both clips start on the same frame so the attack and the matching hit reaction read as
    /// one beat, instead of the hit only starting after the attack has finished.
    /// </summary>
    private IEnumerator PlayPairAndWait(PlayerUI.Side attackerSide, string attackerAnimationId,
                                        PlayerUI.Side victimSide, string victimAnimationId, float crossFade)
    {
        bool hasAttacker = !string.IsNullOrEmpty(attackerAnimationId);
        bool hasVictim = !string.IsNullOrEmpty(victimAnimationId);

        // Fire both on the same frame so the attack and the matching hit read as one beat.
        float aHold = PlayNow(attackerSide, attackerAnimationId, crossFade);
        float vHold = PlayNow(victimSide, victimAnimationId, crossFade);

        // Wait for the PRECISE completion of each side (the clip actually finishing), NOT a guessed
        // duration. Both clips start on the same frame; we advance when BOTH have reported finished.
        //
        // IMPORTANT: the SAFETY CAP is maxActionHold, NOT the reported clip length. The reported length
        // can be stale/wrong (it was read from the Idle state, ~1.0s, because the animator had not yet
        // evaluated the transition when we asked). Using it as the loop bound cut a 6s hit reaction off
        // after one second. The duration is now only a FLOOR here: it stops a one-frame "finished"
        // reading from skipping the clip entirely, but never truncates a genuinely playing clip.
        float reported = Mathf.Max(aHold, vHold);
        float floor = Mathf.Min(Mathf.Max(reported, defaultAnimationHold) * 0.5f, Mathf.Max(0f, maxActionHold));
        float cap = Mathf.Max(0f, maxActionHold);
        if (cap <= 0f) yield break;

        float elapsed = 0f;
        while (elapsed < cap)
        {
            bool attackerDone = IsSideFinished(attackerSide, hasAttacker);
            bool victimDone = IsSideFinished(victimSide, hasVictim);

            // Advance only once BOTH sides report finished AND we are past the small floor, so a stale
            // "finished" on the very first frame cannot skip the clip (IsSideFinished returns true when
            // nothing is being watched).
            bool attackerSettled = !hasAttacker || attackerDone;
            bool victimSettled = !hasVictim || victimDone;
            if (attackerSettled && victimSettled && elapsed >= floor) break;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    /// <summary>
    /// True when a side that was asked to animate has finished. Prefers the AUTHORITATIVE
    /// AnimationEndAction signal (a real event from the clip) and only falls back to polling the
    /// animator when that hook is not wired.
    /// </summary>
    private bool IsSideFinished(PlayerUI.Side side, bool wasAnimated)
    {
        if (!wasAnimated) return true;

        // AUTHORITATIVE: the AnimationEndAction behaviour on the state reported it exiting. This is the
        // actual end of the clip -- no timing guess involved.
        if (SideAnimationEndSignalledHandler != null)
        {
            try { if (SideAnimationEndSignalledHandler(side)) return true; }
            catch { /* fall through to polling */ }
        }

        if (AnimationFinishedHandler == null) return true; // no precise source -> rely on the duration cap
        try { return AnimationFinishedHandler(side); }
        catch { return true; }
    }

    /// <summary>
    /// Owner-supplied hook reporting whether the side's AnimationEndAction has fired for the state that
    /// is being waited on. This is the precise "clip is over" edge; when wired, it takes priority over
    /// every timing-based fallback.
    /// </summary>
    public System.Func<PlayerUI.Side, bool> SideAnimationEndSignalledHandler { get; set; }

    /// <summary>
    /// Waits for a clip that was chained onto this item (e.g. the getup) to actually play through before
    /// the queue advances, so the next queued turn never cuts it off. Waits for the side's real clip-end
    /// via the finished hook when available, with <paramref name="maxWait"/> as the safety upper bound.
    /// Returns immediately when no finished hook is wired.
    /// </summary>
    private IEnumerator WaitForChainedClip(PlayerUI.Side side, float maxWait)
    {
        if (AnimationFinishedHandler == null) yield break;

        float cap = Mathf.Min(Mathf.Max(0.05f, maxWait), Mathf.Max(0f, maxActionHold));
        float elapsed = 0f;

        // First give the animator a couple of frames to ENTER the chained clip (otherwise the
        // finished-hook may still report the previous clip as done and we would advance instantly).
        yield return null;
        yield return null;

        while (elapsed < cap)
        {
            bool done;
            try { done = AnimationFinishedHandler(side); }
            catch { done = true; }
            if (done) yield break;

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }

    // ------------------------------------------------------------------
    // Queue API
    //
    // GameManager drives the whole match through this single queue. Every event becomes ONE
    // QueuedAction carrying whatever work that event needs (apply / move / animate / wait).
    // ------------------------------------------------------------------

    /// <summary>
    /// Enqueues a general work item. Use this for ANY gameplay event so its UI/state application and
    /// its animation serialize behind everything already queued.
    /// </summary>
    /// <param name="label">Human-readable tag for logs.</param>
    /// <param name="apply">Synchronous work to run when the item starts (may be null).</param>
    /// <param name="isFinished">Optional precise completion predicate; the queue advances the moment it returns true.</param>
    /// <param name="maxHold">Safety upper bound (seconds) the item may hold the queue.</param>
    public void EnqueueWork(string label, System.Action apply, System.Func<bool> isFinished, float maxHold)
    {
        EnqueueWork(label, apply, isFinished, maxHold, null);
    }

    /// <summary>
    /// Turn-aware overload of <see cref="EnqueueWork"/>: records the item's backend turn so the
    /// per-turn barrier can keep turns from interleaving and can reject stale re-deliveries.
    /// </summary>
    public void EnqueueWork(string label, System.Action apply, System.Func<bool> isFinished, float maxHold, string turnId)
    {
        _queue.Enqueue(new QueuedAction
        {
            label = label,
            apply = apply,
            moveAttacker = false,
            playAnimation = false,
            maxHold = maxHold,
            isFinished = isFinished,
            turnId = turnId,
        });

        if (verboseLogging)
            Alog($"Queued work '{label}'{(string.IsNullOrEmpty(turnId) ? "" : $" [turn={turnId}]")}. Pending={_queue.Count}");

        EnsureQueueRunning();
    }

    /// <summary>
    /// Enqueues the full combat exchange: run <paramref name="apply"/> (GameManager applies the
    /// event: UI, hit routing, ...), THEN step the attacker to the spot in front of the victim,
    /// THEN play the animation pair. One item, one beat, no overlap with anything else queued.
    /// </summary>
    /// <param name="label">Human-readable tag for logs.</param>
    /// <param name="apply">Event application run before the move (may be null).</param>
    /// <param name="attackerSide">The side that attacks (steps forward).</param>
    /// <param name="victimSide">The side that reacts (is stepped toward).</param>
    /// <param name="attackerAnimationId">Animation id for the attacker (e.g. "attack2").</param>
    /// <param name="victimAnimationId">Animation id for the victim (e.g. "hit2"). May be empty.</param>
    /// <param name="isFinished">Optional precise predicate for the whole exchange.</param>
    /// <param name="maxHold">Safety upper bound (seconds) the item may hold the queue.</param>
    public void EnqueueMoveThenAnimate(string label, System.Action apply,
                                       PlayerUI.Side attackerSide, PlayerUI.Side victimSide,
                                       string attackerAnimationId, string victimAnimationId,
                                       System.Func<bool> isFinished, float maxHold)
    {
        EnqueueMoveThenAnimate(label, apply, attackerSide, victimSide,
                               attackerAnimationId, victimAnimationId, isFinished, maxHold,
                               null, 0f, 0f, null);
    }

    /// <summary>
    /// Full overload: enqueue the exchange AND an optional <paramref name="afterAnimation"/> action that
    /// runs after the attack+hit pair of THIS item finishes (still inside the same queue step). Used to
    /// chain a getup onto a knockout exchange so the victim stands back up before the next queued turn
    /// plays. <paramref name="afterAnimationHold"/> is the minimum hold before the chained action (let
    /// the hit clip finish); <paramref name="afterAnimationSettle"/> is how long to wait for the chained
    /// clip itself to play through. <paramref name="turnId"/> records the backend turn for the per-turn
    /// barrier (see the fields on <see cref="QueuedAction"/>).
    /// </summary>
    public void EnqueueMoveThenAnimate(string label, System.Action apply,
                                       PlayerUI.Side attackerSide, PlayerUI.Side victimSide,
                                       string attackerAnimationId, string victimAnimationId,
                                       System.Func<bool> isFinished, float maxHold,
                                       System.Action afterAnimation, float afterAnimationHold,
                                       float afterAnimationSettle)
    {
        EnqueueMoveThenAnimate(label, apply, attackerSide, victimSide,
                               attackerAnimationId, victimAnimationId, isFinished, maxHold,
                               afterAnimation, afterAnimationHold, afterAnimationSettle, null);
    }

    /// <summary>Turn-aware overload of the full exchange enqueue.</summary>
    public void EnqueueMoveThenAnimate(string label, System.Action apply,
                                       PlayerUI.Side attackerSide, PlayerUI.Side victimSide,
                                       string attackerAnimationId, string victimAnimationId,
                                       System.Func<bool> isFinished, float maxHold,
                                       System.Action afterAnimation, float afterAnimationHold,
                                       float afterAnimationSettle, string turnId)
    {
        _queue.Enqueue(new QueuedAction
        {
            label = label,
            apply = apply,
            moveAttacker = true,
            attackerSide = attackerSide,
            victimSide = victimSide,
            attackerAnimationId = attackerAnimationId,
            victimAnimationId = victimAnimationId,
            crossFade = defaultCrossFade,
            playAnimation = true,
            maxHold = maxHold,
            isFinished = isFinished,
            afterAnimation = afterAnimation,
            afterAnimationHold = afterAnimationHold,
            afterAnimationSettle = afterAnimationSettle,
            turnId = turnId,
        });

        if (verboseLogging)
            Alog($"Queued move+anim '{label}': {attackerSide} '{attackerAnimationId}' -> {victimSide} '{victimAnimationId}'" +
                 (afterAnimation != null ? " (+post-animation getup)" : "") +
                 (string.IsNullOrEmpty(turnId) ? "" : $" [turn={turnId}]") + $". Pending={_queue.Count}");

        EnsureQueueRunning();
    }

    /// <summary>
    /// Back-compat convenience overload: enqueue a pure move+animate exchange with no apply step.
    /// </summary>
    public void EnqueueMoveThenAnimate(PlayerUI.Side attackerSide, PlayerUI.Side victimSide,
                                       string attackerAnimationId, string victimAnimationId)
    {
        EnqueueMoveThenAnimate($"move+anim {attackerSide}->{victimSide}", null,
                               attackerSide, victimSide, attackerAnimationId, victimAnimationId,
                               null, maxActionHold);
    }

    /// <summary>
    /// Enqueues an animation-only step (no move). Used for reactions that play in place
    /// (e.g. a getup) so they still serialize behind any in-flight action.
    /// </summary>
    public void EnqueueAnimate(PlayerUI.Side side, string animationId)
    {
        _queue.Enqueue(new QueuedAction
        {
            label = $"anim {side} '{animationId}'",
            attackerSide = side,
            victimSide = Opposite(side),
            attackerAnimationId = animationId,
            victimAnimationId = null,
            crossFade = defaultCrossFade,
            moveAttacker = false,
            playAnimation = true,
            maxHold = maxActionHold,
            isFinished = null,
        });

        if (verboseLogging)
            Alog($"Queued anim: {side} '{animationId}'. Pending={_queue.Count}");

        EnsureQueueRunning();
    }

    /// <summary>
    /// Applies the master <see cref="spacingScale"/> to a configured gap. Used by every spacing pause so
    /// the whole sequence can be breathed in/out from ONE Inspector value.
    /// </summary>
    private float ScaleSpacing(float seconds)
    {
        return Mathf.Max(0f, seconds) * Mathf.Max(0f, spacingScale);
    }

    /// <summary>Drops everything still waiting. The in-flight action finishes normally.</summary>
    public void ClearQueue()
    {
        _queue.Clear();

        // Dropping the queue can leave an attack shot held (its "finished" callback will never run),
        // which would strand the camera leaning toward whoever attacked last. Release it here too.
        try { AttackFinishedHandler?.Invoke(); }
        catch (System.Exception ex) { AlogWarn($"attack-finished handler threw while clearing the queue: {ex}"); }

        // The turn barrier tracks turn progress for THIS match. Dropping the queue ends that progress, so
        // clear it too -- otherwise a turn id reused by a fresh match/reconnect could be seen as "already
        // completed" and its items silently dropped.
        ResetTurnBarrier();
    }

    /// <summary>
    /// Clears the per-turn barrier state (turn order, running turn, last completed turn). Called when the
    /// queue is dropped and at match start so a new match never inherits the previous one's turn history.
    /// </summary>
    public void ResetTurnBarrier()
    {
        _runningTurnId = null;
        _lastCompletedTurnId = null;
        _turnOrder.Clear();
        _turnOrderCounter = 0;
    }

    /// <summary>Human-readable snapshot of the per-turn barrier state (for logs/diagnostics).</summary>
    public string TurnBarrierSummary =>
        $"running={(string.IsNullOrEmpty(_runningTurnId) ? "-" : _runningTurnId)}, " +
        $"lastDone={(string.IsNullOrEmpty(_lastCompletedTurnId) ? "-" : _lastCompletedTurnId)}, " +
        $"pending={_queue.Count}";

    /// <summary>
    /// The single serial drain loop. For each item: APPLY -> MOVE -> ANIMATE -> (wait). Because exactly
    /// one coroutine owns this loop, an action can never start before the previous one has completed,
    /// and there is no second scheduler anywhere that could race it.
    /// </summary>
    private IEnumerator RunQueue()
    {
        while (_queue.Count > 0)
        {
            var action = _queue.Dequeue();
            _isRunningAction = true;

            // ---- PER-TURN BARRIER (dequeue-time) --------------------------------------------------
            // Reject a STALE item: one whose turn has already finished. This is the backstop against an
            // out-of-order / re-delivered event slipping in after its turn was closed and replaying an
            // attack, a hit or an HP write that no longer belongs to the current beat.
            string rejectReason = GetTurnOrderGuardRejectReason(action.turnId);
            if (!string.IsNullOrEmpty(rejectReason))
            {
                AlogWarn($"[queue] dropping stale item '{action.label}' [turn={action.turnId}] -> {rejectReason}");
                _isRunningAction = false;
                continue;
            }

            // Note the turn this item belongs to. The queue is single-threaded and every item holds the
            // queue until its exchange is fully settled, so by the time we get here the PREVIOUS turn is
            // necessarily drained -- this assignment simply makes the boundary explicit for logging and
            // for the completion bookkeeping at the end of the item.
            string previousRunningTurn = _runningTurnId;
            if (!string.IsNullOrEmpty(previousRunningTurn) &&
                !string.Equals(previousRunningTurn, action.turnId, System.StringComparison.Ordinal))
            {
                // The turn we were running has now genuinely finished: we are moving on to a different
                // turn (or to an untagged item). Close it so a later re-delivery of ANY of its events is
                // rejected as stale. A turn is closed here -- NOT after every item -- because several
                // items (ARGUMENT_SELECTED, DAMAGE_APPLIED, HP_CHANGED) share one turnId and must all run.
                _lastCompletedTurnId = previousRunningTurn;
                if (verboseLogging)
                    Alog($"[queue] turn '{previousRunningTurn}' completed (" + TurnBarrierSummary + $")");
            }

            _runningTurnId = action.turnId;
            if (!string.IsNullOrEmpty(action.turnId) &&
                !string.Equals(previousRunningTurn, action.turnId, System.StringComparison.Ordinal))
            {
                if (!_turnOrder.ContainsKey(action.turnId)) _turnOrder[action.turnId] = ++_turnOrderCounter;
                if (verboseLogging)
                    Alog($"[queue] turn boundary -> starting turn '{action.turnId}' (" +
                         $"previous='{(string.IsNullOrEmpty(previousRunningTurn) ? "-" : previousRunningTurn)}', " +
                         $"lastDone='{(string.IsNullOrEmpty(_lastCompletedTurnId) ? "-" : _lastCompletedTurnId)}'), " +
                         $"pending={_queue.Count}");
            }

            if (verboseLogging)
                Alog($"[queue] run '{action.label}'" +
                     (string.IsNullOrEmpty(action.turnId) ? "" : $" [turn={action.turnId}]") +
                     $" (pending left {_queue.Count})");

            // 0) APPLY: GameManager's event application (UI/state/hit routing) runs first so the
            //    move and animation below act on the freshly-applied state.
            if (action.apply != null)
            {
                try { action.apply(); }
                catch (System.Exception ex) { AlogError($"action '{action.label}' apply threw: {ex}"); }
            }

            // 1) MOVE the attacker to the spot in front of the victim (if this item moves anyone).
            if (action.moveAttacker)
            {
                Transform attacker = GetFighter(action.attackerSide);
                Transform victim = GetFighter(action.victimSide);
                if (attacker != null && victim != null)
                {
                    // A LIGHT attack closes to a fraction of the normal distance so the two fighters end
                    // up closer together before the swing; heavy attacks keep the full distance.
                    float distanceScale = DistanceScaleForAnimation(action.attackerAnimationId);
                    yield return StepAttackerToSpot(action.attackerSide, attacker, victim, distanceScale);
                }

                // Settle before firing the attack. The shipped controller authors attack/hit/getup
                // transitions ONLY from the Idle state, so firing while the model is still in Walk
                // (Speed just driven to 0) swallows the trigger. Wait the settle delay, then poll the
                // animator until it reports it has left the locomotion blend (with a safety timeout).
                if (driveMoveSpeed)
                {
                    if (moveSettleDelay > 0f) yield return new WaitForSeconds(moveSettleDelay);
                    else yield return null;

                    float waited = 0f;
                    while (LocomotionSettledHandler != null &&
                           !LocomotionSettledHandler(action.attackerSide) &&
                           waited < maxMoveSettleWait)
                    {
                        waited += Time.deltaTime;
                        yield return null;
                    }
                }
            }

            // 2) ANIMATE the pair TOGETHER, then wait for the longer clip to finish.
            if (action.playAnimation)
            {
                // SPACING: let the step-in fully settle before the clips fire, so the attack is never
                // triggered mid-blend (which can swallow the trigger or start the clip from a snap pose).
                float preGap = ScaleSpacing(preExchangeSpacing);
                if (preGap > 0f) yield return new WaitForSeconds(preGap);

                // The clips are about to start: this is the moment the health bar must drop, so the
                // damage lands exactly with the swing (not during the step-in, not after the clip).
                try { AnimationStartingHandler?.Invoke(); }
                catch (System.Exception ex) { AlogWarn($"animation-start handler threw: {ex}"); }

                yield return PlayPairAndWait(action.attackerSide, action.attackerAnimationId,
                                             action.victimSide, action.victimAnimationId, action.crossFade);

                // SPACING: a breath between this exchange and whatever comes next, so two clips never
                // read as one continuous motion.
                float postGap = ScaleSpacing(betweenActionsSpacing);
                if (postGap > 0f) yield return new WaitForSeconds(postGap);
            }

            // 2b) POST-ANIMATION CHAIN: e.g. a getup that must run right after a knockout exchange.
            //     Doing it HERE (instead of in a later DAMAGE_APPLIED item) guarantees the victim is
            //     back on its feet before the next queued turn can fire, which is what stops the
            //     "attack plays while the fighter is still down" problem.
            if (action.afterAnimation != null)
            {
                // HOLD until the VICTIM's hit reaction clip has genuinely finished before the chained
                // action (the getup) runs. We require the victim's clip to be OBSERVED as finished at
                // least once (not merely "not animating yet"), and we additionally enforce a minimum
                // hold of afterAnimationHold so a stubbed/instant 'finished' report can never let the
                // getup cut the hit short.
                float minHold = Mathf.Max(0f, action.afterAnimationHold);
                // Upper bound for the hold. This is the CLIP bound, NOT the generic 15s queue safety:
                // using the queue safety here meant a victim who never reported "finished" (e.g. a
                // looping death pose) stalled the queue for a full 15s. A hit reaction is at most a few
                // seconds, so cap it tightly; the queue's own maxActionHold still guards the item overall.
                float cap = minHold + maxHoldOvershoot;

                float afterElapsed = 0f;
                bool victimWasAnimated = !string.IsNullOrEmpty(action.victimAnimationId);
                // Phase 1: wait out the minimum hold AND the victim's hit clip (whichever is longer).
                // IsSideActionPlaying is the NON-MUTATING query, so polling it here does not consume the
                // watch state -- the hit clip is genuinely waited out, not assumed finished.
                while (afterElapsed < cap)
                {
                    // LETHAL only: a dead victim parks in a looping KO pose that never reports clip
                    // finished, so waiting on the clip would run the hold to its cap. A SURVIVOR must not
                    // be cut short -- the hit clip has to play through before the getup fires.
                    if (IsSideDead(action.victimSide)) break;

                    bool victimStillPlaying = victimWasAnimated && IsSideActionPlaying(action.victimSide);
                    if (!victimStillPlaying && afterElapsed >= minHold) break;

                    // Early-out: once the hit is done and both fighters are visibly idle+settled there is
                    // no reason to sit out the rest of the hold -- fire the chained getup immediately.
                    if (afterElapsed >= minHold && BothFightersIdleSettled()) break;

                    afterElapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (verboseLogging)
                    Alog($"running post-animation action after '{action.label}' (held {afterElapsed:0.00}s, minHold {minHold:0.00}s).");
                try { action.afterAnimation(); }
                catch (System.Exception ex) { AlogError($"post-animation action for '{action.label}' threw: {ex}"); }

                // Phase 2: let the chained clip (getup) actually play out before the queue advances.
                yield return WaitForChainedClip(action.victimSide, action.afterAnimationSettle);
            }

            // 2c) IDLE FAST-PATH: if both fighters are already standing idle and settled (the exchange
            //     is visibly over), skip the remaining waits and let the next queued stack run right
            //     away. This is what removes the long pause between turns.
            //
            //     DISABLED while alwaysWaitFullClipLength is on: advancing the moment the animator looks
            //     idle was cutting clip tails off. Stability first -- spacing gets tightened later.
            if (!alwaysWaitFullClipLength && BothFightersIdleSettled())
            {
                if (verboseLogging)
                    Alog($"both fighters idle+settled after '{action.label}' -> advancing immediately.");
                goto postAnimationCorrection;
            }

            // 3) WAIT for the precise predicate OR the safety hold.
            //
            // IMPORTANT: when a precise predicate IS supplied, it is AUTHORITATIVE -- the duration is
            // only a safety UPPER BOUND, never a shorter early-out. Previously the loop exited at
            // `hold` even while the predicate still reported "not finished", and because the estimated
            // duration was wrong (~1.0s, read from the Idle state instead of the heavy clip) a 5-6s hit
            // animation was cut off roughly one second in. That is exactly the "animation did not play
            // to the end" behaviour.
            float safetyCap = Mathf.Max(0f, maxActionHold);
            if (action.isFinished == null)
            {
                // No precise source: fall back to the estimated hold.
                float hold = Mathf.Min(Mathf.Max(0f, action.maxHold), safetyCap);
                float waited = 0f;
                while (waited < hold)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }
            else
            {
                // Precise completion drives the advance; the cap only guards against a stuck predicate.
                // Give it a small floor so a one-frame "finished" reading cannot skip the clip entirely.
                float floor = Mathf.Min(Mathf.Max(0f, action.maxHold), safetyCap);
                float elapsed = 0f;
                while (elapsed < safetyCap)
                {
                    bool done;
                    try { done = action.isFinished(); }
                    catch { done = true; }
                    if (done && elapsed >= floor) break;

                    // Idle fast-path: nothing is animating any more -> do not sit out the floor/cap.
                    if (elapsed >= floor && BothFightersIdleSettled()) break;

                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

        postAnimationCorrection:

            // 4) POST-ANIM CORRECTION: everything that repositions/reorients the fighters happens
            //    ONLY here, after the clip(s) have finished. Nothing is corrected while a clip plays.
            //
            //    IMPORTANT: this runs for EVERY item, not just move+animate ones. A DAMAGE_APPLIED hit
            //    reaction or a getup plays on the bridges through the apply step (not the controller's
            //    animation step), so if we only corrected when action.playAnimation was true, the hit
            //    clip could lift/offset a fighter's Y and it would never be brought back down. Running
            //    the correction unconditionally guarantees Y returns to the captured ground after any
            //    clip that just played.
            {
                Transform left = GetFighter(PlayerUI.Side.Left);
                Transform right = GetFighter(PlayerUI.Side.Right);

                // Facing: re-aim each fighter at the other (only matters after a clip that rotated it).
                ApplyFacingAfterAnim(left, right);
                ApplyFacingAfterAnim(right, left);

                // Ground: ease each fighter's Y back to its captured height. Skipped entirely when the
                // fighter is already grounded, so an idle fighter does not pay the ease duration.
                if (resetGroundYAfterAnim)
                {
                    if (left != null && !IsAtGroundY(left, GroundYForSide(PlayerUI.Side.Left)))
                        yield return SnapYToGround(left, GroundYForSide(PlayerUI.Side.Left));
                    if (right != null && !IsAtGroundY(right, GroundYForSide(PlayerUI.Side.Right)))
                        yield return SnapYToGround(right, GroundYForSide(PlayerUI.Side.Right));
                }

                if (verboseLogging)
                    Alog($"Post-anim correction done after '{action.label}' (Y regrounded).");
            }

            // 4b) RESET TO HOME: after any exchange that MOVED someone, walk BOTH fighters back to the
            //     positions they held at match start. That restores the original spacing and keeps them
            //     symmetric around the fixed <see cref="BattleCenter"/> before the next turn begins --
            //     instead of leaving whoever closed in (a light attack) bunched up against the opponent.
            //
            //     WAIT FIRST: the walk-home must NOT start while a fighter is still mid-animation. With a
            //     heavy hit the victim is knocked down and gets up AFTER the attacker's clip ended, so
            //     walking home straight away dragged a still-down fighter across the stage. We therefore
            //     wait until BOTH fighters are genuinely back in Idle, then walk home.
            if (action.playAnimation && action.moveAttacker && returnHomeAfterExchange)
            {
                bool anyDead = IsSideDead(PlayerUI.Side.Left) || IsSideDead(PlayerUI.Side.Right);

                if (!anyDead)
                {
                    float settleForHome = 0f;
                    float settleForHomeCap = Mathf.Max(0f, maxHoldOvershoot + postAnimationSettleDelay);
                    while (settleForHome < settleForHomeCap)
                    {
                        if (BothFightersIdleSettled()) break;

                        settleForHome += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    if (verboseLogging)
                        Alog($"waiting {settleForHome:0.00}s for both fighters to settle before walking home after '{action.label}'.");

                    yield return ResetBothFightersHome();

                    // SPACING: let the walk-home settle before the next turn's step-in begins, so the two
                    // moves do not read as one continuous run.
                    float homeGap = ScaleSpacing(afterReturnHomeSpacing);
                    if (homeGap > 0f) yield return new WaitForSeconds(homeGap);
                }
                else if (verboseLogging)
                {
                    Alog($"walk-home SKIPPED after '{action.label}': a fighter is dead (no reposition of the body).");
                }
            }

            // 4c) END THE ATTACK SHOT: release the camera back to the midpoint between both fighters.
            //
            //     RULES (both were wrong before, which is why the camera kept snapping to centre mid-
            //     animation):
            //       * A DEAD fighter keeps the camera where it is. Releasing on death yanked the view to
            //         the centre while the body was still collapsing.
            //       * Otherwise the shot is held until BOTH fighters are genuinely back in Idle -- so a
            //         hit reaction / getup runs to completion first, and only then does the camera glide
            //         back to the middle.
            if (action.playAnimation)
            {
                bool anyDead = IsSideDead(PlayerUI.Side.Left) || IsSideDead(PlayerUI.Side.Right);

                if (!anyDead)
                {
                    // Cap at the CLIP bound (not the generic 15s queue safety): a fighter that never
                    // reports idle must not park the camera forever.
                    float focusReleaseWaited = 0f;
                    float focusReleaseCap = Mathf.Max(0f, maxHoldOvershoot + postAnimationSettleDelay);
                    while (focusReleaseWaited < focusReleaseCap)
                    {
                        if (BothFightersIdleSettled()) break;

                        focusReleaseWaited += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    if (verboseLogging)
                        Alog($"releasing camera attack shot after '{action.label}' (waited {focusReleaseWaited:0.00}s for both fighters to settle).");

                    try { AttackFinishedHandler?.Invoke(); }
                    catch (System.Exception ex) { AlogWarn($"attack-finished handler threw: {ex}"); }
                }
                else if (verboseLogging)
                {
                    Alog($"camera attack shot KEPT after '{action.label}': a fighter is dead (no recentre on death).");
                }
            }

            // Settle pause before the next item runs. This is now applied UNCONDITIONALLY (scaled by the
            // master spacing) whenever a clip played: the old code skipped it exactly when both fighters
            // were idle -- i.e. after every normal exchange -- which is why animations ran back-to-back
            // with no breathing room. Stability first; tighten spacingScale later once the flow is right.
            if (action.playAnimation)
            {
                float settle = ScaleSpacing(postAnimationSettleDelay + betweenActionsSpacing);
                if (settle > 0f) yield return new WaitForSeconds(settle);
            }
            else
            {
                float idleSettle = ScaleSpacing(postAnimationSettleDelay);
                if (idleSettle > 0f && !BothFightersIdleSettled()) yield return new WaitForSeconds(idleSettle);
            }

            // ---- TURN COMPLETION (on drain) ------------------------------------------------------
            // If this was the LAST item, close the turn it belonged to now so the barrier above rejects
            // any re-delivery of its events afterwards. When more items remain they will close it at
            // their own turn boundary above (several items can share one turnId).
            if (_queue.Count == 0 && !string.IsNullOrEmpty(_runningTurnId))
            {
                _lastCompletedTurnId = _runningTurnId;
                if (verboseLogging)
                    Alog($"[queue] turn '{_runningTurnId}' completed on drain (" + TurnBarrierSummary + $")");
            }
            _runningTurnId = null;

            _isRunningAction = false;
        }

        _queueRunner = null;
    }

    /// <summary>
    /// Decides whether a queued item must be DROPPED because of its turn (the per-turn barrier).
    /// Returns null when the item may run, otherwise a human-readable reason.
    ///
    /// Rules:
    ///   * An item with no turnId always runs (it is not part of the turn barrier, e.g. a getup item).
    ///   * An item whose turn already COMPLETED is stale (a re-delivery / out-of-order replay) and is
    ///     dropped -- this is what stops a turn's attack/hit/HP from firing a second time after its turn
    ///     has been closed.
    ///   * An item whose turn is the SAME as the one still running is fine (same turn, more items).
    ///   * An item of a LATER turn is fine: the queue is single-threaded and the running item holds the
    ///     queue until its turn is fully drained, so the previous turn cannot still be mid-flight here.
    /// </summary>
    private string GetTurnOrderGuardRejectReason(string turnId)
    {
        if (string.IsNullOrEmpty(turnId)) return null;

        // Already completed -> stale replay.
        if (!string.IsNullOrEmpty(_lastCompletedTurnId) &&
            string.Equals(turnId, _lastCompletedTurnId, System.StringComparison.Ordinal))
        {
            return $"turn '{turnId}' already completed";
        }

        // Older-than-last-completed -> stale (turns are compared by ARRIVAL order, since a backend turnId
        // is an opaque string). A turn we already saw and closed must never run again.
        if (_turnOrder.TryGetValue(turnId, out var thisOrder) &&
            !string.IsNullOrEmpty(_lastCompletedTurnId) &&
            _turnOrder.TryGetValue(_lastCompletedTurnId, out var lastOrder) &&
            thisOrder < lastOrder)
        {
            return $"turn '{turnId}' is older than completed turn '{_lastCompletedTurnId}'";
        }

        return null;
    }
}
