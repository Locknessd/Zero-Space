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
/// - Keep a MINIMUM GAP between the two fighters and keep the pair symmetric around the fixed battle
///   centre (RepositionToFightStance), WITHOUT teleporting anyone through the other.
/// - Own and run the single event queue that serializes event application, moves and playback.
///
/// RUNTIME LOCKS (enforced every frame in LateUpdate, after all animation writes):
///   - Z: pinned to the captured fighting line, so a clip can never drift the model forward/backward.
///   - Rotation: pinned to the captured facing (yaw), so a clip can never spin/tilt the model while
///     it plays. The facing correction between turns updates the locked yaw (ApplyFacingAfterAnim).
/// X is left untouched (step-move axis). Y is NEVER written while a clip plays (jump / flying clips must
/// be free to lift the model); the model is put back on its captured ground height ONCE the clip has
/// finished, in the post-animation correction (SnapYToGround).
///
/// STANCE (never cross through each other):
/// Attack spots and walk-in steps can never drag a fighter PAST its opponent, and a step that would
/// cross the other model is automatically re-routed onto the fighter's OWN side of the arena. Because
/// a step is an interpolated move over several frames, the two models can never swap places or pass
/// through one another.
///
/// POST-ANIM CORRECTION (computed ONCE, after an animation finishes -> queue step 4):
///   - Facing: yaw is turned toward the opponent, pitch/roll cleared (ApplyFacingAfterAnim).
///   - Ground: Y is eased back to the captured height (SnapYToGround), because airborne clips must be
///     free to lift the model while they play and only get grounded once they end.
///   - Stance: the pair ONLY gaps out when they overlap. When they are apart, both fighters keep the
///     side and the ground they ended the animation on -- nobody is dragged back through the other
///     one to a stored home position.
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
    [Tooltip("Bring each fighter's Y back to its captured ground height once a clip has FINISHED (queue step 4). While a clip plays Y is never touched, so a jump / flying clip can lift the model freely; this is what puts it back on the floor afterwards.")]
    [SerializeField] private bool resetGroundYAfterAnim = true;
    [Tooltip("Seconds to ease a fighter's Y back down to the ground after a clip ends. 0 = snap instantly.")]
    [SerializeField] private float groundResetDuration = 0.15f;
    [Tooltip("Lock each fighter's rotation every frame (at runtime) while a clip plays, so an animation can never spin/tilt the model off its captured facing.")]
    [SerializeField] private bool lockRotationRuntime = true;
    [Tooltip("Force both fighters to face EACH OTHER (yaw only, pitch/roll cleared) when correcting between turns so clips cannot leave a model facing the wrong way.")]
    [SerializeField] private bool faceEachOther = true;

    [Header("Attack Spot")]
    [Tooltip("HEAVY attack spot distance (world units from the enemy). The attacker stands this far from the victim for a heavy swing.\n\nTHIS IS ALSO THE RESTING DISTANCE: after every exchange both fighters are moved back out ONCE, to the HEAVY distance, so the pair always resets to this same spacing. Configure it LARGER than the light distance below -- that difference is what makes a light attack take an extra short step IN before it swings.")]
    [Range(0.1f, 6f)]
    [SerializeField] private float attackSpotDistanceHeavy = DefaultAttackSpotDistanceHeavy;
    [Tooltip("LIGHT attack spot distance (world units from the enemy). The attacker stands this far from the victim for a light swing -- closer than the heavy distance.\n\nTHE STEP-IN: because the pair rests at attackSpotDistanceHeavy, a light attack starts the exchange FARTHER out than its own spot, so the attacker walks the difference in (heavy minus light) before the clip fires. That walk is the only extra movement a light attack makes; the return is the single re-spacing move shared by both weights.")]
    [Range(0.1f, 6f)]
    [SerializeField] private float attackSpotDistanceLight = DefaultAttackSpotDistanceLight;
    [Tooltip("DEPRECATED / kept only so old scenes load. It is no longer used for anything: the spot distance now comes straight from attackSpotDistanceHeavy / attackSpotDistanceLight. Overwritten on Awake.")]
    [Range(0.05f, 1f)]
    [SerializeField] private float lightAttackSpotScale = DefaultLightAttackSpotScale;
    [Tooltip("MOVE-IN ONLY FOR THE SWING.\n\nOFF (default): the attacker NEVER steps OUT to a wider spot to attack. It only closes in when it is farther than the attack spot (e.g. after a re-spacing), and once the clip finishes the queue restores the standard gap EXACTLY ONCE. This is what stops the old 'pushed far out, then pulled back in' two-step.\n\nON: legacy behaviour -- the attacker always walks to the computed spot, even when that spot is FARTHER away than where it already stands.")]
    [SerializeField] private bool stepOutToAttackSpot = false;
    [Tooltip("The attack spot is measured FROM THE BATTLE CENTRE (a point on the fighting line), per SIDE, instead of along the victim's forward axis. This is what makes a fighter that already ran round the back of its opponent step BACK to its own side instead of staying behind it. Turn OFF to restore the old victim-forward behaviour.")]
    [SerializeField] private bool attackSpotsRelativeToCentre = true;
    [Tooltip("Side-lane offset (X only) applied per SIDE, so each fighter keeps a lane of its own: the LEFT fighter aims at (centre - offset) and the RIGHT fighter at (centre + offset). This is what stops a fighter from ending up on the wrong side after an attack, and it makes the walk-back to a strayed fighter readable instead of a slide through its opponent. 0 = both aim exactly at the centre.")]
    [Range(0f, 2f)]
    [SerializeField] private float sideLaneOffset = 0.8f;
    [Tooltip("The STANDARD gap the two fighters are held at between exchanges (world units). After every exchange BOTH fighters are moved back out exactly ONCE, to the distance below.\n\nNOTE: this value is IGNORED at runtime -- the standard gap is always attackSpotDistanceHeavy, so the resting spacing and the heavy attack spot are the same number by construction. Kept only so old scenes load.")]
    [Range(0.2f, 6f)]
    [SerializeField] private float desiredFighterDistance = 2.2f;
    [Tooltip("Hard floor (world units) between the two fighters. Steps and attack spots that would break it are clamped, so the models can NEVER overlap or cross. Should be smaller than BOTH attack spot distances.")]
    [Range(0f, 3f)]
    [SerializeField] private float minFighterDistance = 0.7f;
    [Tooltip("Extra world-space offset applied on top of the computed spot (X = lateral nudge, Y = height, Z = depth). Usually left at zero.")]
    [SerializeField] private Vector3 attackSpotOffset = Vector3.zero;

    [Header("Movement")]
    [Tooltip("Seconds the attacker takes to step to the attack spot.")]
    [SerializeField] private float moveDuration = 0.35f;
    [Tooltip("Seconds the post-animation return to the standard gap takes. Kept short on purpose: this is the ONE reposition that runs after the clip finishes, so it should read as a settle rather than a second walk.")]
    [SerializeField] private float respaceDuration = 0.2f;
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
    [SerializeField] private float preExchangeSpacing = 0.1f;
    [Tooltip("Extra pause added AFTER a clip finishes and before the next queued item starts. Keeps consecutive animations from blending into each other.")]
    [SerializeField] private float betweenActionsSpacing = 0.15f;
    [Tooltip("Only used when the walk-back on match start is enabled: seconds the fighters take to walk to their starting stance. Also the duration of the automatic re-space after each exchange.")]
    [SerializeField] private float stanceReturnDuration = 0.3f;
    [Tooltip("When true, the two fighters are moved back out to the standard gap (attackSpotDistanceHeavy, the heavy attack spot) after EVERY exchange. This is what maintains a consistent spacing between the bots. Turn OFF to only ever gap them out when they physically overlap.")]
    [SerializeField] private bool enforceFighterSpacing = true;
    [Tooltip("When true, the fighters walk back to their WAITING STANCE (the configured side lanes) at the START of a match only, so both sides are placed symmetrically once. Leave this OFF if the scene already places the fighters where they should stand -- otherwise they visibly shift right on match start.")]
    [SerializeField] private bool resetStanceOnMatchStart = false;
    [Tooltip("When true, the queue ALWAYS waits the full estimated clip length instead of advancing as soon as the animator reports the clip done. Slower but the most stable (no early cut-offs).")]
    [SerializeField] private bool alwaysWaitFullClipLength = true;

    [Header("Queue")]
    [Tooltip("Safety cap (seconds) a single queued action may hold the queue before force-advancing. Keep this ABOVE the longest real clip plus its waits (the heavy hits run ~6s), but not so high that a stuck predicate costs the whole value: every second here is a second of dead air when something goes wrong.")]
    [SerializeField] private float maxActionHold = 6f;
    [Tooltip("Extra seconds past the minimum hold that the post-attack hold will wait for the victim's hit clip to end. Must exceed the longest hit reaction (the heavy hits here are ~6s), otherwise the getup fires while the victim is still on the ground. A DEAD victim short-circuits this anyway.")]
    [SerializeField] private float maxHoldOvershoot = 8f;

    [Header("Diagnostics")]
    [Tooltip("Log queue steps, moves and playback (verbose debugging). All messages are prefixed with 'Animation'.")]
    [SerializeField] private bool verboseLogging = true;

    // ------------------------------------------------------------------
    // Attack-spot distances: SINGLE SOURCE OF TRUTH
    // ------------------------------------------------------------------
    // These are constants, and Awake re-asserts them onto the serialized fields every run.
    //
    // WHY: the serialized fields are still shown in the Inspector, but a value stored in the SCENE wins
    // over the code default. That silently broke the light/heavy spacing: the code said 1.1 and 0.75,
    // yet the trace reported "distance=1, scale=0.5", i.e. a light attack closed to HALF the heavy
    // distance instead of three quarters, and the absolute distances were wrong too. Re-asserting here
    // means the numbers below are the only place the spacing is defined.
    // NOTE: the attack-spot distances are deliberately re-asserted in Awake from the constants below, so
    // a stale value left in the SCENE cannot override them.
    private const float DefaultAttackSpotDistanceHeavy = 2.2f;
    private const float DefaultAttackSpotDistanceLight = 1.5f;
    // Unused since the two distances above replaced it; kept so the serialized field still has a value.
    private const float DefaultLightAttackSpotScale = 0.75f;

    // Move-in pacing. moveDuration is the close-in step BEFORE the clip; respaceDuration is the SINGLE
    // return to the standard gap AFTER the clip. Both are deliberately short so the sequence reads as
    // "close in -> hit -> settle back" instead of two full walks per exchange.
    private const float DefaultMoveDuration = 0.22f;
    private const float DefaultRespaceDuration = 0.2f;

    // ------------------------------------------------------------------
    // Logging
    // ------------------------------------------------------------------
    // Every log emitted by this controller is routed through AnimLogChecker, so the whole animation
    // pipeline shares ONE filterable tag: "[AnimLogChecker]". Gather the full trace in the console by
    // filtering for that single string, then send it on as-is.

    /// <summary>Logs an animation message through the shared AnimLogChecker flag.</summary>
    private void Alog(string message)
    {
        AnimLogChecker.Log("Positioning", message);
    }

    /// <summary>Logs an animation WARNING through the shared AnimLogChecker flag.</summary>
    private void AlogWarn(string message)
    {
        AnimLogChecker.LogWarning("Positioning", message);
    }

    /// <summary>Logs an animation ERROR (never filtered out -- errors are always shown).</summary>
    private void AlogError(string message)
    {
        Debug.LogError($"{AnimLogChecker.Tag}[Positioning] {message}", this);
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

        /// <summary>Time.unscaledTime when this item was enqueued, used by the LATENCY diagnostics to
        /// report how long the item waited before it actually started running.</summary>
        public float enqueuedAt;
    }

    private readonly Queue<QueuedAction> _queue = new Queue<QueuedAction>();
    private Coroutine _queueRunner;
    private bool _isRunningAction;

    /// <summary>
    /// Duration of the most recent PlayPairAndWait, in seconds. PlayPairAndWait is a separate coroutine,
    /// so it cannot add to the caller's running total directly; it stores the value here and the queue
    /// picks it up straight after the yield. Diagnostics only.
    /// </summary>
    private float LastClipWaitSeconds;

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
    // fighter returns to once a clip finishes.
    //
    // EACH SIDE IS CAPTURED EXACTLY ONCE (see CaptureGroundLock). The per-side flags are what make that
    // once-only: without them, the capture ran again on every SetFighters() call -- i.e. every turn and
    // every queued event -- and re-read the LIVE position.y, so an animated crouch/step pose became the
    // new "ground" and the fighter slowly sank into the floor.
    private bool _groundCaptured;
    private bool _leftGroundCaptured;
    private bool _rightGroundCaptured;
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

    // Which side of the arena each fighter currently stands on, judged against the FIXED battle centre.
    // Refreshed once per turn (CaptureStanceSides) and used to route every step onto the fighter's OWN
    // side, so an attack can never drag a model through its opponent to the other side of the arena.
    private bool _stanceSidesKnown;
    private bool _leftOnLeftSide = true;
    private bool _rightOnRightSide = true;

    // True while the fighters are being walked into their waiting stance (match start).
    private bool _stanceResetRunning;

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
    /// STRONGER companion to <see cref="LocomotionSettledHandler"/>: reports whether the side's animator
    /// is LITERALLY sitting in a standing Idle state, i.e. a state the Animator Controller will accept an
    /// attack / hit trigger FROM. The controller authors those transitions only from Idle, so this is the
    /// real precondition for firing; the locomotion check only approximates it.
    ///
    /// When wired, this takes priority and a trigger is only fired once it reports true (bounded by
    /// maxMoveSettleWait). When null, the queue falls back to the locomotion check, preserving the old
    /// behaviour.
    /// </summary>
    public LocomotionSettledDelegate AttackerIdleReadyHandler { get; set; }

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

    /// <summary>
    /// Fast "can the queue move on?" test for the post-animation waits (steps 4b / 4c).
    ///
    /// WHY THIS EXISTS: <see cref="BothFightersIdleSettled"/> maps to the bridge's
    /// IsFullyIdleNow, which demands that the animator is LITERALLY sitting in the Idle state. That is
    /// stricter than the end-of-clip event: AnimationEndAction can have already reported the hit/attack
    /// state EXITING while the animator is still in a knockdown pose, a getup, or merely a transition
    /// frame on its way back to Idle. During those frames IsFullyIdleNow stays false, so the post-
    /// animation waits kept spinning out their whole 8s cap even though the clip had genuinely ended --
    /// which is the "the end event fired but the turn still drags" symptom.
    ///
    /// This check therefore also accepts the AUTHORITATIVE end-of-clip signal for BOTH sides: once each
    /// side that was animated has reported its state exiting, nothing is playing any more and the queue
    /// may advance. A DEAD fighter is excluded, because a lethal hit parks in a looping KO pose that
    /// never reports clip-finished and whose body must not be repositioned.
    /// </summary>
    private bool BothFightersSettledFast()
    {
        // A DEAD fighter's body stays where it fell and its KO pose never reports clip-finished, so it is
        // SKIPPED rather than treated as "not settled". Previously ANY dead fighter made this return false
        // unconditionally, which meant the post-animation waits (re-spacing, camera release) ran to their
        // full cap on a lethal hit -- measured as "waited 8.052s (cap=8.05s)" on the KO turn.
        //
        // The caller already avoids REPOSITIONING a dead body (it checks IsSideDead before moving anyone);
        // this function only answers "may the queue advance?", and a corpse must not hold it back.
        bool leftDead = IsSideDead(PlayerUI.Side.Left);
        bool rightDead = IsSideDead(PlayerUI.Side.Right);

        // Both dead: there is nothing left to animate.
        if (leftDead && rightDead) return true;

        // The strict check covers the normal case (animators genuinely back in Idle). A dead side is
        // ignored by it, because it can never reach Idle.
        if (BothFightersIdleSettled()) return true;

        // Otherwise accept the precise end-of-clip event, skipping the dead side for the same reason.
        if (SideAnimationEndSignalledHandler == null) return false;
        try
        {
            bool leftOk = leftDead || SideAnimationEndSignalledHandler(PlayerUI.Side.Left);
            bool rightOk = rightDead || SideAnimationEndSignalledHandler(PlayerUI.Side.Right);
            return leftOk && rightOk;
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

        // Re-assert the attack-spot spacing from the constants above, so a stale value left in the SCENE
        // cannot override the code default (see the constants for why this matters).
        attackSpotDistanceHeavy = DefaultAttackSpotDistanceHeavy;
        attackSpotDistanceLight = DefaultAttackSpotDistanceLight;
        lightAttackSpotScale = DefaultLightAttackSpotScale;   // unused, kept so old scenes load

        // Same trap as the two above: these are the ONLY places the move-in policy and its pacing live.
        // A stale scene value used to keep the old "always step to the spot" behaviour alive even after
        // the code was changed, which is exactly the bug being fixed here.
        stepOutToAttackSpot = false;
        moveDuration = DefaultMoveDuration;
        respaceDuration = DefaultRespaceDuration;

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
    /// Records the ground Y / Z / yaw each fighter should be pinned to.
    ///
    /// CALLED ONLY ONCE PER FIGHTER. The capture is a BOOTSTRAP of the scene's intended placement, so
    /// it must happen while the model is in its neutral pose -- NOT on every SetFighters() call.
    ///
    /// THE BUG THIS FIXES ("fighters slowly sink into the floor after several animations"):
    /// SetFighters() is called by GameManager on EVERY turn and EVERY queued event, and it used to call
    /// this method unconditionally. Each call re-read the fighter's LIVE position.y -- which, mid-clip,
    /// is the animated Y (a crouch / step / hit pose that sits lower than the ground) -- and stored THAT
    /// as the new ground height. SyncYToGround then dutifully lowered the model to it. Every animation
    /// shaved a little more off the height, so over a match the fighter visibly sank through the floor.
    ///
    /// The ground is now captured ONCE per fighter and never overwritten by an animated pose. Use
    /// <see cref="RebaseGroundLock"/> explicitly if a legitimate re-placement really is needed.
    /// </summary>
    private void CaptureGroundLock()
    {
        // Capture the LEFT fighter's ground only while it is still unknown.
        if (leftFighter != null && !_leftGroundCaptured)
        {
            _leftGroundY = leftFighter.position.y;
            _leftLockedZ = leftFighter.position.z;
            _leftLockedYaw = leftFighter.eulerAngles.y;
            _leftGroundCaptured = true;
        }
        // Same for the RIGHT fighter.
        if (rightFighter != null && !_rightGroundCaptured)
        {
            _rightGroundY = rightFighter.position.y;
            _rightLockedZ = rightFighter.position.z;
            _rightLockedYaw = rightFighter.eulerAngles.y;
            _rightGroundCaptured = true;
        }
        _groundCaptured = _leftGroundCaptured || _rightGroundCaptured;

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

            // The scene placement defines the two side lanes, so the fighters keep the ground the scene
            // gave them (they are never shifted to a computed stance unless that is explicitly asked for).
            _stanceSidesKnown = false;

            if (resetStanceOnMatchStart && !_stanceResetRunning)
                StartCoroutine(ResetToWaitingStanceAtMatchStart());
        }
    }

    /// <summary>
    /// FORCE a re-capture of the ground/Z/facing lock at the fighters' CURRENT pose.
    ///
    /// DELIBERATELY EXPLICIT: this is the ONLY way to move the ground reference after it has been
    /// captured, and it reads the LIVE pose (so it must be called when the models are standing in their
    /// intended neutral position, never mid-clip).
    ///
    /// It exists for a genuine re-placement (a respawn, a new round, a scene that moved the fighters),
    /// NOT for routine use: the automatic path no longer re-captures, precisely because a capture taken
    /// while a clip was playing baked the animated Y in as the ground and made the fighters sink.
    /// </summary>
    public void RebaseGroundLock()
    {
        // Clear the once-only flags, then re-capture from the current (assumed neutral) pose.
        _leftGroundCaptured = false;
        _rightGroundCaptured = false;
        CaptureGroundLock();

        if (verboseLogging)
            Alog($"RebaseGroundLock(): ground re-captured (leftY={_leftGroundY:0.###}, rightY={_rightGroundY:0.###}).");
    }

    /// <summary>
    /// Per-frame lock applied to a single fighter root in LateUpdate:
    /// - Z is pinned to the captured fighting line, so a clip can never drift the model forward/back.
    /// - Rotation is pinned to the captured facing, so a clip can never spin/tilt the model while it
    ///   plays (the facing correction between turns is applied separately, in ApplyFacingAfterAnim).
    /// X is left untouched (X = step move). Y is NEVER written while a clip plays -- not by this lock and
    /// not anywhere else in the controller -- so a jump / flying clip is free to lift the model. The ONLY
    /// thing that ever touches Y is SnapYToGround, and that runs exclusively AFTER a clip has finished
    /// (queue step 4), so the model is always put back on its ground once the animation is over.
    /// </summary>
    private void ApplyRuntimeLock(Transform fighter, float lockedZ, float lockedYaw)
    {
        if (fighter == null) return;

        Vector3 pos = fighter.position;
        float z = lockDepthZ ? lockedZ : pos.z;

        // Y is passed through VERBATIM: this lock owns Z and rotation only. Whether the model is airborne
        // is decided entirely by the clip; the re-grounding happens in the post-animation correction.
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
    /// Re-aims a fighter at its opponent RIGHT NOW (yaw only, pitch/roll cleared) and refreshes the
    /// runtime yaw lock so LateUpdate keeps this new facing.
    ///
    /// This MUST be called after any reposition (the step-in move) and BEFORE the attack clip is
    /// fired. Otherwise the model attacks while still facing the direction it had before the move --
    /// the "attack animation plays before the fighter is in position" symptom.
    /// </summary>
    private void FaceOpponentNow(Transform fighter, Transform opponent)
    {
        if (fighter == null || opponent == null) return;

        Vector3 dir = opponent.position - fighter.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return; // degenerate: leave the facing alone
        float yaw = Quaternion.LookRotation(dir.normalized, Vector3.up).eulerAngles.y;

        if (faceEachOther)
            fighter.rotation = Quaternion.Euler(0f, yaw, 0f);

        // Refresh the lock so LateUpdate does not snap the model back to the stale yaw.
        if (fighter == leftFighter) _leftLockedYaw = yaw;
        else if (fighter == rightFighter) _rightLockedYaw = yaw;
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
        // UNSCALED: choreography pacing, not gameplay simulation -- see MoveFighterTo for why.
        while (elapsed < groundResetDuration)
        {
            elapsed += Time.unscaledDeltaTime;
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
    /// True when the given side is the LEFT side of the arena. "Left" is resolved from the character
    /// bridge order when available, otherwise from the inspector-assigned transform.
    /// </summary>
    public static bool IsLeftSide(PlayerUI.Side side)
    {
        return side == PlayerUI.Side.Left;
    }

    /// <summary>
    /// Refreshes which side of the battle centre each fighter stands on. Called ONCE per turn, BEFORE
    /// the attacker steps in, so every step of that turn is routed onto the fighter's own side. A
    /// fighter that ended the previous turn on the wrong side is routed home instead of through its
    /// opponent.
    /// </summary>
    private void CaptureStanceSides()
    {
        if (leftFighter == null || rightFighter == null) return;

        Vector3 centre = BattleCenter;
        if (!_homeCaptured)
        {
            // No fixed centre yet (fighters not wired at match start): fall back to the live midpoint.
            centre = (leftFighter.position + rightFighter.position) * 0.5f;
        }

        _leftOnLeftSide = leftFighter.position.x <= centre.x;
        _rightOnRightSide = rightFighter.position.x >= centre.x;
        _stanceSidesKnown = true;

        if (verboseLogging)
            Alog($"Stance sides: left={( _leftOnLeftSide ? "LEFT" : "RIGHT" )}, right={( _rightOnRightSide ? "RIGHT" : "LEFT" )} (centre x={centre.x:0.###}).");
    }

    /// <summary>
    /// The point this SIDE should stand on for a given attack distance.
    ///
    /// The spot is measured FROM THE VICTIM along the attacker's own approach direction, so the distance
    /// that matters -- how far the attacker ends up from the fighter it is hitting -- is exactly the
    /// configured attack distance. This is what makes a LIGHT attack land closer than a HEAVY one.
    ///
    /// WHY IT IS NOT MEASURED FROM THE BATTLE CENTRE ANY MORE: anchoring to the fixed centre placed the
    /// attacker at a CONSTANT world position while the victim kept moving. The resulting gap was therefore
    /// |centre ± distance - victim.x|, which drifted with wherever the victim happened to stand. The trace
    /// showed the same light attack producing gaps of 0.70, 1.925 and 2.115, and a heavy producing 2.2,
    /// 2.358 and 2.642 -- the configured distance was never what the player actually saw.
    ///
    /// The centre is still used as a FALLBACK when no victim is available, and the caller's
    /// ClampToOwnSide still guarantees the attacker can never be placed past its opponent.
    /// </summary>
    private Vector3 ComputeSideSpot(PlayerUI.Side side, float distance, Transform attacker, Transform victim)
    {
        // Preferred: a point `distance` from the VICTIM, on the attacker's own side of it. This is the
        // only formulation that guarantees the requested gap.
        if (victim != null)
        {
            float sign = IsLeftSide(side) ? -1f : 1f;

            // Approach along the arena's X axis (the fighting line), so the attacker stops at a precise
            // horizontal distance from the victim regardless of which way the models are facing.
            Vector3 spot = victim.position + new Vector3(sign * distance, 0f, 0f);

            spot.y = GroundYForSide(side) + attackSpotOffset.y;
            spot += new Vector3(attackSpotOffset.x, 0f, attackSpotOffset.z);
            return spot;
        }

        // Fallback: no victim known yet, so fall back to the fixed centre lane.
        if (!_homeCaptured) return Vector3.zero;

        float fallbackSign = IsLeftSide(side) ? -1f : 1f;
        Vector3 fallbackSpot = BattleCenter + new Vector3(fallbackSign * distance, 0f, 0f);
        fallbackSpot.y = GroundYForSide(side) + attackSpotOffset.y;
        fallbackSpot += new Vector3(attackSpotOffset.x, 0f, attackSpotOffset.z);
        return fallbackSpot;
    }

    /// <summary>
    /// Clamps <paramref name="target" /> (X only) so the fighter keeps its OWN side of the battle centre
    /// and a minimum gap from its opponent. This is the guarantee that a step can never carry a model
    /// THROUGH the other one: the target is pushed back onto the fighter's own side before it moves.
    ///
    /// WHICH SIDE is decided from the FIXED BATTLE CENTRE, not from the fighter's current position.
    /// Using the live position made the result depend on where the fighter happened to be standing when
    /// the step was computed: a fighter that had not yet walked back from the previous exchange was
    /// judged to be on the OTHER side, so the same attack produced a different final gap from one turn
    /// to the next (observed: light attacks landing at 1.40 then 2.10 units from the victim). The battle
    /// centre is captured once at match start and never moves, so the clamp is now consistent.
    /// </summary>
    private Vector3 ClampToOwnSide(Transform fighter, Transform opponent, Vector3 target)
    {
        if (fighter == null || opponent == null) return target;

        float opponentX = opponent.position.x;
        float minGap = Mathf.Max(0f, minFighterDistance);

        // Prefer the FIXED centre when it is known: the side is a property of the fighter's LANE, not of
        // its momentary position. Fall back to the opponent comparison only before the centre exists.
        bool onLeftOfOpponent;
        if (_homeCaptured)
        {
            float centreX = BattleCenter.x;
            // Which lane does this fighter BELONG to? Resolve it from the side it was assigned, so the
            // clamp cannot flip just because the model is currently standing across the centre.
            bool isLeftFighter = fighter == leftFighter;
            onLeftOfOpponent = isLeftFighter ? true : false;

            // If the fighter is not one of the two known roots (edge case), fall back to the centre.
            if (fighter != leftFighter && fighter != rightFighter)
                onLeftOfOpponent = fighter.position.x <= centreX;
        }
        else
        {
            onLeftOfOpponent = fighter.position.x <= opponentX;
        }

        float x = target.x;
        x = onLeftOfOpponent
            ? Mathf.Min(x, opponentX - minGap)   // stay on the left of the opponent
            : Mathf.Max(x, opponentX + minGap);  // stay on the right of the opponent
        target.x = x;
        return target;
    }

    /// <summary>
    /// Computes the attack spot for the attacker on <paramref name="attackerSide" />, without moving
    /// anyone. The spot sits on the fighting line, on the attacker's OWN side, at
    /// <see cref="attackSpotDistanceHeavy" /> (heavy attacks) or <see cref="attackSpotDistanceLight" />
    /// (light attacks) from the VICTIM.
    /// Returns zero when the victim is not supplied.
    /// </summary>
    /// <param name="distanceScale">
    /// SELECTS which of the two distances to use, it does not scale one: below 1 (what a light attack
    /// passes) selects <see cref="attackSpotDistanceLight" />, anything else selects
    /// <see cref="attackSpotDistanceHeavy" />.
    /// </param>
    public Vector3 ComputeAttackSpot(PlayerUI.Side attackerSide, Transform attacker, Transform victim,
                                     float distanceScale = 1f)
    {
        if (attacker == null || victim == null) return Vector3.zero;

        float distance = Mathf.Max(0f, SpotDistanceForScale(distanceScale));

        Vector3 spot;
        if (attackSpotsRelativeToCentre)
        {
            // Spots are anchored to the FIXED battle centre, so a fighter that strayed behind its
            // opponent walks BACK to its own side instead of staying behind it.
            spot = ComputeSideSpot(attackerSide, distance, attacker, victim);
            if (spot == Vector3.zero)
            {
                // No centre captured yet: fall back to the victim-relative spot below.
                spot = SpotInFrontOfVictim(attacker, victim, distance);
            }
        }
        else
        {
            // Legacy behaviour: stand in front of the victim, along the victim's forward axis.
            spot = SpotInFrontOfVictim(attacker, victim, distance);
        }

            // Y MUST COME FROM THE CAPTURED GROUND, NEVER FROM THE LIVE POSE.
            //
            // Reading attacker.position.y here meant the spot inherited whatever Y the current clip had
            // put the model at (a crouch / lunge / hit pose), and that Y then travelled with the spot
            // into MoveFighterTo -- which writes the whole Vector3. So a move could actively push the
            // fighter OFF the ground plane, on top of the drift the capture bug already caused.
            // The attacker's captured GROUND height is the only correct source for a step target.
            spot.y = GroundYForSide(attackerSide) + attackSpotOffset.y;

        _currentAttackSpot = spot;
        _hasAttackSpot = true;

        if (verboseLogging)
            Alog($"Attack spot for {attackerSide} attacker '{attacker.name}' = {spot} " +
                 $"(distance={distance:0.###}, scale={distanceScale:0.###}, relativeToCentre={attackSpotsRelativeToCentre})");

        // DIAGNOSTIC: report the CONFIGURED values alongside the computed spot. The two spot distances are
        // independent fields now, so if the resulting gap does not equal the one this attack selected,
        // something has overridden it (the Inspector copy of a serialized field wins over the code default).
        Alog($"LATENCY [SPOT-CONFIG] {attackerSide}: attackSpotDistanceHeavy={attackSpotDistanceHeavy:0.###}, " +
             $"attackSpotDistanceLight={attackSpotDistanceLight:0.###}, selected distance={distance:0.###} " +
             $"(distanceScale={distanceScale:0.###}), " +
             $"desiredFighterDistance={desiredFighterDistance:0.###} (runtime uses the heavy distance), " +
             $"minFighterDistance={minFighterDistance:0.###}, sideLaneOffset={sideLaneOffset:0.###}.");

        // DIAGNOSTIC: the ACTUAL gap this spot produces between the two fighters. This is what the player
        // sees, and it is what the light/heavy difference should be judged on -- not the nominal distance.
        // With the spot anchored to the victim it should now equal the requested `distance` exactly; if it
        // does not, ClampToOwnSide pushed it back (usually because the requested distance is below
        // minFighterDistance, or the attacker's lane is on the wrong side of the victim).
        float resultingGap = Mathf.Abs(spot.x - victim.position.x);
        Alog($"LATENCY [SPOT-GAP] {attackerSide}: spot.x={spot.x:0.###}, victim.x={victim.position.x:0.###}, " +
             $"resulting gap={resultingGap:0.###} (requested {distance:0.###}).");

        if (Mathf.Abs(resultingGap - distance) > 0.02f)
        {
            AlogWarn($"LATENCY [SPOT-GAP-CLAMPED] {attackerSide}: the spot was clamped -- requested {distance:0.###} " +
                     $"from the victim but the final gap is {resultingGap:0.###}. Check that the requested distance " +
                     $"is ABOVE minFighterDistance ({minFighterDistance:0.###}) and that the attacker's lane is on " +
                     $"its own side of the victim.");
        }

        return spot;
    }

    /// <summary>Legacy spot: a point in front of the victim, along the victim's forward axis.</summary>
    private Vector3 SpotInFrontOfVictim(Transform attacker, Transform victim, float distance)
    {
        Vector3 forward = victim.forward;
        forward.y = 0f;                 // keep the step on the ground plane
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
        forward.Normalize();

        Vector3 spot = victim.position + forward * distance;

        // Y comes from the VICTIM'S CAPTURED GROUND, not from the live pose of either fighter. The old
        // `attacker.position.y` read dragged an animated (crouching / airborne) height into the step
        // target, which the move then wrote onto the fighter. The victim's side tells us which captured
        // ground applies, since this legacy path has no side argument of its own.
        float groundY = victim == rightFighter
            ? GroundYForSide(PlayerUI.Side.Right)
            : GroundYForSide(PlayerUI.Side.Left);
        spot.y = groundY + attackSpotOffset.y;

        spot += new Vector3(attackSpotOffset.x, 0f, attackSpotOffset.z);
        return spot;
    }

    /// <summary>
    /// Distance scale to use for an attacker animation id. A LIGHT attack returns a value below 1 so
    /// <see cref="ComputeAttackSpot" /> selects <see cref="attackSpotDistanceLight" />; everything else
    /// returns 1, which selects <see cref="attackSpotDistanceHeavy" />.
    /// </summary>
    public float DistanceScaleForAnimation(string attackerAnimationId)
    {
        if (IsLightAnimation(attackerAnimationId)) return LightDistanceScale;
        return 1f;
    }

    // The scale values are now only a SELECTOR for which of the two spot distances applies: anything
    // below 1 means "light", 1 means "heavy". The actual distances are configured independently.
    private const float LightDistanceScale = 0.5f;

    /// <summary>
    /// Resolves the SELECTED spot distance from a distance scale. Below 1 = the light distance,
    /// otherwise the heavy one. Kept as a single place so <see cref="ComputeAttackSpot" /> and the
    /// diagnostics can never disagree about which distance was used.
    /// </summary>
    private float SpotDistanceForScale(float distanceScale)
    {
        return distanceScale < 1f ? attackSpotDistanceLight : attackSpotDistanceHeavy;
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
    /// SELECTS the spot distance: a light attack passes a value below 1 and walks in to
    /// <see cref="attackSpotDistanceLight" />; anything else walks in to
    /// <see cref="attackSpotDistanceHeavy" />.
    /// </param>
    public IEnumerator StepAttackerToSpot(PlayerUI.Side attackerSide, Transform attacker, Transform victim,
                                          float distanceScale = 1f)
    {
        if (attacker == null || victim == null)
        {
            AlogWarn("StepAttackerToSpot called with a null attacker/victim.");
            yield break;
        }

        Vector3 spot = ComputeAttackSpot(attackerSide, attacker, victim, distanceScale);

        // --- MOVE-IN ONLY FOR THE SWING -------------------------------------------------------------
        //
        // THE GAP PROBLEM THIS FIXES: the attacker used to be dragged to the computed spot
        // UNCONDITIONALLY. When the spot is FARTHER than where the attacker already stands (which is what
        // happens whenever the pair rests wider than the selected spot) the model visibly stepped
        // BACKWARDS, away from its opponent, and the queue then dragged it home again: the old "pushed far
        // out, then pulled back in" two-step.
        //
        // THE INTENDED FLOW (see the two spot distances in the Inspector):
        //   * The pair RESTS at attackSpotDistanceHeavy after every exchange (the single return move).
        //   * A HEAVY attack therefore needs NO step-in at all -- it is already at its spot.
        //   * A LIGHT attack's spot is CLOSER, so it is farther out than its spot and genuinely walks the
        //     difference (heavy minus light) IN before the clip fires.
        //
        // So the rule below is exactly what that flow needs: travel only to CLOSE distance, never to open
        // it. Staying put when already at (or nearer than) the spot is what makes the heavy attack a
        // zero-step swing and leaves the light attack's walk as the only extra movement.
        //
        // Set stepOutToAttackSpot to true to restore the old "always walk to the spot" behaviour.
        if (!stepOutToAttackSpot)
        {
            float currentGap = Mathf.Abs(attacker.position.x - victim.position.x);
            float spotGap = Mathf.Abs(spot.x - victim.position.x);

            if (currentGap <= spotGap + 0.02f)
            {
                // Already at (or closer than) the attack spot: NO move at all, so nothing can push the
                // attacker out. Just square up so the clip plays from the correct heading.
                FaceOpponentNow(attacker, victim);
                FaceOpponentNow(victim, attacker);
                yield return null;

                _currentAttackSpot = attacker.position;

                if (verboseLogging)
                    Alog($"'{attacker.name}' stays at its current spot (gap={currentGap:0.###} <= target " +
                         $"{spotGap:0.###}) -- no step-out before the swing.");
                yield break;
            }

            if (verboseLogging)
                Alog($"'{attacker.name}' closes in for the swing: gap {currentGap:0.###} -> {spotGap:0.###} " +
                     $"(target = {(distanceScale < 1f ? "LIGHT" : "HEAVY")} spot).");
        }

        // Drive the run locomotion while stepping, then stop (speed 0) on arrival. This is what makes
        // the model actually RUN to the spot instead of sliding there in idle.
        SetMoveSpeed(attackerSide, moveSpeedValue);

        yield return MoveFighterTo(attackerSide, attacker, spot);

        // --- ARRIVED: square up BEFORE the attack clip fires ---------------------------------------
        //
        // The attack clip must play from the position the fighter just stepped to AND facing the
        // victim. Without this the model plays its attack while still pointing wherever it was
        // aiming before the move (and the runtime yaw lock in LateUpdate would keep it that way),
        // which reads as "the attack animation starts before the fighter is in position".
        FaceOpponentNow(attacker, victim);
        FaceOpponentNow(victim, attacker);

        // Let the animator apply the new facing for one frame before the clip is triggered, so the
        // attack does not start from the pre-turn pose.
        yield return null;

        // Remember where the attacker actually stands so the return-home move can be verified.
        _currentAttackSpot = spot;

        if (verboseLogging)
            Alog($"'{attacker.name}' stepped to attack spot {spot} and squared up (Speed=0).");
    }

    /// <summary>
    /// Walks a fighter back onto its OWN side of the arena (its side lane), used to un-stack the two
    /// models after an exchange. The target is clamped so the fighter always stops on the side it
    /// currently occupies relative to its opponent -- it never crosses over. Returns a coroutine so the
    /// queue can wait for it.
    /// </summary>
    public IEnumerator StepFighterToOwnSide(PlayerUI.Side side, Transform fighter, Transform opponent)
    {
        if (fighter == null || opponent == null) yield break;

        // Re-read the side the fighter is on RIGHT NOW, so the walk-back always heads to the side the
        // fighter already occupies and never sweeps it across its opponent.
        float opponentX = opponent.position.x;
        bool onLeftOfOpponent = fighter.position.x <= opponentX;
        float minGap = Mathf.Max(0f, minFighterDistance);

        Vector3 target = ComputeSideSpot(side, sideLaneOffset, fighter, null);
        if (target == Vector3.zero)
        {
            // No fixed battle centre: fall back to a plain gap-keeping nudge on the current side.
            target = fighter.position;
            target.x = onLeftOfOpponent ? opponentX - minGap : opponentX + minGap;
        }
        else
        {
            // The side lane is a MINIMUM: never pull the fighter closer than the hard floor, otherwise
            // a small sideLaneOffset would stack the two models on top of each other.
            target.x = onLeftOfOpponent
                ? Mathf.Min(target.x, opponentX - minGap)
                : Mathf.Max(target.x, opponentX + minGap);
        }

        // Keep the fighter on the arena Z line / ground height; only X travel is meaningful here.
        target.z = fighter.position.z;

        // Never cross the opponent on the way back.
        target.x = onLeftOfOpponent
            ? Mathf.Min(target.x, opponentX - minGap)
            : Mathf.Max(target.x, opponentX + minGap);

        if (verboseLogging)
            Alog($"walking '{fighter.name}' back to its own side {target} (opponent x={opponentX:0.###}).");

        SetMoveSpeed(side, moveSpeedValue);
        yield return MoveFighterTo(side, fighter, target, stanceReturnDuration);
    }

    /// <summary>
    /// Puts the two fighters back at the STANDARD spacing between exchanges: this is the ONE return
    /// move that runs after a clip finishes.
    ///
    /// THE STANDARD GAP IS THE HEAVY ATTACK SPOT (<see cref="attackSpotDistanceHeavy" />). Making the
    /// resting distance and the heavy spot the SAME number by construction is what gives the intended
    /// sequence:
    ///   * a HEAVY attack is already standing on its spot, so it needs no step-in at all;
    ///   * a LIGHT attack's spot is closer, so it walks the difference IN before swinging.
    ///
    /// If the resting gap were a separate value, the heavy attack would have to step out or in first and
    /// the two weights would not be comparable.
    ///
    /// It eases BOTH fighters to their side lanes (each at half the standard gap from the fixed battle
    /// centre) at the same time, so the pair is always re-centred and re-spaced symmetrically before the
    /// next turn -- instead of slowly drifting together exchange after exchange until the models overlap.
    ///
    /// Two guarantees, both needed:
    ///   * the standard gap (the heavy distance) when spacing enforcement is on, and
    ///   * the hard floor (<see cref="minFighterDistance" />) always.
    ///
    /// Nobody is ever moved THROUGH the other model: each target is clamped onto the fighter's own side
    /// of its opponent (ClampToOwnSide), and the travel is interpolated over several frames.
    ///
    /// Returns a coroutine the queue waits for; it completes in a single frame when the fighters are
    /// already at the right spacing.
    /// </summary>
    public IEnumerator RepositionToFightStance()
    {
        Transform left = leftFighter;
        Transform right = rightFighter;
        if (left == null || right == null) yield break;

        float minGap = Mathf.Max(0f, minFighterDistance);
        // THE standard gap: the heavy attack spot. desiredFighterDistance is intentionally NOT used -- it
        // is a legacy field, and letting it win here would put the resting gap out of step with the heavy
        // spot, which is exactly what would reintroduce a step-in for a heavy attack.
        float desiredGap = Mathf.Max(minGap, attackSpotDistanceHeavy);
        float gap = Mathf.Abs(right.position.x - left.position.x);

        Vector3 leftTarget;
        Vector3 rightTarget;

        if (_homeCaptured)
        {
            // Preferred: aim each fighter at its own lane, measured from the FIXED battle centre. This
            // both restores the spacing AND re-centres the pair, so they cannot drift off one side.
            float half = desiredGap * 0.5f;
            leftTarget = ComputeSideSpot(PlayerUI.Side.Left, half, left, null);
            rightTarget = ComputeSideSpot(PlayerUI.Side.Right, half, right, null);
        }
        else
        {
            // No fixed centre (fighters not wired at match start): space them out around their midpoint.
            float midX = (left.position.x + right.position.x) * 0.5f;
            float half = desiredGap * 0.5f;
            leftTarget = left.position; leftTarget.x = midX - half;
            rightTarget = right.position; rightTarget.x = midX + half;
        }

        // NEVER cross the opponent, and always respect the hard floor.
        leftTarget = ClampToOwnSide(left, right, leftTarget);
        rightTarget = ClampToOwnSide(right, left, rightTarget);

        leftTarget.z = left.position.z;
        rightTarget.z = right.position.z;

        // Already at the right spacing -> nothing to do (no animation, no wait).
        bool leftOk = Mathf.Abs(left.position.x - leftTarget.x) <= 0.02f;
        bool rightOk = Mathf.Abs(right.position.x - rightTarget.x) <= 0.02f;
        if (leftOk && rightOk) yield break;

        // The floor is enforced even when the standard-gap pass is disabled.
        if (!enforceFighterSpacing && gap >= minGap) yield break;

        if (verboseLogging)
            Alog($"re-spacing fighters: gap={gap:0.###} -> {desiredGap:0.###} (left->{leftTarget}, right->{rightTarget}).");

        SetMoveSpeed(PlayerUI.Side.Left, moveSpeedValue);
        SetMoveSpeed(PlayerUI.Side.Right, moveSpeedValue);

        Vector3 leftStart = left.position;
        Vector3 rightStart = right.position;

        // respaceDuration, NOT stanceReturnDuration: this is the ONE return to the standard gap that runs
        // after the clip, so it is kept short. stanceReturnDuration stays reserved for the match-start
        // stance walk (which is allowed to read as a walk).
        float duration = Mathf.Max(0.01f, respaceDuration);
        float elapsed = 0f;

        // UNSCALED: choreography pacing, not gameplay simulation -- see MoveFighterTo for why.
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = moveEase.Evaluate(t);

            if (left != null)
            {
                Vector3 pL = Vector3.Lerp(leftStart, leftTarget, eased);
                pL.y = left.position.y;   // Y is never touched by a re-spacing pass
                left.position = pL;
            }
            if (right != null)
            {
                Vector3 pR = Vector3.Lerp(rightStart, rightTarget, eased);
                pR.y = right.position.y;  // Y is never touched by a re-spacing pass
                right.position = pR;
            }

            yield return null;
        }

        if (left != null)
        {
            Vector3 lf = leftTarget; lf.y = left.position.y; left.position = lf;
        }
        if (right != null)
        {
            Vector3 rf = rightTarget; rf.y = right.position.y; right.position = rf;
        }

        SetMoveSpeed(PlayerUI.Side.Left, 0f);
        SetMoveSpeed(PlayerUI.Side.Right, 0f);

        if (verboseLogging)
            Alog($"fighters re-spaced to {Mathf.Abs(right.position.x - left.position.x):0.###} (Speed=0).");
    }

    /// <summary>
    /// Walks BOTH fighters to their waiting stance (the configured side lanes) AT THE SAME TIME. Only
    /// used at match start when <see cref="resetStanceOnMatchStart"/> is enabled; otherwise the fighters
    /// keep the placement the scene gave them.
    /// </summary>
    public IEnumerator ResetBothFightersToStance()
    {
        Transform left = leftFighter;
        Transform right = rightFighter;
        if (left == null || right == null) yield break;

        Vector3 leftTarget = ComputeSideSpot(PlayerUI.Side.Left, attackSpotDistanceHeavy * 0.5f, left, null);
        Vector3 rightTarget = ComputeSideSpot(PlayerUI.Side.Right, attackSpotDistanceHeavy * 0.5f, right, null);
        if (leftTarget == Vector3.zero || rightTarget == Vector3.zero) yield break;

        leftTarget.z = left.position.z;
        rightTarget.z = right.position.z;

        Vector3 leftStart = left.position;
        Vector3 rightStart = right.position;

        if (verboseLogging)
            Alog($"walking BOTH fighters to their waiting stance (left->{leftTarget}, right->{rightTarget}).");

        SetMoveSpeed(PlayerUI.Side.Left, moveSpeedValue);
        SetMoveSpeed(PlayerUI.Side.Right, moveSpeedValue);

        float duration = Mathf.Max(0.01f, stanceReturnDuration);
        float elapsed = 0f;

        // UNSCALED: choreography pacing, not gameplay simulation -- see MoveFighterTo for why.
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = moveEase.Evaluate(t);

            if (left != null)
            {
                Vector3 pL = Vector3.Lerp(leftStart, leftTarget, eased);
                pL.y = left.position.y;   // Y is never touched by a re-spacing pass
                left.position = pL;
            }
            if (right != null)
            {
                Vector3 pR = Vector3.Lerp(rightStart, rightTarget, eased);
                pR.y = right.position.y;  // Y is never touched by a re-spacing pass
                right.position = pR;
            }

            yield return null;
        }

        if (left != null)
        {
            Vector3 lf = leftTarget; lf.y = left.position.y; left.position = lf;
        }
        if (right != null)
        {
            Vector3 rf = rightTarget; rf.y = right.position.y; right.position = rf;
        }

        SetMoveSpeed(PlayerUI.Side.Left, 0f);
        SetMoveSpeed(PlayerUI.Side.Right, 0f);

        if (verboseLogging)
            Alog("both fighters in their waiting stance (Speed=0).");
    }

    /// <summary>
    /// Match-start helper: walks the pair into the waiting stance ONCE, the first time both fighters are
    /// known. Guarded so it only ever runs a single time per match.
    /// </summary>
    private IEnumerator ResetToWaitingStanceAtMatchStart()
    {
        _stanceResetRunning = true;
        yield return ResetBothFightersToStance();
        _stanceSidesKnown = false; // the walk may have changed who is where; re-read on the next turn
        _stanceResetRunning = false;
    }

    /// <summary>
    /// Eases a fighter to <paramref name="target" /> using the configured move ease, driving the run
    /// locomotion while it travels. Shared by the step-in, the gap-out and the stance walk.
    /// </summary>
    private IEnumerator MoveFighterTo(PlayerUI.Side side, Transform fighter, Vector3 target,
                                      float overrideDuration = -1f)
    {
        if (fighter == null) { SetMoveSpeed(side, 0f); yield break; }

        Vector3 startPos = fighter.position;
        float duration = Mathf.Max(0.01f, overrideDuration > 0f ? overrideDuration : moveDuration);
        float elapsed = 0f;
        float wallStart = Time.unscaledTime;
        int frames = 0;

        // THE MOVE OWNS X AND Z ONLY -- NEVER Y.
        //
        // A step is horizontal travel along the fighting line; the model's HEIGHT is not this method's
        // business. Writing the interpolated Vector3 wholesale meant the target's Y (and, worse, any
        // Y the Lerp passed through) was stamped onto the fighter DURING the move -- so the controller
        // touched Y while a clip was playing, which is exactly what must not happen.
        //
        // Y is therefore carried through VERBATIM from the fighter's own live value, frame by frame, so
        // an airborne clip keeps rising throughout the step and only SnapYToGround (after the clip ends)
        // ever moves it. The target's Y is ignored entirely.
        //
        // UNSCALED: this is choreography pacing, not gameplay simulation. Driving it with Time.deltaTime
        // meant a slow-motion effect (Time.timeScale = 0.2 from the template's CamSlowMotionDelay) stretched
        // a 0.35s step into ~1.75s of real time, and every wait in this controller with it. Pacing must
        // stay constant so the queue advances at a predictable rate regardless of the game's time scale.
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = moveEase.Evaluate(t);

            if (fighter == null) { SetMoveSpeed(side, 0f); yield break; } // destroyed mid-step

            // X and Z travel to the target; Y stays exactly where the clip has put it.
            Vector3 step = Vector3.Lerp(startPos, target, eased);
            step.y = fighter.position.y;
            fighter.position = step;
            frames++;
            yield return null;
        }

        // DIAGNOSTIC: with unscaled timing this should now always be close to `duration`. If it is still
        // far larger, the frame rate itself is the problem (a step advances one frame at a time).
        float wall = Time.unscaledTime - wallStart;
        if (wall > duration * 2f + 0.2f)
        {
            AlogWarn($"LATENCY [STEP-MOVE-SLOW] {side}: the {duration:0.000}s move took {wall:0.000}s of wall " +
                     $"time over {frames} frames (timeScale={Time.timeScale:0.###}, " +
                     $"unscaledDeltaTime={Time.unscaledDeltaTime:0.0000}s). A very low frame rate stretches " +
                     $"every frame-by-frame wait in this controller.");
        }

        if (fighter == null) { SetMoveSpeed(side, 0f); yield break; }

        // Land exactly on the target X/Z so the next attack is pixel-accurate. Y is carried over from
        // the fighter itself (see the loop above): the move never sets height. The stale comment here
        // used to claim "Y is re-pinned by LateUpdate", but LateUpdate only pins Z and rotation -- so
        // writing target.y here really did move the fighter vertically, mid-clip.
        Vector3 landed = target;
        landed.y = fighter.position.y;
        fighter.position = landed;

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
        float startTime = Time.unscaledTime;
        while (elapsed < cap)
        {
            bool attackerDone = IsSideFinished(attackerSide, hasAttacker);
            bool victimDone = IsSideFinished(victimSide, hasVictim);

            // Advance only once BOTH sides report finished AND we are past the small floor, so a stale
            // "finished" on the very first frame cannot skip the clip (IsSideFinished returns true when
            // nothing is being watched).
            bool attackerSettled = !hasAttacker || attackerDone;
            bool victimSettled = !hasVictim || victimDone;
            if (attackerSettled && victimSettled && elapsed >= floor)
            {
                // DIAGNOSTIC: this is the single most useful latency number for the animation step. If it
                // is close to the real clip length, the end signal is doing its job. If it sits at the cap
                // instead, the end signal never arrived and the wait ran out its safety bound -- which is
                // what drags a whole turn out.
                Alog($"LATENCY [CLIP-WAIT] attacker={attackerSide} '{attackerAnimationId}', " +
                     $"victim={victimSide} '{victimAnimationId}': finished after {Time.unscaledTime - startTime:0.000}s " +
                     $"(floor={floor:0.00}s, cap={cap:0.00}s, endSignal={SideAnimationEndSignalledHandler != null}).");
                LastClipWaitSeconds = Time.unscaledTime - startTime;
                break;
            }

            // STUCK-CLIP GUARD: a side that was asked to animate but whose animator NEVER entered an
            // action state means the trigger was swallowed -- no clip is playing, so no end-of-clip event
            // will EVER arrive and the wait would run out its whole safety cap (measured: exactly the
            // 15s maxActionHold, which is what made a turn drag). Once the floor has passed and neither
            // side is actually animating anything, there is nothing left to wait for, so advance.
            if (elapsed >= floor && !AnySideActivelyAnimating(attackerSide, hasAttacker,
                                                              victimSide, hasVictim))
            {
                Alog($"LATENCY [CLIP-WAIT-NO-CLIP] attacker={attackerSide} '{attackerAnimationId}', " +
                     $"victim={victimSide} '{victimAnimationId}': neither side is actually playing a clip after " +
                     $"{Time.unscaledTime - startTime:0.000}s -- advancing without waiting for the {cap:0.00}s cap. " +
                     $"The animation trigger was most likely swallowed.");
                LastClipWaitSeconds = Time.unscaledTime - startTime;
                break;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        // Reached here without breaking -> the cap expired while a side still reported "not finished".
        // That is the failure mode behind a turn that runs far longer than its clip.
        if (elapsed >= cap)
        {
            AlogWarn($"LATENCY [CLIP-WAIT-TIMEOUT] attacker={attackerSide} '{attackerAnimationId}', " +
                     $"victim={victimSide} '{victimAnimationId}': hit the {cap:0.00}s safety cap without both " +
                     $"sides reporting finished. The end-of-clip event is NOT arriving for at least one side.");
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

    [Tooltip("Anti-false-positive window (seconds) for the precise completion predicate: the predicate must report finished AND at least this much time must have passed, so a one-frame stale 'finished' cannot skip a clip. Keep it SMALL -- it is a guard, not a pacing value. It must never be derived from maxHold, or it becomes a floor equal to the safety cap and every item waits out its full bound.")]
    [SerializeField] private float minClipCompletionFloor = 0.15f;

    /// <summary>
    /// True when at least one of the sides we asked to animate is ACTUALLY playing an action clip.
    ///
    /// Used by the stuck-clip guard in <see cref="PlayPairAndWait"/>: when an animation trigger is
    /// swallowed (the controller only authors attack/hit transitions from Idle, so firing mid-blend can
    /// drop the trigger) no clip ever runs, so no end-of-clip event can arrive and the wait would sit
    /// out its entire safety cap. This check lets the queue notice that nothing is playing and move on.
    ///
    /// Uses the NON-MUTATING <see cref="SideActionPlayingHandler"/>, so polling it here does not consume
    /// the watch state. When that hook is not wired it returns true, i.e. the guard is disabled and the
    /// old cap-only behaviour is preserved.
    /// </summary>
    private bool AnySideActivelyAnimating(PlayerUI.Side attackerSide, bool hasAttacker,
                                          PlayerUI.Side victimSide, bool hasVictim)
    {
        if (SideActionPlayingHandler == null) return true;

        try
        {
            if (hasAttacker && SideActionPlayingHandler(attackerSide)) return true;
            if (hasVictim && SideActionPlayingHandler(victimSide)) return true;
        }
        catch { return true; }

        return false;
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
            enqueuedAt = Time.unscaledTime,
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
            enqueuedAt = Time.unscaledTime,
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

    /// <summary>
    /// Waits a fixed amount of REAL time, ignoring Time.timeScale.
    ///
    /// WHY: this queue is CHOREOGRAPHY, not gameplay simulation. Waiting with WaitForSeconds ties every
    /// spacing pause to the game's time scale, so a slow-motion effect (the template's CamSlowMotionDelay
    /// sets Time.timeScale = 0.2) stretched every pause fivefold -- a 0.4s breath became 2s of real time,
    /// on top of the clips themselves. Pacing must stay constant so a turn takes a predictable amount of
    /// wall-clock time no matter what the game does with its time scale.
    /// </summary>
    private IEnumerator WaitUnscaled(float seconds)
    {
        if (seconds <= 0f) yield break;

        float elapsed = 0f;
        while (elapsed < seconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
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

            // ---- LATENCY: queue time (candidate C) --------------------------------------------------
            // How long this item waited in the queue before it actually started running, and (at the end
            // of the item) how long the whole choreography took. Together these separate "the queue is
            // backed up" from "one exchange simply takes this long to animate".
            float itemStartTime = Time.unscaledTime;
            float queuedAt = action.enqueuedAt;
            // Running total of every wait this item performs that we instrument individually. Compared
            // against the item's full runtime at the end (see the SUMMARY line), it shows whether any
            // significant cost is still hiding in an uninstrumented yield.
            float measuredWaitTotal = 0f;
            if (queuedAt > 0f)
            {
                Alog($"LATENCY [QUEUE-START] '{action.label}'" +
                     (string.IsNullOrEmpty(action.turnId) ? "" : $" [turn={action.turnId}]") +
                     $": waited {itemStartTime - queuedAt:0.000}s in the queue before starting.");
            }

            // ---- TIME-SCALE GUARD ------------------------------------------------------------------
            // The queue paces itself in REAL time (Time.unscaledDeltaTime) so a slow-motion effect can no
            // longer stretch a turn. This check reports a non-default time scale anyway, because it still
            // affects the ANIMATION CLIPS: at timeScale 0.2 a 6s heavy hit plays for 30s of wall clock, and
            // the clip-end waits would then legitimately hold the queue that long. If a turn is slow and
            // this warning appears, the cause is the time scale, not the queue.
            if (Time.timeScale < 0.99f)
            {
                AlogWarn($"LATENCY [TIMESCALE] '{action.label}': Time.timeScale={Time.timeScale:0.###} while the " +
                         $"queue runs. Pacing is unscaled so the queue itself is unaffected, but animation clips " +
                         $"play {1f / Mathf.Max(0.01f, Time.timeScale):0.#}x slower, which will hold the clip waits.");
            }

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
                float applyStart = Time.unscaledTime;
                try { action.apply(); }
                catch (System.Exception ex) { AlogError($"action '{action.label}' apply threw: {ex}"); }

                // DIAGNOSTIC: brackets the apply step. This is the only code between QUEUE-START and the
                // step-in, so if the step-in reports a large "into the item" value while its OWN duration
                // is small, the time was spent here (or in the idle/locomotion gate below).
                float applyTook = Time.unscaledTime - applyStart;
                measuredWaitTotal += applyTook;
                if (applyTook > 0.1f)
                    AlogWarn($"LATENCY [APPLY-SLOW] '{action.label}': the apply step took {applyTook:0.000}s " +
                             $"({Time.unscaledTime - itemStartTime:0.000}s into the item).");
                else
                    Alog($"LATENCY [APPLY-DONE] '{action.label}': apply took {applyTook:0.000}s " +
                         $"({Time.unscaledTime - itemStartTime:0.000}s into the item).");
            }

            // 1) MOVE the attacker to the spot in front of the victim (if this item moves anyone).
            if (action.moveAttacker)
            {
                Transform attacker = GetFighter(action.attackerSide);
                Transform victim = GetFighter(action.victimSide);
                if (attacker != null && victim != null)
                {
                    // Re-read which side of the arena each fighter is on BEFORE the step, so the step is
                    // routed onto the attacker's OWN side. A fighter that ended the previous turn on the
                    // wrong side is therefore walked back home instead of further through its opponent.
                    CaptureStanceSides();

                    // A LIGHT attack walks in to attackSpotDistanceLight; a HEAVY one targets
                    // attackSpotDistanceHeavy (which is also the resting gap, so it does not move).
                    float distanceScale = DistanceScaleForAnimation(action.attackerAnimationId);

                    // The step itself: move to the spot, then square up (face the opponent) so the clip
                    // below plays from the correct position AND heading.
                    float stepStart = Time.unscaledTime;
                    yield return StepAttackerToSpot(action.attackerSide, attacker, victim, distanceScale);
                    measuredWaitTotal += Time.unscaledTime - stepStart;
                    Alog($"LATENCY [STEP-MOVE] '{action.label}': {Time.unscaledTime - itemStartTime:0.000}s into the item " +
                         $"(the step-in itself took {Time.unscaledTime - stepStart:0.000}s).");

                    // HARD GATE: do not fire the attack until the move has genuinely finished. The
                    // attacker must be AT the spot (and the animator settled) before the clip starts.
                    // MoveFighterTo lands exactly on the target, but the animator needs a frame to
                    // pick up the new pose/facing; without this gate the attack can begin from the
                    // previous pose, which is exactly the reported bug.
                    float waitForArrival = 0f;
                    while (attacker != null &&
                           Vector3.Distance(attacker.position, _currentAttackSpot) > 0.01f &&
                           waitForArrival < maxMoveSettleWait)
                    {
                        waitForArrival += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    if (verboseLogging)
                        Alog($"attacker in position after {waitForArrival:0.00}s -> firing attack.");
                }

                // Settle before firing the attack. The shipped controller authors attack/hit/getup
                // transitions ONLY from the Idle state, so firing while the model is still in Walk
                // (Speed just driven to 0) swallows the trigger -- and a swallowed trigger means no clip
                // runs, no end-of-clip event arrives, and the queue sits out its whole 15s safety cap.
                //
                // We therefore wait for the STRONGEST available condition, in order of preference:
                //   1. the bridge reporting it is LITERALLY in a standing Idle state (the real
                //      requirement of the Animator Controller), when that hook is wired;
                //   2. otherwise the locomotion-settled hook (weaker, kept for compatibility).
                // Both are bounded by maxMoveSettleWait so a stuck animator cannot hang the queue.
                if (driveMoveSpeed)
                {
                    float settleStart = Time.unscaledTime;
                    if (moveSettleDelay > 0f) yield return WaitUnscaled(moveSettleDelay);
                    else yield return null;

                    float waited = 0f;
                    while (waited < maxMoveSettleWait)
                    {
                        bool ready;
                        if (AttackerIdleReadyHandler != null)
                        {
                            // Authoritative: the animator is actually sitting in Idle, so the controller
                            // will accept the attack trigger.
                            try { ready = AttackerIdleReadyHandler(action.attackerSide); }
                            catch { ready = true; }
                        }
                        else if (LocomotionSettledHandler != null)
                        {
                            try { ready = LocomotionSettledHandler(action.attackerSide); }
                            catch { ready = true; }
                        }
                        else
                        {
                            ready = true;
                        }

                        if (ready) break;

                        waited += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    // DIAGNOSTIC: the idle/locomotion settle gate. This polls until the animator is in a
                    // state the controller accepts a trigger from; if it never gets there it runs to
                    // maxMoveSettleWait and the attack is likely to be refused by the bridge.
                    measuredWaitTotal += Time.unscaledTime - settleStart;
                    Alog($"LATENCY [STEP-SETTLE] '{action.label}': {Time.unscaledTime - itemStartTime:0.000}s into the item " +
                         $"(settle gate took {Time.unscaledTime - settleStart:0.000}s, polled {waited:0.000}s of max {maxMoveSettleWait:0.00}s).");
                }
            }

            // 2) ANIMATE the pair TOGETHER, then wait for the longer clip to finish.
            if (action.playAnimation)
            {
                // SPACING: let the step-in fully settle before the clips fire, so the attack is never
                // triggered mid-blend (which can swallow the trigger or start the clip from a snap pose).
                float preGap = ScaleSpacing(preExchangeSpacing);
                if (preGap > 0f) yield return WaitUnscaled(preGap);
                Alog($"LATENCY [STEP-ANIM] '{action.label}': {Time.unscaledTime - itemStartTime:0.000}s into the item at the start of the clip pair.");

                // The clips are about to start: this is the moment the health bar must drop, so the
                // damage lands exactly with the swing (not during the step-in, not after the clip).
                try { AnimationStartingHandler?.Invoke(); }
                catch (System.Exception ex) { AlogWarn($"animation-start handler threw: {ex}"); }

                yield return PlayPairAndWait(action.attackerSide, action.attackerAnimationId,
                                             action.victimSide, action.victimAnimationId, action.crossFade);
                measuredWaitTotal += LastClipWaitSeconds;
                Alog($"LATENCY [STEP-PAIR-DONE] '{action.label}': {Time.unscaledTime - itemStartTime:0.000}s into the item after the clip pair.");

                // SPACING: a breath between this exchange and whatever comes next, so two clips never
                // read as one continuous motion.
                float postGap = ScaleSpacing(betweenActionsSpacing);
                if (postGap > 0f) yield return WaitUnscaled(postGap);
                measuredWaitTotal += postGap;
                Alog($"LATENCY [STEP-POSTGAP] '{action.label}': {Time.unscaledTime - itemStartTime:0.000}s into the item " +
                     $"(postGap={postGap:0.000}s, spacingScale={spacingScale:0.###}).");
            }

            // DIAGNOSTIC: closes the gap between the clip pair and the post-animation chain. If the item
            // jumps a long way between STEP-POSTGAP and this line, the cost is in the 2b chain below.
            Alog($"LATENCY [PRE-CHAIN] '{action.label}': {Time.unscaledTime - itemStartTime:0.000}s into the item " +
                 $"(hasChainedGetup={action.afterAnimation != null}).");

            // DIAGNOSTIC: every branch of the 2b chain is bracketed, so the gap between PRE-CHAIN and
            // CORRECTION-ENTER can be attributed to one specific wait instead of being guessed at.
            float chainStart = Time.unscaledTime;

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
                // DIAGNOSTIC: how long this post-animation hold actually cost, and which condition ended
                // it. 'hit-cap' means the victim never reported its hit clip finished, so the hold ran to
                // the ceiling -- a direct contributor to a slow turn.
                measuredWaitTotal += afterElapsed;
                Alog($"LATENCY [POST-ANIM-HOLD] '{action.label}': held {afterElapsed:0.000}s " +
                     $"(minHold={minHold:0.00}s, cap={cap:0.00}s, victimStillPlaying={victimWasAnimated && IsSideActionPlaying(action.victimSide)}).");
                try { action.afterAnimation(); }
                catch (System.Exception ex) { AlogError($"post-animation action for '{action.label}' threw: {ex}"); }

                // Phase 2: let the chained clip (getup) actually play out before the queue advances.
                float chainedStart = Time.unscaledTime;
                yield return WaitForChainedClip(action.victimSide, action.afterAnimationSettle);
                measuredWaitTotal += Time.unscaledTime - chainedStart;
                Alog($"LATENCY [CHAINED-CLIP] '{action.label}': waiting for the chained getup clip took " +
                     $"{Time.unscaledTime - chainedStart:0.000}s ({Time.unscaledTime - itemStartTime:0.000}s into the item).");
            }

            // DIAGNOSTIC: closes the 2b chain. A large value here, with the branches above each reporting
            // a small cost, points at the idle fast-path / predicate wait rather than a specific hold.
            Alog($"LATENCY [CHAIN-DONE] '{action.label}': 2b chain took {Time.unscaledTime - chainStart:0.000}s " +
                 $"({Time.unscaledTime - itemStartTime:0.000}s into the item).");

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
                Alog($"LATENCY [PREDICATE-WAIT] '{action.label}': no predicate, waited the estimated hold {waited:0.000}s.");
            }
            else
            {
                // Precise completion drives the advance; the cap only guards against a stuck predicate.
                //
                // FLOOR: a SMALL anti-false-positive window, NOT the item's maxHold. It previously read
                // action.maxHold, which for these items is the animation safety hold (20s) clamped to the
                // cap -- so the floor came out EQUAL to the cap and the loop could not exit before the full
                // safety bound even when the predicate reported finished on the first frame. That is the
                // exact "waited the full 15.00s safety cap ... floor was 15.00s" line in the trace.
                //
                // It only needs to stop a one-frame stale "finished" from skipping the clip, so it is a
                // short fixed beat. The predicate is authoritative; the cap stays the safety bound.
                float floor = Mathf.Min(minClipCompletionFloor, safetyCap);
                float elapsed = 0f;
                while (elapsed < safetyCap)
                {
                    bool done;
                    try { done = action.isFinished(); }
                    catch { done = true; }
                    if (done && elapsed >= floor) break;

                    // Idle fast-path: nothing is animating any more -> do not sit out the floor/cap.
                    if (elapsed >= floor && BothFightersSettledFast()) break;

                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                // DIAGNOSTIC: this is the wait that was hiding the missing time. 'hit the cap' means the
                // item's precise predicate never reported finished, so the queue sat out the full safety
                // bound -- exactly the multi-second stall seen between PRE-CHAIN and CORRECTION-ENTER.
                // Logged through Alog (not AlogWarn) so it is never missed in a plain trace filter.
                if (elapsed >= safetyCap)
                {
                    Alog($"LATENCY [PREDICATE-TIMEOUT] '{action.label}': the completion predicate never " +
                         $"reported finished; waited the full {safetyCap:0.00}s safety cap " +
                         $"({Time.unscaledTime - itemStartTime:0.000}s into the item). " +
                         $"floor was {floor:0.00}s.");
                }
                else
                {
                    Alog($"LATENCY [PREDICATE-WAIT] '{action.label}': predicate reported finished after {elapsed:0.000}s " +
                         $"(floor={floor:0.00}s, cap={safetyCap:0.00}s).");
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

                // DIAGNOSTIC: timestamp entering the correction block, so the gap between the clip wait
                // and this point can be measured. This is where the unexplained time was hiding.
                    float correctionStart = Time.unscaledTime;
                    if (itemStartTime > 0f)
                        Alog($"LATENCY [CORRECTION-ENTER] '{action.label}': {correctionStart - itemStartTime:0.000}s into the item.");

                // Facing: re-aim each fighter at the other (only matters after a clip that rotated it).
                ApplyFacingAfterAnim(left, right);
                ApplyFacingAfterAnim(right, left);

                // Ground: ease each fighter's Y back to its captured height. Skipped entirely when the
                // fighter is already grounded, so an idle fighter does not pay the ease duration.
                //
                // This is the ONLY place the controller ever writes Y. While a clip played, Y was left
                // untouched so a jump / flying clip could lift the model; now that the clip is over the
                // model is put back on its ground.
                if (resetGroundYAfterAnim)
                {
                    if (left != null && !IsAtGroundY(left, GroundYForSide(PlayerUI.Side.Left)))
                        yield return SnapYToGround(left, GroundYForSide(PlayerUI.Side.Left));
                    if (right != null && !IsAtGroundY(right, GroundYForSide(PlayerUI.Side.Right)))
                        yield return SnapYToGround(right, GroundYForSide(PlayerUI.Side.Right));
                }

                if (verboseLogging)
                    Alog($"Post-anim correction done after '{action.label}' (Y regrounded).");

                measuredWaitTotal += Time.unscaledTime - correctionStart;
                Alog($"LATENCY [CORRECTION-DONE] '{action.label}': correction block took {Time.unscaledTime - correctionStart:0.000}s " +
                     $"(total {Time.unscaledTime - itemStartTime:0.000}s into the item).");
            }

            // 4b) KEEP THE FIGHTERS APART: the ONLY automatic repositioning after an exchange. It moves
            //     NOBODY when the fighters are already apart -- so a fighter that ended the exchange on
            //     the other side of the arena STAYS on that side instead of being dragged back through
            //     its opponent. When they DO overlap, each is nudged a small, clamped amount onto its own
            //     side (never past the other model).
            //
            //     WAIT FIRST: the gap-out must NOT start while a fighter is still mid-animation. With a
            //     heavy hit the victim is knocked down and gets up AFTER the attacker's clip ended, so
            //     moving straight away dragged a still-down fighter across the stage. We therefore wait
            //     until BOTH fighters are genuinely back in Idle.
            if (action.playAnimation && action.moveAttacker)
            {
                bool anyDead = IsSideDead(PlayerUI.Side.Left) || IsSideDead(PlayerUI.Side.Right);

                // A dead fighter keeps its body where it fell, but the SURVIVOR still has to be spaced
                // correctly -- otherwise the next turn's step-in starts from an overlapping pair.
                if (!anyDead)
                {
                    float settleForStance = 0f;
                    float settleForStanceCap = Mathf.Max(0f, maxHoldOvershoot + postAnimationSettleDelay);
                    while (settleForStance < settleForStanceCap)
                    {
                        // BothFightersSettledFast also accepts the authoritative end-of-clip event, so
                        // the wait ends the moment the clips are over instead of spinning until the
                        // animator is literally back in Idle (which cost the full cap on every heavy hit).
                        //
                        // It ALSO now treats a DEAD fighter as settled, which matters here: on a lethal
                        // hit the corpse parks in a looping KO pose that never reports clip-finished, so
                        // without that exemption this wait ran to its full 8.05s cap (the observed
                        // "waited 8.052s (cap=8.05s)" on the KO turn).
                        if (BothFightersSettledFast()) break;

                        // A fighter that DIED during this wait must release it immediately: its KO pose
                        // never settles, and the HP write can land a frame before the bridge's dead flag
                        // updates, so `anyDead` above may still have been false when the loop started.
                        if (IsSideDead(PlayerUI.Side.Left) || IsSideDead(PlayerUI.Side.Right)) break;

                        settleForStance += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    if (verboseLogging)
                        Alog($"waiting {settleForStance:0.00}s for both fighters to settle before re-spacing after '{action.label}'.");

                    // DIAGNOSTIC: the settle wait before re-spacing. Close to 0 means the end signal
                    // released it immediately; a large value means the fighters were still mid-clip.
                    measuredWaitTotal += settleForStance;
                    Alog($"LATENCY [RESPACE-WAIT] '{action.label}': waited {settleForStance:0.000}s " +
                         $"(cap={settleForStanceCap:0.00}s) before re-spacing.");

                    // Restores the standard gap AND re-centres the pair. This is the ONE reposition that
                    // runs after the clip, so the exchange reads as "close in -> hit -> settle back"
                    // rather than two separate walks. A single frame when already at the right spacing.
                    float respaceStart = Time.unscaledTime;
                    yield return RepositionToFightStance();
                    measuredWaitTotal += Time.unscaledTime - respaceStart;
                    Alog($"LATENCY [RESPACE-RUN] '{action.label}': RepositionToFightStance took {Time.unscaledTime - respaceStart:0.000}s " +
                         $"(duration={respaceDuration:0.###}s).");
                }
                else if (verboseLogging)
                {
                    Alog($"re-spacing SKIPPED after '{action.label}': a fighter is dead (no reposition of the body).");
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
                        // Same fast test as the re-spacing wait above: the end-of-clip event releases the
                        // camera shot immediately, instead of holding the attack framing for the whole cap
                        // while the animator merely transitions back to Idle.
                        if (BothFightersSettledFast()) break;

                        // Same death escape as the re-spacing wait: a fighter that died during this wait
                        // never settles, so release the shot instead of spinning out the cap.
                        if (IsSideDead(PlayerUI.Side.Left) || IsSideDead(PlayerUI.Side.Right)) break;

                        focusReleaseWaited += Time.unscaledDeltaTime;
                        yield return null;
                    }

                    if (verboseLogging)
                        Alog($"releasing camera attack shot after '{action.label}' (waited {focusReleaseWaited:0.00}s for both fighters to settle).");

                    // DIAGNOSTIC: the camera-release wait. This runs AFTER the re-spacing wait, so a large
                    // value here is time added on top of everything else in the item.
                    measuredWaitTotal += focusReleaseWaited;
                    Alog($"LATENCY [CAM-RELEASE-WAIT] '{action.label}': waited {focusReleaseWaited:0.000}s " +
                         $"(cap={focusReleaseCap:0.00}s) to release the attack shot.");

                    try { AttackFinishedHandler?.Invoke(); }
                    catch (System.Exception ex) { AlogWarn($"attack-finished handler threw: {ex}"); }
                }
                else if (verboseLogging)
                {
                    Alog($"camera attack shot KEPT after '{action.label}': a fighter is dead (no recentre on death).");
                }
            }

            // ---- LATENCY: total time this item held the queue (candidate C) ------------------------
            // From the moment the item started running to here. Large values mean the CHOREOGRAPHY itself
            // (move + clip + post-anim waits + spacing) is the cost -- not the backend, not the flush.
            Alog($"LATENCY [QUEUE-DONE] '{action.label}'" +
                 (string.IsNullOrEmpty(action.turnId) ? "" : $" [turn={action.turnId}]") +
                 $": ran for {Time.unscaledTime - itemStartTime:0.000}s" +
                 (queuedAt > 0f ? $", {Time.unscaledTime - queuedAt:0.000}s end-to-end from enqueue." : "."));

            // ---- LATENCY: UNACCOUNTED TIME SUMMARY ---------------------------------------------
            // Reports how much of the item's total runtime is NOT explained by the waits we measure
            // individually. A large unaccounted value points at a yield we have not instrumented yet, so
            // this line is what decides whether the search is finished.
            float measured = measuredWaitTotal;
            float total = Time.unscaledTime - itemStartTime;
            Alog($"LATENCY [SUMMARY] '{action.label}': total={total:0.000}s, measured={measured:0.000}s, " +
                 $"UNACCOUNTED={Mathf.Max(0f, total - measured):0.000}s.");

            // Settle pause before the next item runs. This is now applied UNCONDITIONALLY (scaled by the
            // master spacing) whenever a clip played: the old code skipped it exactly when both fighters
            // were idle -- i.e. after every normal exchange -- which is why animations ran back-to-back
            // with no breathing room. Stability first; tighten spacingScale later once the flow is right.
            if (action.playAnimation)
            {
                float settle = ScaleSpacing(postAnimationSettleDelay + betweenActionsSpacing);
                if (settle > 0f) yield return WaitUnscaled(settle);
            }
            else
            {
                float idleSettle = ScaleSpacing(postAnimationSettleDelay);
                // No clip played on this item, so there is nothing to cut short: the fast test is safe
                // here and skips the pause as soon as the end-of-clip event confirms both sides are done.
                if (idleSettle > 0f && !BothFightersSettledFast()) yield return WaitUnscaled(idleSettle);
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
