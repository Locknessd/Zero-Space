using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PROJECT ARCHITECTURE: Presentation / Animation Layer
/// ROLE: Robust bridge between gameplay code and a single character's Animator Controller.
/// RESPONSIBILITIES:
/// - Safely set / trigger Animator parameters (Speed, AttackIndex, TriggerAttack, HitIndex,
///   TriggerHit, IsKnockedDown, GetupType, TriggerGetup, IsDead) without leaving stale
///   triggers or conflicting one-shot flags that cause state locks or glitches.
/// - Expose a clean, intention-revealing public API: Move(), Attack(index), TakeHit(...),
///   Knockdown(), Getup(type), Die().
/// - Guard against missing parameters / missing Animator / missing controller so the game
///   never crashes when a prefab is used without a fully wired-up controller.
///
/// AI NOTE:
/// This is a *bridge*, not a state machine. Authoritative flow (Idle -> Attack -> Idle,
/// light hit inline, heavy hit -> KB_Idle_1 -> Getup -> Idle, Any-State -> KB_TopKO on death)
/// lives inside the Animator Controller. This script only feeds the parameters that the
/// controller's transitions listen for, in the correct order and with defensive resets.
///
/// EXPECTED ANIMATOR PARAMETERS (types must match exactly):
///   Speed          : Float
///   AttackIndex    : Int
///   TriggerAttack  : Trigger
///   HitIndex       : Int
///   TriggerHit     : Trigger
///   IsKnockedDown  : Bool
///   GetupType      : Int
///   TriggerGetup   : Trigger
///   IsDead         : Bool
///
/// EXPECTED STATES (by name):
///   Idle, Walk, attack3, attack5, combo_weapon 1, 6, 7,
///   hit3, hit5, hit_combo_weapon 1, 6, 7,
///   KB_Idle_1, Combo_Getup01, Combo_Getup02, KB_TopKO
/// </summary>
[DisallowMultipleComponent]
public class CharacterAnimatorBridge : MonoBehaviour
{
    public enum GetupDirection
    {
        Back = 1,
        Front = 2
    }

    /// <summary>
    /// Logical attack slots mapped to the AttackIndex values the CONTROLLER expects.
    /// NOTE: the Animator's AttackIndex numbering does NOT match the state display names. Verified
    /// against Enemy_Assistant_Controller.controller:
    ///   AttackIndex 1 -> state "attack5"
    ///   AttackIndex 3 -> state "attack3"
    ///   AttackIndex 5 -> state "combo_weapon 1"
    ///   AttackIndex 6 -> state "combo_weapon(6)"
    ///   AttackIndex 7 -> state "combo_weapon(7)"
    /// The field names describe the SLOT (which pool it belongs to), the values are the raw indices.
    /// </summary>
    public enum AttackType
    {
        Attack5 = 1,
        TwoHandedAxe = 2,
        Attack3 = 3,
        Assassin = 4,
        ComboWeapon1 = 5,
        Attack6 = 6,
        Attack7 = 7,
        DualDaggers = 8,
        Katana = 9
    }

    /// <summary>
    /// BE driving convention (per backend contract):
    ///   animationId containing "light" -> light pool
    ///   animationId containing "heavy" -> heavy pool
    /// A light attack randomly plays one of the light AttackIndex values; a heavy attack
    /// randomly plays one of the heavy AttackIndex values. Hit reactions use the matching
    /// HitIndex pool for the same weight class.
    /// </summary>
    public enum HitWeight { Light, Heavy }

    [Header("Animator")]
    [Tooltip("Animator to drive. If left empty, the Animator on this GameObject is used.")]
    [SerializeField] private Animator animator;

    [Header("Parameter Names (must match the Animator Controller)")]
    [Tooltip("Base-layer state names that count as a locomotion (Walk/Run) state. Attack/Hit/Getup are typically authored only from Idle, so we wait to leave these before firing a one-shot trigger.")]
    [SerializeField] private string walkStateName = "Enemy1_Walk";
    [SerializeField] private string runStateName = "Enemy1_Run";
    [Tooltip("Base-layer IDLE state name. The queue treats this state as \"settled\" so a getup stack only completes once the fighter is back in Idle.")]
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string speedParam = "Speed";
    [SerializeField] private string attackIndexParam = "AttackIndex";
    [SerializeField] private string triggerAttackParam = "TriggerAttack";
    [SerializeField] private string hitIndexParam = "HitIndex";
    [SerializeField] private string triggerHitParam = "TriggerHit";
    [SerializeField] private string isKnockedDownParam = "IsKnockedDown";
    [SerializeField] private string getupTypeParam = "GetupType";
    [SerializeField] private string triggerGetupParam = "TriggerGetup";
    [SerializeField] private string isDeadParam = "IsDead";

    [Header("Attack Variant Pools (AttackIndex values)")]
    [Tooltip("AttackIndex values that a LIGHT attack may randomly pick from.")]
    [SerializeField] private int[] lightAttackIndices = new int[] { 1, 3 };
    [Tooltip("AttackIndex values that a HEAVY attack may randomly pick from.")]
    [SerializeField] private int[] heavyAttackIndices = new int[] { 5, 6, 7 };

    [Header("Hit Variant Pools (HitIndex values)")]
    [Tooltip("HitIndex values that a LIGHT hit reaction may pick from (matched to the attack variant when available).")]
    [SerializeField] private int[] lightHitIndices = new int[] { 1, 3 };
    [Tooltip("HitIndex values that a HEAVY hit reaction may pick from (matched to the attack variant when available).")]
    [SerializeField] private int[] heavyHitIndices = new int[] { 5, 6, 7 };

    [Header("VFX")]
    [Tooltip("Particle system played every time this fighter plays a hit reaction (see TriggerHitEffect). Leave empty to disable.")]
    public ParticleSystem hitEffect;
    [Tooltip("Delay (seconds) before the hit effect plays on a HEAVY hit, so it lands on the impact frame instead of the wind-up. Light hits fire immediately.")]
    [SerializeField] private float heavyHitEffectDelay = 0.5f;

    [Header("Getup Type by Knockdown Hit")]
    [Tooltip("HitIndex values whose knockdown gets up with GetupType 1 (Combo_Getup01 / Back).")]
    [SerializeField] private int[] backGetupHitIndices = new int[] { 5, 6 };
    [Tooltip("HitIndex values whose knockdown gets up with GetupType 2 (Combo_Getup02 / Front).")]
    [SerializeField] private int[] frontGetupHitIndices = new int[] { 7 };
    [Tooltip("GetupType used when no knockdown-hit rule matches. 1 = Back (Combo_Getup01), 2 = Front (Combo_Getup02).")]
    [SerializeField] private int defaultGetupType = 1;

    [Header("Safety / Tuning")]
    [Tooltip("When true, verifying that every expected parameter exists on Awake and warn if not.")]
    [SerializeField] private bool validateParametersOnAwake = true;
    [Tooltip("When true, a heavy hit locks Attack/Move until Getup() is called (requires the controller to have a KD_Idle state driven by IsKnockedDown). Leave FALSE for controllers without a knockdown idle, otherwise the character would lock forever.")]
    [SerializeField] private bool knockdownBlocksActions = false;
    [Tooltip("Min seconds between two attacks to avoid double-fire from repeated calls in one frame.")]
    [SerializeField] private float attackCooldown = 0.05f;
    [Tooltip("Log state transitions and parameter writes (verbose debugging). All messages are prefixed with 'Animation'.")]
    [SerializeField] private bool verboseLogging = true;

    // ------------------------------------------------------------------
    // Logging
    // ------------------------------------------------------------------
    // Every log emitted by this bridge starts with "Animation" so the whole animation pipeline can
    // be filtered in the console with a single search string.
    private const string LogTag = "Animation";

    /// <summary>Logs an animation message prefixed with the shared "Animation" tag.</summary>
    private void Alog(string message)
    {
        Debug.Log($"{LogTag} [{name}] {message}", this);
    }

    /// <summary>Logs an animation WARNING prefixed with the shared "Animation" tag.</summary>
    private void AlogWarn(string message)
    {
        Debug.LogWarning($"{LogTag} [{name}] {message}", this);
    }

    /// <summary>Logs an animation ERROR prefixed with the shared "Animation" tag.</summary>
    private void AlogError(string message)
    {
        Debug.LogError($"{LogTag} [{name}] {message}", this);
    }

    // ---- Animator parameter hashes (resolved once) ----
    private int _speedHash;
    private int _attackIndexHash;
    private int _triggerAttackHash;
    private int _hitIndexHash;
    private int _triggerHitHash;
    private int _isKnockedDownHash;
    private int _getupTypeHash;
    private int _triggerGetupHash;
    private int _isDeadHash;

    // Capability flags: which parameters actually exist on this Animator.
    private bool _hasSpeed, _hasAttackIndex, _hasTriggerAttack;
    private bool _hasHitIndex, _hasTriggerHit, _hasIsKnockedDown;
    private bool _hasGetupType, _hasTriggerGetup, _hasIsDead;

    // ---- Per-clip trigger mode ----------------------------------------------------------------
    // Some controllers (e.g. Frank_Attacker_Master / Frank_Victim_Master) do NOT use an
    // AttackIndex/HitIndex int + a shared TriggerAttack/TriggerHit. Instead every clip is its own
    // Trigger named after the clip: attack1..attack5, hit1..hit5, idle, die. When that shape is
    // detected we drive those named triggers instead of the index parameters, otherwise the attack
    // is silently ignored (the trigger we fire simply does not exist on that controller).
    private bool _usePerClipTriggers;
    [Tooltip("Trigger name format for per-clip controllers (e.g. 'attack{0}' -> attack1..attack5).")]
    [SerializeField] private string attackTriggerFormat = "attack{0}";
    [Tooltip("Trigger name format for per-clip controllers (e.g. 'hit{0}' -> hit1..hit5).")]
    [SerializeField] private string hitTriggerFormat = "hit{0}";
    [Tooltip("Idle trigger name for per-clip controllers.")]
    [SerializeField] private string idleTriggerName = "idle";
    [Tooltip("Die parameter name for per-clip controllers (trigger or bool).")]
    [SerializeField] private string perClipDieName = "die";

    // Runtime state.
    private bool _isDead;
    private bool _isKnockedDown;
    private float _lastAttackTime = -999f;

    // The HitIndex of the heavy hit that most recently entered the knockdown. Used to derive which
    // getup direction to play (see GetupTypeForHitIndex) when GetupFromKnockdown() is called.
    private int _pendingKnockdownHitIndex;

    // Getup retry state.
    // The controller's KB_Idle_1 -> Combo_Getup transitions are authored with HasExitTime=true
    // and an ExitTime near 0.96, so a single TriggerGetup edge fired while the hit clip is still
    // playing can be consumed/expired before the transition is ever evaluated. When that happens
    // the character would stay down forever. We therefore remember the pending getup and re-fire
    // the trigger each frame until the controller actually leaves the knocked-down state.
    private bool _getupPending;
    private int _pendingGetupType;
    [Tooltip("Max seconds before giving up on a getup the controller never consumes, so the queue is never held indefinitely (safety valve).")]
    [SerializeField] private float getupRetryTimeout = 1.5f;
    private float _getupRequestTime = -999f;

    // True once the getup CLIP has actually been observed playing (a Combo_Getup state was entered).
    // Used so completion is only reported after the getup really ran, not on the first frame a
    // trigger edge is fired.
    private bool _sawGetupState;

    // Seconds elapsed since the getup request was issued. Used as a grace period before we accept
    // that the fighter has left the knockdown pose without ever entering a recognised getup clip.
    private float _getupElapsed => Time.time - _getupRequestTime;
    [Tooltip("Seconds to wait after a getup request before accepting that the fighter has recovered even if no Combo_Getup* clip was observed (safety net for controllers with differently-named getup clips).")]
    [SerializeField] private float getupSettleGrace = 0.25f;

    /// <summary>
    /// Raised when the getup has actually been consumed by the controller (the fighter is back on
    /// its feet). Subscribers use this to recompute choreography that depends on the victim's
    /// facing, which can change while it stands up.
    /// </summary>
    public event Action GetupCompleted;

    // Well-known state name hashes for diagnostics / best-effort checks.
    private static readonly string[] KnownStateNames =
    {
        "Idle", "Walk",
        "attack3", "attack5", "Damage_Critical_GreatSword", "Damage_Critical_Spear", "Damage_Critical_Warrior",
        "hit3", "hit5", "Damage_Critical_GreatSword_Hit", "Damage_Critical_Spear_Hit", "Damage_Critical_Warrior_Hit",
        "KB_Idle_1", "Combo_Getup01", "Combo_Getup02", "KB_TopKO"
    };

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null)
        {
            AlogError("No Animator found. Bridge will be inert.");
            enabled = false;
            return;
        }

        ResolveParameterHashes();
        if (validateParametersOnAwake) ValidateParameters();

        // Ensure a deterministic starting state: not dead, not knocked down, no residual triggers.
        ResetRuntimeState();
    }

    private void ResolveParameterHashes()
    {
        _speedHash = Animator.StringToHash(speedParam);
        _attackIndexHash = Animator.StringToHash(attackIndexParam);
        _triggerAttackHash = Animator.StringToHash(triggerAttackParam);
        _hitIndexHash = Animator.StringToHash(hitIndexParam);
        _triggerHitHash = Animator.StringToHash(triggerHitParam);
        _isKnockedDownHash = Animator.StringToHash(isKnockedDownParam);
        _getupTypeHash = Animator.StringToHash(getupTypeParam);
        _triggerGetupHash = Animator.StringToHash(triggerGetupParam);
        _isDeadHash = Animator.StringToHash(isDeadParam);

        _hasSpeed = HasParameter(speedParam, _speedHash, AnimatorControllerParameterType.Float);
        _hasAttackIndex = HasParameter(attackIndexParam, _attackIndexHash, AnimatorControllerParameterType.Int);
        _hasTriggerAttack = HasParameter(triggerAttackParam, _triggerAttackHash, AnimatorControllerParameterType.Trigger);
        _hasHitIndex = HasParameter(hitIndexParam, _hitIndexHash, AnimatorControllerParameterType.Int);
        _hasTriggerHit = HasParameter(triggerHitParam, _triggerHitHash, AnimatorControllerParameterType.Trigger);
        _hasIsKnockedDown = HasParameter(isKnockedDownParam, _isKnockedDownHash, AnimatorControllerParameterType.Bool);
        _hasGetupType = HasParameter(getupTypeParam, _getupTypeHash, AnimatorControllerParameterType.Int);
        _hasTriggerGetup = HasParameter(triggerGetupParam, _triggerGetupHash, AnimatorControllerParameterType.Trigger);
        _hasIsDead = HasParameter(isDeadParam, _isDeadHash, AnimatorControllerParameterType.Bool);

        // Detect the per-clip trigger convention: a controller that has NO AttackIndex/TriggerAttack
        // but DOES have a trigger literally named "attack1" drives attacks with one trigger per clip.
        _usePerClipTriggers = !_hasTriggerAttack && HasTriggerByName(string.Format(attackTriggerFormat, 1));

        _parametersResolved = true;
    }

    /// <summary>
    /// Resolves a per-clip trigger index that actually exists on this controller.
    /// Tries the requested index first; if that clip is missing (e.g. a fighter with only
    /// attack1..attack5 but the heavy pool asks for 6/7), it falls back to the nearest EXISTING
    /// index, preferring the same number, then any index that has a clip (scanned from 1 upward).
    /// Returns 0 when no clip exists at all.
    /// </summary>
    private int ResolvePerClipIndex(string format, int requestedIndex)
    {
        if (requestedIndex > 0 && HasTriggerByName(string.Format(format, requestedIndex)))
            return requestedIndex;

        // Scan a reasonable range for the first existing clip trigger.
        for (int i = 1; i <= 8; i++)
            if (HasTriggerByName(string.Format(format, i)))
                return i;

        return 0;
    }

    /// <summary>True when the Animator declares a Trigger parameter with exactly this name.</summary>
    private bool HasTriggerByName(string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName)) return false;
        int hash = Animator.StringToHash(triggerName);
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger &&
                (p.nameHash == hash || string.Equals(p.name, triggerName, StringComparison.Ordinal)))
                return true;
        return false;
    }

    // Whether ResolveParameterHashes() has run against a populated Animator. Guards against the
    // common case where Awake runs before the runtime AnimatorController is assigned, leaving the
    // capability flags all false -- which silently drops every parameter write (e.g. AttackIndex).
    private bool _parametersResolved;

    /// <summary>
    /// Lazily (re)resolves the parameter capability flags the first time a write is attempted if they
    /// were never resolved against a populated controller. This makes the bridge robust to Animator /
    /// runtime-controller assignment order, which otherwise causes parameters (AttackIndex, etc.) to
    /// never be sent at runtime.
    /// </summary>
    private void EnsureParametersResolved()
    {
        if (_parametersResolved) return;
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null) return;

        ResolveParameterHashes();
        if (verboseLogging) Alog("Parameters resolved lazily (animator assigned after Awake).");
    }

    private void ValidateParameters()
    {
        var missing = new List<string>();
        if (!_hasSpeed) missing.Add($"{speedParam} (Float)");
        if (!_hasAttackIndex) missing.Add($"{attackIndexParam} (Int)");
        if (!_hasTriggerAttack) missing.Add($"{triggerAttackParam} (Trigger)");
        if (!_hasHitIndex) missing.Add($"{hitIndexParam} (Int)");
        if (!_hasTriggerHit) missing.Add($"{triggerHitParam} (Trigger)");
        if (!_hasIsKnockedDown) missing.Add($"{isKnockedDownParam} (Bool)");
        if (!_hasGetupType) missing.Add($"{getupTypeParam} (Int)");
        if (!_hasTriggerGetup) missing.Add($"{triggerGetupParam} (Trigger)");
        if (!_hasIsDead) missing.Add($"{isDeadParam} (Bool)");

        if (missing.Count > 0)
        {
            AlogWarn("missing/mistyped Animator parameters:\n  - " + string.Join("\n  - ", missing));
        }
        else if (verboseLogging)
        {
            Alog("all parameters validated.");
        }

        // Always log the resolved capability flags + the animator's real parameter list, so a runtime
        // "the index is never sent" report can be traced to a missing/mistyped parameter immediately.
        if (animator != null)
        {
            var names = new List<string>();
            foreach (var p in animator.parameters) names.Add($"{p.name}:{p.type}");
            string controllerName = animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name
                : "(none)";
            Alog(
                $"capability:\n" +
                $"  animator GameObject='{animator.gameObject.name}', controller='{controllerName}'\n" +
                $"  mode={( _usePerClipTriggers ? "PER-CLIP (attackN/hitN triggers)" : "INDEX (AttackIndex+TriggerAttack)")}\n" +
                $"  Speed={_hasSpeed}, AttackIndex={_hasAttackIndex}, TriggerAttack={_hasTriggerAttack}, " +
                $"HitIndex={_hasHitIndex}, TriggerHit={_hasTriggerHit}, IsKnockedDown={_hasIsKnockedDown}, " +
                $"GetupType={_hasGetupType}, TriggerGetup={_hasTriggerGetup}, IsDead={_hasIsDead}\n" +
                $"  animator parameters: [{string.Join(", ", names)}]");
        }
    }

    private void ResetRuntimeState()
    {
        _isDead = false;
        _isKnockedDown = false;

        // Clear residual one-shot triggers so the graph starts clean.
        if (_hasTriggerAttack) animator.ResetTrigger(_triggerAttackHash);
        if (_hasTriggerHit) animator.ResetTrigger(_triggerHitHash);
        if (_hasTriggerGetup) animator.ResetTrigger(_triggerGetupHash);

        if (_hasIsDead) animator.SetBool(_isDeadHash, false);
        if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, false);
        if (_hasSpeed) animator.SetFloat(_speedHash, 0f);
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Drives the locomotion blend. value is typically 0..1 (Idle -> Walk), clamped defensively.
    /// No-op when dead or knocked down so death/knockdown poses are not disrupted.
    /// </summary>
    public void Move(float value)
    {
        EnsureParametersResolved();
        if (!CanAct()) return;
        if (!_hasSpeed) return;

        animator.SetFloat(_speedHash, Mathf.Clamp01(value));
        if (verboseLogging) Alog($"Move(Speed={value:0.##})");
    }

    /// <summary>
    /// True when the base layer is NOT in a locomotion (Walk/Run) blend, i.e. it is safe to fire a
    /// one-shot trigger (Attack / Hit / Getup).
    ///
    /// This matters because in the shipped controller (Enemy_Assistant_Controller) the attack/hit/
    /// getup transitions are authored ONLY from the Idle state. If the model is still in Walk (Speed
    /// was driven by a move) the trigger has no valid transition and is silently swallowed.
    /// The caller can poll this after a move before firing the attack.
    /// </summary>
    public bool IsLocomotionSettled()
    {
        if (animator == null) return true;
        if (!_hasSpeed) return true;

        // Settled when the animator reads Speed ~0 AND is no longer in a walk/run-named state.
        try
        {
            float speed = animator.GetFloat(_speedHash);
            if (speed > 0.1f) return false;

            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.IsName(walkStateName) || info.IsName(runStateName)) return false;
        }
        catch { }
        return true;
    }

    /// <summary>
    /// True when the base layer is currently sitting in a real standing idle pose. Used by the queue
    /// to hold a getup item until the fighter has FULLY stood back up and returned to Idle before the
    /// next queued stack is allowed to run.
    ///
    /// IMPORTANT: the knockdown idle (KB_Idle_1) is deliberately NOT counted as idle. Treating it as
    /// "idle" made the queue believe a knocked-down fighter had already settled, so the pending getup
    /// item could be marked finished while the character was still lying down -- which is exactly how
    /// the fighter ended up stuck in the hit / knockdown pose.
    /// </summary>
    public bool IsInIdleState()
    {
        if (animator == null) return true;

        // While an action we initiated is still in flight, this is NOT settled -- even if the animator
        // has not evaluated its transition yet and still reports Idle. Without this gate the camera
        // (and the queue) saw "idle" for the first frames of every attack and snapped back to centre
        // mid-swing.
        if (_actionInFlight) return false;

        try
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            // Standing idles only. KB_Idle_1 (knocked down) is explicitly excluded.
            return info.IsName("Idle") || info.IsName("Enemy1_Idle") || info.IsName(idleStateName);
        }
        catch { return true; }
    }

    /// <summary>
    /// Convenience: true when this fighter is standing idle AND not knocked down / not mid-getup. The
    /// queue uses this to know a previous getup has fully settled back into Idle.
    /// </summary>
    public bool IsIdleAndSettled => IsInIdleState() && !_isKnockedDown && !_getupPending;

    /// <summary>
    /// STRICT version of <see cref="IsIdleAndSettled"/> for the camera: true ONLY when the animator is
    /// literally playing the Idle state right now, no action is in flight, and the fighter is neither
    /// knocked down nor mid-getup.
    ///
    /// The looser check above returned true during the transition frames of a hit / knockdown too (the
    /// animator still reported Idle for a frame or two), which made the camera snap back to centre while
    /// a heavy hit reaction was still on screen.
    /// </summary>
    public bool IsFullyIdleNow
    {
        get
        {
            if (_isKnockedDown || _getupPending || _isDead) return false;
            if (animator == null) return false;

            try
            {
                var info = animator.GetCurrentAnimatorStateInfo(0);

                // Must literally BE the standing idle state -- not merely "not in a hit pose". This is
                // checked BEFORE the in-flight flag, because the flag is only released once we can see
                // the animator has genuinely settled into Idle.
                bool inIdle = info.IsName("Idle") || info.IsName("Enemy1_Idle") || info.IsName(idleStateName);
                if (!inIdle)
                {
                    // Still mid-clip (attack / hit / knockdown / getup): nothing is settled yet.
                    return false;
                }

                // The animator is in Idle now: the previously issued action has fully landed, so it is
                // safe to release the in-flight flag for good.
                _actionInFlight = false;

                // And the idle clip must be looping along normally (not a one-shot tail of something else).
                return info.normalizedTime > 0.01f || info.loop;
            }
            catch { return false; }
        }
    }

    // True from the moment an action is issued until the animator has genuinely settled back into a
    // standing Idle state. This is what makes IsInIdleState() honest during the transition frames, when
    // the animator still reports Idle because it has not evaluated the new transition yet.
    //
    // It is released INSIDE IsFullyIdleNow when the animator is observed in Idle -- NOT on a state exit.
    // A state exit also happens when a hit reaction hands off to the knockdown pose, so releasing it
    // there made a still-down fighter count as settled.
    private bool _actionInFlight;

    /// <summary>True while an issued action (attack / hit / getup) has not yet reported its clip end.</summary>
    public bool IsActionInFlight => _actionInFlight;

    /// <summary>
    /// Fires a specific attack. index maps to AttackIndex values (1, 3, 5, 6, 7).
    /// Sets AttackIndex then the TriggerAttack trigger. Trigger is reset first to guarantee
    /// a fresh edge (prevents a swallowed attack when a previous trigger was left set).
    /// </summary>
    public void Attack(int index)
    {
        EnsureParametersResolved();
        // A new attack owns the end signal: drop any stale flag from the previous state exit, and mark
        // the fighter as BUSY so nothing treats it as settled during the transition frames.
        BeginAction();
        Alog($"Attack({index}) ENTER (isDead={_isDead}, isKnockedDown={_isKnockedDown}, perClip={_usePerClipTriggers})");

        if (!CanAct())
        {
            AlogWarn($"Attack({index}) BLOCKED: CanAct()=false (isDead={_isDead}, isKnockedDown={_isKnockedDown}).");
            return;
        }
        if (!IsValidAttackIndex(index))
        {
            AlogWarn($"Attack({index}) is not in the light {PoolToString(lightAttackIndices)} or heavy {PoolToString(heavyAttackIndices)} pool.");
            return;
        }

        // Cooldown guard against accidental double-fire within a single frame.
        if (Time.time - _lastAttackTime < attackCooldown)
        {
            Alog($"Attack({index}) skipped: within cooldown ({attackCooldown:0.###}s).");
            return;
        }
        _lastAttackTime = Time.time;

        // Force the locomotion blend to a stop before attacking (harmless when the controller has no
        // Speed parameter -- the write is simply skipped).
        if (_hasSpeed) animator.SetFloat(_speedHash, 0f);

        // PER-CLIP mode: the controller has one Trigger per clip. Fire it, falling back to an existing
        // index if the requested one has no clip on this controller (e.g. a fighter with only
        // attack1..attack5 but the heavy pool asks for index 6/7).
        if (_usePerClipTriggers)
        {
            int resolved = ResolvePerClipIndex(attackTriggerFormat, index);
            string triggerName = resolved > 0 ? string.Format(attackTriggerFormat, resolved) : null;
            if (triggerName != null && FireTriggerByName(triggerName))
            {
                if (verboseLogging) Alog($"Attack(index={index} -> clip {resolved}) fired per-clip Trigger '{triggerName}'.");
            }
            else
            {
                AlogError($"per-clip controller has no usable '{attackTriggerFormat}' trigger. " +
                          $"Check attackTriggerFormat and the clip trigger names on the controller.");
            }
            return;
        }

        // INDEX mode: AttackIndex (Int) + a shared TriggerAttack.
        if (!_hasTriggerAttack)
        {
            AlogError($"Animator has neither Trigger '{triggerAttackParam}' nor a per-clip '{string.Format(attackTriggerFormat, 1)}' trigger. " +
                      $"Set the parameter names to match the Animator Controller.");
            return;
        }

        if (_hasAttackIndex)
        {
            animator.SetInteger(_attackIndexHash, index);
        }
        else
        {
            AlogError($"cannot set '{attackIndexParam}' (Animator parameter missing or wrong type). " +
                      $"The AttackIndex will NOT be passed and the attack transition cannot match.");
        }
        FireTrigger(_hasTriggerAttack, _triggerAttackHash, triggerAttackParam);

        // PROOF: read the value back from the Animator we are driving. If it does not equal `index`,
        // the Animator instance we hold is not the one the gameplay code thinks it is (e.g. a damage
        // sub-object's Animator), which is exactly how the index can appear "not sent" at runtime.
        int readBack = -999;
        try { readBack = animator.GetInteger(_attackIndexHash); } catch { }
        Alog($"Attack(index={index}) fired. AttackIndex readback={readBack} " +
             $"(animator='{animator.name}', hasTrigger={_hasTriggerAttack}, hasIndex={_hasAttackIndex})");
    }

    /// <summary>
    /// Convenience overload for the enum-based attack slots.
    /// </summary>
    public void Attack(AttackType attack)
    {
        Attack((int)attack);
    }

    /// <summary>
    /// Fires an attack using the BE-supplied animationId string (e.g. "bot_a_attack_light"
    /// or "bot_a_attack_heavy"). The weight class is inferred from the string ("heavy" ->
    /// heavy pool, otherwise light) and a random AttackIndex is picked from the matching pool.
    /// Returns the AttackIndex that was applied (0 when the call was rejected).
    /// </summary>
    public int AttackFromAnimationId(string animationId)
    {
        var weight = ClassifyWeight(animationId);
        int index = PickFromPool(weight == HitWeight.Heavy ? heavyAttackIndices : lightAttackIndices);

        // Always-on trace so a "the attack never fires" report can be pinned to this call site.
        Alog($"AttackFromAnimationId('{animationId}') weight={weight} -> index={index}");

        if (index == 0)
        {
            AlogWarn($"no attack variants configured for weight={weight} ('{animationId}').");
            return 0;
        }
        Attack(index);
        return index;
    }

    /// <summary>
    /// Plays a hit reaction using the BE-supplied animationId string (e.g. "bot_a_attack_light"
    /// or "bot_a_attack_heavy").
    ///
    /// HitIndex is driven by the ATTACK, not randomized: when <paramref name="matchedAttackIndex"/>
    /// is non-zero, that exact index is used so the hit reaction visually pairs with the attack
    /// variant (e.g. attack 6 -> hit 6). Only when no attack index is supplied (0) does it fall back
    /// to a random pick from the matching weight pool. Weight is inferred from the string
    /// ("heavy" -> heavy/knockdown); heavy hits additionally enter the knockdown state.
    /// Returns the HitIndex that was applied (0 when the call was rejected).
    /// </summary>
    public int TakeHitFromAnimationId(string animationId, int matchedAttackIndex = 0)
    {
        var weight = ClassifyWeight(animationId);
        bool isHeavy = weight == HitWeight.Heavy;
        int[] pool = isHeavy ? heavyHitIndices : lightHitIndices;

        // Primary path: mirror the attack's variant exactly (no randomization).
        // Fallback path: no attack index available -> random pick from the weight pool.
        int index = matchedAttackIndex != 0 ? matchedAttackIndex : PickFromPool(pool);

        if (index == 0)
        {
            AlogWarn($"no hit variants configured for weight={weight} ('{animationId}').");
            return 0;
        }
        TakeHit(index, isHeavy);
        return index;
    }

    /// <summary>
    /// Plays a hit reaction using an explicit attack-derived index (no animationId string,
    /// no randomization). Use this when the caller already knows the attack variant the attacker
    /// played and wants the target's hit reaction to mirror it. Heavy hits enter the knockdown state.
    /// Returns the HitIndex that was applied (0 when the call was rejected).
    /// </summary>
    public int TakeHitByAttackIndex(int attackIndex, bool isHeavy, string animationIdForWeight = null)
    {
        if (attackIndex == 0)
        {
            AlogWarn("TakeHitByAttackIndex called with index 0.");
            return 0;
        }

        // If an animationId is supplied, let it refine the weight class ("heavy" -> heavy).
        if (!string.IsNullOrEmpty(animationIdForWeight))
        {
            isHeavy = ClassifyWeight(animationIdForWeight) == HitWeight.Heavy;
        }

        TakeHit(attackIndex, isHeavy);
        return attackIndex;
    }

    /// <summary>
    /// Plays a hit reaction. Light hits (hit3 / hit5) return to Idle inline. Heavy hits
    /// (hit_combo_weapon 1 / 6 / 7) play their heavier reaction; the controller decides where
    /// they return to.
    ///
    /// A LETHAL hit (the last HP) must NOT go through here -- use <see cref="TakeFatalHit"/> so
    /// the character collapses into KB_TopKO instead of a normal hit reaction.
    ///
    /// Note on knockdown: IsKnockedDown is set for heavy hits so a controller that authors a
    /// knockdown idle (driven by IsKnockedDown) can react. The character is only *locked* from
    /// further actions when <see cref="knockdownBlocksActions"/> is enabled -- otherwise (the
    /// default, and the case for a controller without a knockdown idle) actions keep flowing.
    /// </summary>
    /// <param name="hitIndex">Maps to HitIndex (e.g. 1, 3, 5, 6, 7).</param>
    /// <param name="isHeavy">True for heavy/knockdown hits, false for light inline reactions.</param>
    public void TakeHit(int hitIndex, bool isHeavy)
    {
        EnsureParametersResolved();
        if (_isDead) return; // Death has highest priority; ignore hits once dead.

        // A new hit reaction owns the end signal: drop any stale flag from the previous state exit,
        // otherwise the queue sees "already finished" and fires the getup before this hit has played.
        BeginAction();

        if (!IsValidHitIndex(hitIndex, isHeavy))
        {
            AlogWarn($"TakeHit({hitIndex}, heavy={isHeavy}) is not in the expected hit pool.");
            return;
        }

        // Force locomotion to a stop first: the controller authors hit transitions only from Idle,
        // so a hit fired while still in Walk would be swallowed (same reason as Attack).
        if (_hasSpeed) animator.SetFloat(_speedHash, 0f);

        // VFX: play the hit effect every time a hit reaction starts (no-op when none is assigned).
        //
        // A HEAVY hit waits `heavyHitEffectDelay` first, so the effect lands on the IMPACT frame of the
        // heavy reaction rather than on its wind-up. A LIGHT hit fires immediately.
        if (isHeavy && heavyHitEffectDelay > 0f)
        {
            StartCoroutine(TriggerHitEffectDelayed(heavyHitEffectDelay));
        }
        else
        {
            TriggerHitEffect();
        }

        if (isHeavy)
        {
            // Flag the knockdown for controllers that author a knockdown idle.
            if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, true);
            if (knockdownBlocksActions) _isKnockedDown = true;

            // Remember which heavy hit knocked us down so the getup direction can be derived from it
            // (hit 5/6 -> GetupType 1, hit 7 -> GetupType 2 by default; see GetupTypeForHitIndex).
            _pendingKnockdownHitIndex = hitIndex;
        }

        // PER-CLIP mode: one Trigger per clip (hit1..hit5), with the same fallback as attacks.
        if (_usePerClipTriggers)
        {
            int resolved = ResolvePerClipIndex(hitTriggerFormat, hitIndex);
            string triggerName = resolved > 0 ? string.Format(hitTriggerFormat, resolved) : null;
            if (triggerName != null && FireTriggerByName(triggerName))
            {
                if (verboseLogging) Alog($"TakeHit(index={hitIndex} -> clip {resolved}, heavy={isHeavy}) fired per-clip Trigger '{triggerName}'.");
            }
            else
            {
                AlogError($"per-clip controller has no usable '{hitTriggerFormat}' trigger. " +
                          $"Check hitTriggerFormat and the clip trigger names on the controller.");
            }
            return;
        }

        // INDEX mode.
        if (_hasHitIndex) animator.SetInteger(_hitIndexHash, hitIndex);
        FireTrigger(_hasTriggerHit, _triggerHitHash, triggerHitParam);

        int hitReadBack = -999;
        try { hitReadBack = animator.GetInteger(_hitIndexHash); } catch { }
        Alog($"TakeHit(index={hitIndex}, heavy={isHeavy}) fired. HitIndex readback={hitReadBack} (animator='{animator.name}')");
    }

    /// <summary>
    /// Convenience: light hit reaction (inline, auto-returns to Idle).
    /// </summary>
    public void TakeLightHit(int hitIndex)
    {
        TakeHit(hitIndex, false);
    }

    /// <summary>
    /// Convenience: heavy hit reaction (forces transition into KB_Idle_1 / knockdown idle).
    /// </summary>
    public void TakeHeavyHit(int hitIndex)
    {
        TakeHit(hitIndex, true);
    }

    /// <summary>
    /// Handles a hit that kills the character (hpAfter reached 0).
    ///
    /// Instead of playing a normal hit reaction (hit3/hit5) or entering the knockdown/getup
    /// flow, this forces the permanent death pose (KB_TopKO) via IsDead. This covers BOTH cases:
    ///   - a light hit that drained the last HP,
    ///   - a heavy hit that drained the last HP (the character stays down; no getup).
    /// Death is terminal and idempotent, so a lethal follow-up hit is ignored once dead.
    /// </summary>
    public void TakeFatalHit()
    {
        if (_isDead) return;   // already down; stay down
        Die();
    }

    /// <summary>
    /// Maps a knockdown-producing HitIndex to the getup direction the controller should play.
    /// By default hit 5/6 get up with GetupType 1 (Combo_Getup01 / Back) and hit 7 with GetupType 2
    /// (Combo_Getup02 / Front). The mapping is configurable via backGetupHitIndices/frontGetupHitIndices.
    /// Returns the Inspector default getupType when no rule matches.
    /// </summary>
    public int GetupTypeForHitIndex(int hitIndex)
    {
        if (PoolContains(frontGetupHitIndices, hitIndex)) return (int)GetupDirection.Front; // 2
        if (PoolContains(backGetupHitIndices, hitIndex)) return (int)GetupDirection.Back;   // 1
        return defaultGetupType;
    }

    // TEMPORARY: force every getup to type 1 (Back / Combo_Getup01). Some knockdown hits (5/6/7) were
    // not reliably leaving the hit state, so the fight could get stuck in a hit pose and never get
    // back to Idle. Until the per-hit getup transitions are verified, always use type 1.
    [Tooltip("TEMP: when true, ALL getups use forceGetupType (default 1) instead of the per-hit mapping, so a fighter can never get stuck in a hit pose.")]
    [SerializeField] private bool forceGetupType = true;
    [Tooltip("GetupType used while forceGetupType is on. 1 = Back (Combo_Getup01).")]
    [SerializeField] private int forcedGetupTypeValue = 1;

    /// <summary>
    /// Performs the getup that matches the heavy hit which knocked this fighter down.
    ///
    /// TEMPORARY BEHAVIOUR: while <see cref="forceGetupType"/> is on (the default), every getup uses
    /// <see cref="forcedGetupTypeValue"/> (1 / Back) regardless of the knockdown hit, so the fighter
    /// always leaves the hit pose and returns to Idle. Turn it off to restore the per-hit mapping
    /// (hit 5/6 -> type 1, hit 7 -> type 2).
    /// </summary>
    public void GetupFromKnockdown()
    {
        int type;
        if (forceGetupType)
        {
            type = forcedGetupTypeValue;
        }
        else
        {
            type = _pendingKnockdownHitIndex > 0
                ? GetupTypeForHitIndex(_pendingKnockdownHitIndex)
                : defaultGetupType;
        }
        if (verboseLogging) Alog($"GetupFromKnockdown -> type={type} (forced={forceGetupType}, knockdown hit={_pendingKnockdownHitIndex})");
        Getup(type);
    }

    /// <summary>
    /// Explicitly forces the knockdown state without a specific hit reaction
    /// (useful if a knockdown is triggered by something other than a hit event).
    /// </summary>
    public void Knockdown()
    {
        if (_isDead) return;
        if (knockdownBlocksActions) _isKnockedDown = true;
        if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, true);
        if (verboseLogging) Alog("Knockdown()");
    }

    /// <summary>
    /// Performs a getup from KB_Idle_1. type selects the animation:
    ///   1 (Back)  -> Combo_Getup01
    ///   2 (Front) -> Combo_Getup02
    /// Clears IsKnockedDown and fires TriggerGetup so the controller transitions out of
    /// the knockdown idle and loops back to Idle on completion.
    /// </summary>
    public void Getup(int type)
    {
        EnsureParametersResolved();
        if (_isDead) return;

        // The getup owns the end signal from now on.
        BeginAction();

        if (type != (int)GetupDirection.Back && type != (int)GetupDirection.Front)
        {
            AlogWarn($"Getup({type}) must be 1 (Back) or 2 (Front).");
            return;
        }
        if (!_hasTriggerGetup)
        {
            // Per-clip controllers (e.g. Frank_*_Master) have no knockdown/getup graph: their hit
            // clips return to Idle on their own, so a getup request is simply a no-op here. Do NOT
            // log an error for them, or every heavy hit would spam the console.
            if (_usePerClipTriggers)
            {
                if (verboseLogging) Alog($"Getup(type={type}) ignored: per-clip controller has no '{triggerGetupParam}'.");
                return;
            }
            AlogError($"Animator has no Trigger '{triggerGetupParam}'. Set the parameter name to match the Animator Controller.");
            return;
        }

        // Record the request so the per-frame retry can re-assert it if the controller does not
        // react to the first trigger edge (see the retry note on _getupPending above).
        // NOTE: a NEW getup resets the re-fire window; a repeated getup for an already-pending request
        // does NOT (otherwise a caller re-issuing getup every frame would keep the window open forever).
        if (!_getupPending)
        {
            _getupCallCount = 0;
            _loggedWindowExpired = false;
            _getupRequestTime = Time.time;
        }
        _getupCallCount++;
        _getupPending = true;
        _pendingGetupType = type;

        ApplyGetup();

        if (verboseLogging) Alog($"Getup(type={type}) fired (TriggerGetup={_hasTriggerGetup}, GetupType={_hasGetupType}, IsKnockedDownParam={_hasIsKnockedDown}).");
    }

    /// <summary>
    /// Fires the getup trigger and LOWERS IsKnockedDown.
    ///
    /// IMPORTANT (why the flag is cleared HERE, not kept up):
    /// The shipped controller's getup transitions (Combo_Getup01 / Combo_Getup02) are authored with
    /// conditions on TriggerGetup + GetupType ONLY -- they are NOT gated on IsKnockedDown. Nothing in
    /// the animator graph ever drives IsKnockedDown back to false, so if the script kept the flag up
    /// until "the animator reports not knocked down", that condition could never become true and the
    /// getup would deadlock (the character stayed in the hit/KB pose forever).
    /// Clearing the flag together with firing the trigger mirrors reality -- the fighter is standing
    /// up right now -- and lets Update() judge completion from the ACTUAL animator state instead.
    /// </summary>
    private void ApplyGetup()
    {
        // Stop locomotion so the getup transition is not blocked by a lingering Walk/Run blend.
        if (_hasSpeed) animator.SetFloat(_speedHash, 0f);

        // Select the getup direction first, then fire the trigger.
        if (_hasGetupType) animator.SetInteger(_getupTypeHash, _pendingGetupType);

        // Lower the knockdown flag BEFORE firing so any state/transition that reads it is already
        // consistent, and so Update()'s completion check below can no longer deadlock.
        if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, false);
        _isKnockedDown = false;

        FireTrigger(_hasTriggerGetup, _triggerGetupHash, triggerGetupParam);
    }

    /// <summary>
    /// Drives the pending getup to completion, frame by frame:
    ///   1) re-assert TriggerGetup while the animator has not reacted yet,
    ///   2) watch the ACTUAL animator state (not a bool the graph never clears),
    ///   3) report completion once the getup clip has played and the fighter is back in an idle pose.
    ///
    /// Completion is judged from state names so it cannot deadlock: we finish when EITHER
    ///   - a getup clip (Combo_Getup*) has been seen AND/OR the animator is back in an idle pose, or
    ///   - the animator has left the hit/knockdown pose and settled on something other than the
    ///     state we started from.
    /// </summary>
    private void Update()
    {
        if (!_getupPending) return;

        // Safety valve: never spin forever if the controller has no getup states authored.
        if (Time.time - _getupRequestTime > getupRetryTimeout)
        {
            // Give up on the trigger but still release the knockdown flag so the fighter is not
            // locked out of future actions.
            _getupPending = false;
            _sawGetupState = false;
            _getupCallCount = 0;
            _loggedWindowExpired = false;
            _isKnockedDown = false;
            if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, false);
            if (verboseLogging)
                AlogWarn($"Getup(type={_pendingGetupType}) was never consumed by the controller within {getupRetryTimeout:0.##}s; giving up and clearing knockdown.");
            return;
        }

        // Inspect the real base-layer state. This is the authoritative signal -- the animator graph
        // never writes IsKnockedDown, so completion must be derived from states.
        bool inGetupClip = false;
        bool inKnockdownPose = false;
        bool inIdlePose = false;
        bool inHitPose = false;
        try
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            inGetupClip = info.IsName("Combo_Getup01") || info.IsName("Combo_Getup02");
            inKnockdownPose = info.IsName("KB_Idle_1");
            inIdlePose = IsInIdleState();
            // Recognise the authored hit-reaction poses (hit3 / hit5 / hit_combo_weapon*) so we can tell
            // "still on the ground in a hit pose" from "has got back up into a normal state".
            inHitPose = info.IsName("hit3") || info.IsName("hit5") ||
                        info.IsName("hit_combo_weapon 1") || info.IsName("hit_combo_weapon(2)") ||
                        info.IsName("hit_combo_weapon(4)") || info.IsName("hit_combo_weapon(5)") ||
                        info.IsName("hit_combo_weapon(6)") || info.IsName("hit_combo_weapon(7)") ||
                        info.IsName("hit_combo_weapon(8)") || info.IsName("hit_combo_weapon(9)");
        }
        catch { }

        if (inGetupClip) _sawGetupState = true;

        if (_sawGetupState)
        {
            // The getup clip ran; it is done once the animator has left it (settled on idle or any
            // other state).
            if (!inGetupClip)
            {
                CompleteGetup();
                return;
            }
        }
        else if (!inHitPose && !inKnockdownPose && !inGetupClip)
        {
            // The fighter is NOT parked in any hit / knockdown pose and no getup clip is playing, so it
            // is back on its feet. This is the important escape hatch: the shipped controller often
            // parks in a hit_combo_weapon pose (NOT KB_Idle_1) after a heavy hit, so requiring an exact
            // "Idle" name left the getup pending until the 1.5s timeout every time -- the original stall.
            //
            // A short grace keeps us from completing on the single frame between firing the trigger and
            // the animator actually entering the getup clip.
            if (_getupElapsed > getupSettleGrace || inIdlePose)
            {
                CompleteGetup();
                return;
            }
        }

        // Re-assert the trigger ONLY during a short initial window. The shipped controller consumes the
        // trigger within a frame or two; continuing to re-fire after that is what produced the long spam
        // of "not yet applied" logs and (because the trigger is reset+set every frame) actively PREVENTED
        // the getup transition from ever being evaluated. Once the window has passed we simply watch the
        // state and let the normal idle-settle branch above finish the job.
        if (!_sawGetupState && _getupElapsed <= getupRetryWindow)
        {
            if (verboseLogging)
            {
                string stateName = "?";
                try { stateName = animator.GetCurrentAnimatorStateInfo(0).IsName("") ? "?" : DescribeCurrentState(); }
                catch { }
                Alog($"Getup(type={_pendingGetupType}) not yet applied (inKnockdownPose={inKnockdownPose}, state='{stateName}', " +
                     $"elapsed={_getupElapsed:0.###}/{getupRetryWindow:0.###}, timeScale={Time.timeScale:0.##}, calls={_getupCallCount}); re-firing TriggerGetup.");
            }
            ApplyGetup();
        }
        else if (!_sawGetupState && verboseLogging && !_loggedWindowExpired)
        {
            _loggedWindowExpired = true;
            Alog($"Getup(type={_pendingGetupType}) re-fire window EXPIRED after {_getupElapsed:0.###}s " +
                 $"(window={getupRetryWindow:0.###}); now passively watching for the getup clip / idle settle. " +
                 $"calls={_getupCallCount}, timeScale={Time.timeScale:0.##}");
        }
    }

    // Number of times Getup() has been entered since the last completion. A value >1 means something is
    // re-issuing the getup every frame, which would keep resetting the re-fire window (the observed spam).
    private int _getupCallCount;
    private bool _loggedWindowExpired;

    /// <summary>Human-readable name of the base-layer state currently playing (diagnostics only).</summary>
    private string DescribeCurrentState()
    {
        try
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            // StateInfo has no name; match against the known names so the log is readable.
            foreach (var n in KnownStateNames)
                if (info.IsName(n)) return n;

            // Also probe the locomotion names configured on this bridge.
            if (info.IsName(walkStateName)) return walkStateName;
            if (info.IsName(runStateName)) return runStateName;
            if (info.IsName(idleStateName)) return idleStateName;

            return $"(hash={info.fullPathHash})";
        }
        catch { return "?"; }
    }

    // Seconds during which TriggerGetup is re-asserted before we switch to passive state-watching.
    // Kept short on purpose: re-firing the trigger every frame resets the pending edge, which can stop
    // the transition from ever firing (the 2-second stall seen in the logs).
    [Tooltip("Seconds to keep re-asserting TriggerGetup before switching to passive state-watching.")]
    [SerializeField] private float getupRetryWindow = 0.3f;

    /// <summary>
    /// Marks the pending getup as applied: clears the knockdown flags, stops watching, and raises
    /// <see cref="GetupCompleted"/> so followers recompute anything that depended on the downed pose.
    /// </summary>
    private void CompleteGetup()
    {
        _getupPending = false;
        _sawGetupState = false;
        _getupCallCount = 0;
        _loggedWindowExpired = false;
        _isKnockedDown = false;
        EndAction();   // the getup is finished: the fighter counts as settled again
        if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, false);
        if (verboseLogging) Alog($"Getup(type={_pendingGetupType}) applied.");

        // The victim is back on its feet; its facing may now differ from the knockdown pose,
        // so anyone tracking a position relative to it (e.g. the attack spot) must recompute.
        try { GetupCompleted?.Invoke(); }
        catch (Exception ex) { AlogWarn($"GetupCompleted handler threw: {ex}"); }
    }

    /// <summary>
    /// Convenience overload for the getup direction enum (Back / Front).
    /// </summary>
    public void Getup(GetupDirection direction)
    {
        Getup((int)direction);
    }

    /// <summary>
    /// Triggers permanent death. Highest priority: sets IsDead and clears conflicting
    /// one-shot state. The Any-State -> KB_TopKO transition guarantees an instant interrupt.
    /// Idempotent: calling Die() again while dead is a no-op.
    /// </summary>
    public void Die()
    {
        EnsureParametersResolved();
        if (_isDead) return;
        ClearAnimationEndSignal();
        _actionInFlight = false;   // death is terminal; nothing is "settling" any more
        _isDead = true;
        _isKnockedDown = false;

        // Clear residual one-shots so nothing re-fires during the death pose.
        if (_hasTriggerAttack) animator.ResetTrigger(_triggerAttackHash);
        if (_hasTriggerHit) animator.ResetTrigger(_triggerHitHash);
        if (_hasTriggerGetup) animator.ResetTrigger(_triggerGetupHash);
        if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, false);
        if (_hasSpeed) animator.SetFloat(_speedHash, 0f);

        // PER-CLIP mode: the controller declares 'die' as a Bool (or Trigger) rather than IsDead.
        if (_usePerClipTriggers)
        {
            bool isBool = HasParameter(perClipDieName, Animator.StringToHash(perClipDieName), AnimatorControllerParameterType.Bool);
            bool isTrigger = HasTriggerByName(perClipDieName);
            if (isBool) animator.SetBool(Animator.StringToHash(perClipDieName), true);
            else if (isTrigger) FireTriggerByName(perClipDieName);
            else if (verboseLogging) AlogWarn($"per-clip controller has no '{perClipDieName}' parameter; death pose not triggered.");

            string kind = isBool ? "bool" : (isTrigger ? "trigger" : "missing");
            if (verboseLogging) Alog($"Die() (per-clip '{perClipDieName}' {kind})");
            return;
        }

        // INDEX mode: death flag last -- this is what the Any-State -> KB_TopKO transition listens for.
        if (_hasIsDead) animator.SetBool(_isDeadHash, true);

        if (verboseLogging) Alog("Die()");
    }

    /// <summary>
    /// Plays the assigned hit VFX. Called from <see cref="TakeHit"/> every time a hit reaction starts,
    /// so the effect fires on the frame the hit is triggered. No-op when no effect is assigned.
    /// </summary>
    public void TriggerHitEffect()
    {
        if (hitEffect == null) return;

        try
        {
            // Play(true) restarts the system even when it is already emitting (a heavy combo can land
            // several hits while one burst is still alive), and re-enables the GameObject if a previous
            // stop disabled it.
            hitEffect.Play(true);

            if (verboseLogging)
                Alog($"TriggerHitEffect() played '{hitEffect.name}'.");
        }
        catch (Exception ex)
        {
            AlogWarn($"TriggerHitEffect() failed: {ex}");
        }
    }

    /// <summary>
    /// Plays the hit VFX after <paramref name="delay"/> seconds. Used for HEAVY hits so the effect lands
    /// on the reaction's impact frame instead of its wind-up.
    /// </summary>
    /// <remarks>
    /// The delay is driven by a coroutine on THIS component. If the fighter dies during the delay the
    /// effect is skipped, so a lethal heavy hit does not spit a hit spark out of the corpse.
    /// </remarks>
    private IEnumerator TriggerHitEffectDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);

        // The fighter may have died (or the object been disabled) while we waited.
        if (_isDead || !isActiveAndEnabled) yield break;

        TriggerHitEffect();
    }

    /// <summary>
    /// Resets the character back to a neutral, alive, standing state.
    /// Clears IsDead + IsKnockedDown and all triggers. Useful on respawn / round reset.
    /// </summary>
    public void ResetToIdle()
    {
        _isDead = false;
        _isKnockedDown = false;

        if (_hasTriggerAttack) animator.ResetTrigger(_triggerAttackHash);
        if (_hasTriggerHit) animator.ResetTrigger(_triggerHitHash);
        if (_hasTriggerGetup) animator.ResetTrigger(_triggerGetupHash);
        if (_hasIsDead) animator.SetBool(_isDeadHash, false);
        if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, false);
        if (_hasSpeed) animator.SetFloat(_speedHash, 0f);

        if (verboseLogging) Alog("ResetToIdle()");
    }

    // ------------------------------------------------------------------
    // Read-only state (for other systems / UI)
    // ------------------------------------------------------------------

    public bool IsDead => _isDead;
    public bool IsKnockedDown => _isKnockedDown;

    /// <summary>
    /// True while a getup request is still waiting to be consumed by the controller. The bridge
    /// re-fires TriggerGetup each frame until the animator leaves the knocked-down pose, so
    /// callers can poll this to know when the fighter is actually back on its feet.
    /// </summary>
    public bool IsGetupPending => _getupPending;

    /// <summary>
    /// The Animator driving this fighter (never null once Awake has run).
    /// </summary>
    public Animator Animator => animator;

    // ------------------------------------------------------------------
    // Animation timing (for the sequential event queue)
    // ------------------------------------------------------------------

    [Header("Animation Timing")]
    [Tooltip("Fallback duration (seconds) used when an animation's length cannot be read.")]
    [SerializeField] private float fallbackAnimationDuration = 0.8f;
    [Tooltip("Extra padding (seconds) added on top of an animation's length before the queue advances.")]
    [SerializeField] private float animationDurationPadding = 0.05f;
    [Tooltip("Maximum seconds the queue will wait for a single animation before forcing the next event. Must exceed the longest authored clip (heavy hits are ~6s here).")]
    [SerializeField] private float maxAnimationWait = 12f;

    /// <summary>
    /// Attempts to read the duration (seconds) of the animation that is currently playing on the
    /// main layer. Returns -1 when no clip length can be determined. Used by the sequential event
    /// queue so it can wait for an animation to finish before applying the next server event.
    /// </summary>
    public float GetCurrentAnimationDuration()
    {
        if (animator == null || animator.runtimeAnimatorController == null) return -1f;
        try
        {
            // IMPORTANT: right after firing a trigger the animator is still in the PREVIOUS state
            // (usually Idle) until the transition is evaluated -- reporting THAT state's length is how
            // a 5-6s heavy hit ended up being measured as ~1.0s (the Idle clip length).
            //
            // Prefer the length of the ACTION state we are actually watching, and fall back to the
            // current state only when we have no better information.
            if (_watching && _sawActionState && _actionStateHash != 0)
            {
                var watched = animator.GetCurrentAnimatorStateInfo(0);
                if (watched.fullPathHash == _actionStateHash && watched.length > 0f)
                    return watched.length;
            }

            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.length > 0f && !float.IsNaN(info.length) && !float.IsInfinity(info.length))
                return info.length;
        }
        catch { }
        return -1f;
    }

    /// <summary>
    /// True while the currently-watched action clip is still playing. Unlike
    /// <see cref="IsAnimationFinished"/> this is a pure query that does not mutate the watch state, so
    /// callers can poll it repeatedly (e.g. to hold a getup until the hit animation has run through).
    /// Returns false when nothing is being watched.
    /// </summary>
    public bool IsActionPlaying()
    {
        if (animator == null || !_watching) return false;

        // AUTHORITATIVE: AnimationEndAction already reported the watched state exiting -> not playing.
        if (_animationEndedSignal) return false;

        try
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);

            // The action state has not been entered yet -> it is about to start, so it IS "playing".
            if (!_sawActionState) return true;

            // Still on the action state and not finished yet -> playing.
            if (info.fullPathHash == _actionStateHash)
                return info.normalizedTime < 1f;

            // Moved to a different state -> the action is over.
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// Returns how long the queue should wait after playing an animation on this fighter:
    /// the current clip length plus padding, clamped to [min, maxAnimationWait], or the
    /// configured fallback when the length is unknown. Used as a SAFETY UPPER BOUND when the
    /// precise <see cref="IsAnimationFinished"/> check is also supplied to the queue.
    /// </summary>
    public float GetRecommendedWaitSeconds()
    {
        float len = GetCurrentAnimationDuration();
        if (len <= 0f) len = fallbackAnimationDuration;
        return Mathf.Clamp(len + animationDurationPadding, 0.05f, maxAnimationWait);
    }

    // ---- Precise completion tracking (for the sequential event queue) ----

    // Snapshot of the state that was playing when we started watching, plus a flag for whether the
    // action's own state has actually been ENTERED yet. Right after firing a trigger the transition
    // may not have been evaluated, so the "current" state is still the previous one (e.g. Idle).
    // Without the _sawActionState gate, IsAnimationFinished() would report "finished" on the very
    // next frame (state changed away from the snapshot) and the queue would cut the clip off early.
    private int _watchedStateHash;      // state playing when watching began (usually the pre-action state)
    private int _actionStateHash;       // the state we are actually waiting to see and finish
    private bool _sawActionState;
    private bool _watching;

    /// <summary>
    /// Begins watching for the animation that is ABOUT to be triggered. Call this right AFTER
    /// issuing the animation command. Because the controller may not have evaluated the transition
    /// yet, this does not assume the action state is already current: it waits until the regular
    /// (looping/locomotion) state has been left AND has then played through before reporting done.
    /// </summary>
    public void BeginWatchCurrentAnimation()
    {
        if (animator == null) { _watching = false; return; }
        _watchedStateHash = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        _actionStateHash = 0;
        _sawActionState = false;
        _watching = true;

        // A new action owns the end signal from now on.
        _animationEndedSignal = false;
        _hasEnteredActionStateViaSignal = false;
    }

    // ------------------------------------------------------------------
    // Authoritative animation-end signal (driven by AnimationEndAction)
    // ------------------------------------------------------------------
    // Set by the AnimationEndAction StateMachineBehaviour that is attached to the attack / hit / getup
    // states. This is the REAL end of the clip, so the queue can advance on an event instead of
    // guessing a duration.
    private bool _animationEndedSignal;
    private bool _hasEnteredActionStateViaSignal;

    /// <summary>
    /// Called by <see cref="AnimationEndAction"/> when a state is ENTERED. Lets the bridge tell "the
    /// action has not started yet" apart from "the action has not been watched yet", which is what
    /// makes the end signal reliable.
    /// </summary>
    public void NotifyAnimationStateEntered(int stateHash)
    {
        _hasEnteredActionStateViaSignal = true;
        _animationEndedSignal = false;
        if (_watching) _sawActionState = true;
    }

    /// <summary>
    /// Called by <see cref="AnimationEndAction"/> when a state EXITS. This is the authoritative
    /// "the clip is over" edge that callers should wait on.
    /// </summary>
    public void NotifyAnimationStateExited(int stateHash)
    {
        _animationEndedSignal = true;

        // NOTE: we deliberately do NOT clear _actionInFlight here. This fires on EVERY state exit --
        // including the hit state exiting into the KNOCKDOWN pose -- so clearing it here made a
        // knocked-down fighter report "fully idle" while it was still lying on the ground, which is how
        // the walk-home / camera recentre yanked it to the middle mid-collapse. The flag is now cleared
        // lazily in IsFullyIdleNow, once the animator really lands in a standing idle state.

        if (verboseLogging)
            Alog($"animation-end signal set (state {stateHash}).");
    }

    /// <summary>
    /// Clears the pending animation-end signal because a NEW action is about to be fired. MUST be called
    /// every time an action is issued (attack / hit / getup / die).
    ///
    /// WHY: the signal is a one-shot flag. Once a state exits it stays true until something clears it.
    /// Without clearing here, the LAST action's exit was still being reported as "finished", so the
    /// queue's post-hit hold broke out instantly (held 0.00s) and the getup fired while the fresh hit
    /// reaction had not even started -- which is the "fighter lies down and never gets up" symptom.
    /// </summary>
    public void ClearAnimationEndSignal()
    {
        _animationEndedSignal = false;
        _hasEnteredActionStateViaSignal = false;
    }

    /// <summary>
    /// Marks the start of an action we issued (attack / hit / getup). From now until the end signal
    /// arrives, IsInIdleState() reports false so nothing treats the fighter as settled mid-clip.
    /// </summary>
    private void BeginAction()
    {
        _actionInFlight = true;
        ClearAnimationEndSignal();
    }

    /// <summary>
    /// Marks the end of the in-flight action. Called as soon as the state exits (the end signal) and
    /// from the getup completion path.
    /// </summary>
    private void EndAction()
    {
        _actionInFlight = false;
    }

    /// <summary>
    /// True once the state we were watching has actually EXITED (reported by AnimationEndAction), i.e.
    /// the clip genuinely finished or was interrupted. This is the precise signal; prefer it over
    /// timing. Returns false when nothing is being watched.
    /// </summary>
    public bool HasAnimationEndSignal => _watching && _animationEndedSignal;

    /// <summary>
    /// True once the watched action animation has finished. Semantics:
    ///   - Until the animator leaves the state we started watching (the pre-action/Idle state), we are
    ///     still waiting for the action to begin -> NOT finished. This is what stops a clip from being
    ///     cut off by an early "finished" report the frame after the trigger is fired.
    ///   - Once the action state is entered, it is finished when its normalizedTime reaches 1, or when
    ///     the animator transitions away to yet another state.
    /// Returns true immediately when not watching (so the queue never stalls).
    /// </summary>
    public bool IsAnimationFinished()
    {
        if (animator == null || !_watching) return true;

        // AUTHORITATIVE: AnimationEndAction reported the watched state exiting. This is the real end of
        // the clip, reported as an event -- no guessing from normalizedTime or durations.
        if (_animationEndedSignal) return true;

        try
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);

            // Still on the pre-action state -> the action has not started yet; keep waiting.
            if (!_sawActionState)
            {
                if (info.fullPathHash == _watchedStateHash) return false;

                // The action state has just been entered: remember it and start measuring it.
                _sawActionState = true;
                _actionStateHash = info.fullPathHash;
                return false;
            }

            // Action state finished naturally.
            if (info.fullPathHash == _actionStateHash)
            {
                if (info.normalizedTime >= 1f) return true;
                if (!info.loop && info.normalizedTime >= 0.99f) return true;
                return false;
            }

            // Transitioned away from the action state -> done.
            return true;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Stops watching the current animation.</summary>
    public void StopWatchingAnimation()
    {
        _watching = false;
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    /// <summary>
    /// True when the character may accept new gameplay-driven commands (not dead).
    /// Knocked-down is intentionally still "can act" only for Getup(); Attack/Move
    /// are additionally gated below.
    /// </summary>
    private bool CanAct()
    {
        if (_isDead) return false;
        if (_isKnockedDown) return false; // Require Getup() before resuming actions.
        return true;
    }

    /// <summary>
    /// Sets a trigger with a guaranteed clean edge. Resetting first avoids the common
    /// bug where a trigger left set from a previous frame suppresses the new transition.
    /// </summary>
    private void FireTrigger(bool hasParam, int hash, string paramName)
    {
        if (!hasParam) return;
        animator.ResetTrigger(hash);
        animator.SetTrigger(hash);
    }

    /// <summary>
    /// Fires a Trigger by name (per-clip controllers). Returns false when no Trigger with that exact
    /// name exists, so the caller can report a clear error instead of silently doing nothing.
    /// </summary>
    private bool FireTriggerByName(string triggerName)
    {
        if (animator == null || string.IsNullOrEmpty(triggerName)) return false;
        if (!HasTriggerByName(triggerName)) return false;

        int hash = Animator.StringToHash(triggerName);
        animator.ResetTrigger(hash);
        animator.SetTrigger(hash);
        return true;
    }

    /// <summary>
    /// True when the Animator declares a parameter with the given name and type.
    ///
    /// Matching is done by NAME (not by hash) because comparing hashes is brittle: if the declared
    /// type differs from what we expect, a hash+type check silently fails and the parameter is never
    /// written -- which is exactly how an AttackIndex can end up never being sent at runtime.
    /// A name match with the WRONG type is reported (once) so the mismatch is obvious in the console.
    /// </summary>
    private bool HasParameter(string paramName, int hash, AnimatorControllerParameterType type)
    {
        if (animator == null) return false;
        var parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            bool nameMatch = p.nameHash == hash ||
                             string.Equals(p.name, paramName, StringComparison.Ordinal);
            if (!nameMatch) continue;

            if (p.type == type) return true;

            AlogWarn(
                $"Animator parameter '{p.name}' is {p.type} " +
                $"but '{type}' was expected. Fix the parameter type in the Animator Controller (the value will not be written until then).");
            return false;
        }
        return false;
    }

    private bool IsValidAttackIndex(int index)
    {
        return PoolContains(lightAttackIndices, index) || PoolContains(heavyAttackIndices, index);
    }

    private bool IsValidHitIndex(int index, bool isHeavy)
    {
        return PoolContains(isHeavy ? heavyHitIndices : lightHitIndices, index);
    }

    /// <summary>
    /// Infers the weight class from a BE-supplied animationId. Anything containing "heavy"
    /// is treated as heavy; everything else (including "light" or an unknown token) is light.
    /// </summary>
    private static HitWeight ClassifyWeight(string animationId)
    {
        if (!string.IsNullOrEmpty(animationId) && animationId.ToLowerInvariant().Contains("heavy"))
            return HitWeight.Heavy;
        return HitWeight.Light;
    }

    private static bool PoolContains(int[] pool, int index)
    {
        if (pool == null) return false;
        for (int i = 0; i < pool.Length; i++)
            if (pool[i] == index) return true;
        return false;
    }

    /// <summary>
    /// Picks a random non-zero value from the given pool. Returns 0 when the pool is empty
    /// or contains only zeroes, so callers can treat 0 as "nothing to play".
    /// </summary>
    private static int PickFromPool(int[] pool)
    {
        if (pool == null || pool.Length == 0) return 0;
        int attempts = 0;
        int candidate = 0;
        do
        {
            candidate = pool[UnityEngine.Random.Range(0, pool.Length)];
            attempts++;
        } while (candidate == 0 && attempts < pool.Length);
        return candidate;
    }

    private static string PoolToString(int[] pool)
    {
        if (pool == null || pool.Length == 0) return "[]";
        return "[" + string.Join(",", pool) + "]";
    }

#if UNITY_EDITOR
    /// <summary>
    /// Editor-only helper: lists the state names present on the controller so you can confirm
    /// names like "combo_weapon 1" are spelled exactly as the transitions expect.
    /// </summary>
    [ContextMenu("Log Animator States")]
    private void LogAnimatorStates()
    {
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            AlogWarn("no Animator controller assigned.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{LogTag} [{name}] controller states:");
        var controller = animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
        if (controller != null)
        {
            for (int li = 0; li < controller.layers.Length; li++)
            {
                sb.AppendLine($"  Layer {li}: {controller.layers[li].name}");
                foreach (var state in controller.layers[li].stateMachine.states)
                    sb.AppendLine($"    - {state.state.name}");
            }
        }
        else
        {
            sb.AppendLine("  (Runtime controller -- state list unavailable in editor helper.)");
        }

        sb.AppendLine("  Expected states: " + string.Join(", ", KnownStateNames));
        Debug.Log(sb.ToString(), this);
    }
#endif
}
