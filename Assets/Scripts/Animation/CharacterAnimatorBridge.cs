using System;
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
    /// Logical attack slots. These map 1:1 to AttackIndex values the controller expects.
    /// </summary>
    public enum AttackType
    {
        Attack3 = 3,
        Attack5 = 5,
        ComboWeapon1 = 1,
        Attack6 = 6,
        Attack7 = 7
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

    [Header("Safety / Tuning")]
    [Tooltip("When true, verifying that every expected parameter exists on Awake and warn if not.")]
    [SerializeField] private bool validateParametersOnAwake = true;
    [Tooltip("When true, a heavy hit locks Attack/Move until Getup() is called (requires the controller to have a KD_Idle state driven by IsKnockedDown). Leave FALSE for controllers without a knockdown idle, otherwise the character would lock forever.")]
    [SerializeField] private bool knockdownBlocksActions = false;
    [Tooltip("Min seconds between two attacks to avoid double-fire from repeated calls in one frame.")]
    [SerializeField] private float attackCooldown = 0.05f;
    [Tooltip("Log state transitions and parameter writes (verbose debugging).")]
    [SerializeField] private bool verboseLogging = false;

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

    // Runtime state.
    private bool _isDead;
    private bool _isKnockedDown;
    private float _lastAttackTime = -999f;

    // Well-known state name hashes for diagnostics / best-effort checks.
    private static readonly string[] KnownStateNames =
    {
        "Idle", "Walk",
        "attack3", "attack5", "combo_weapon 1", "attack6", "attack7",
        "hit3", "hit5", "hit_combo_weapon 1", "hit6", "hit7",
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
            Debug.LogError($"{nameof(CharacterAnimatorBridge)}: No Animator found on '{name}'. Bridge will be inert.", this);
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

        _hasSpeed = HasParameter(_speedHash, AnimatorControllerParameterType.Float);
        _hasAttackIndex = HasParameter(_attackIndexHash, AnimatorControllerParameterType.Int);
        _hasTriggerAttack = HasParameter(_triggerAttackHash, AnimatorControllerParameterType.Trigger);
        _hasHitIndex = HasParameter(_hitIndexHash, AnimatorControllerParameterType.Int);
        _hasTriggerHit = HasParameter(_triggerHitHash, AnimatorControllerParameterType.Trigger);
        _hasIsKnockedDown = HasParameter(_isKnockedDownHash, AnimatorControllerParameterType.Bool);
        _hasGetupType = HasParameter(_getupTypeHash, AnimatorControllerParameterType.Int);
        _hasTriggerGetup = HasParameter(_triggerGetupHash, AnimatorControllerParameterType.Trigger);
        _hasIsDead = HasParameter(_isDeadHash, AnimatorControllerParameterType.Bool);
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
            Debug.LogWarning(
                $"{nameof(CharacterAnimatorBridge)} on '{name}': missing/mistyped Animator parameters:\n  - " +
                string.Join("\n  - ", missing), this);
        }
        else if (verboseLogging)
        {
            Debug.Log($"{nameof(CharacterAnimatorBridge)} on '{name}': all parameters validated.", this);
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
        if (!CanAct()) return;
        if (!_hasSpeed) return;

        animator.SetFloat(_speedHash, Mathf.Clamp01(value));
        if (verboseLogging) Debug.Log($"[{name}] Move(Speed={value:0.##})");
    }

    /// <summary>
    /// Fires a specific attack. index maps to AttackIndex values (1, 3, 5, 6, 7).
    /// Sets AttackIndex then the TriggerAttack trigger. Trigger is reset first to guarantee
    /// a fresh edge (prevents a swallowed attack when a previous trigger was left set).
    /// </summary>
    public void Attack(int index)
    {
        if (!CanAct()) return;
        if (!IsValidAttackIndex(index))
        {
            Debug.LogWarning($"{nameof(CharacterAnimatorBridge)} on '{name}': Attack({index}) is not in the light {PoolToString(lightAttackIndices)} or heavy {PoolToString(heavyAttackIndices)} pool.", this);
            return;
        }

        // Cooldown guard against accidental double-fire within a single frame.
        if (Time.time - _lastAttackTime < attackCooldown) return;
        _lastAttackTime = Time.time;

        if (_hasAttackIndex) animator.SetInteger(_attackIndexHash, index);
        FireTrigger(_hasTriggerAttack, _triggerAttackHash, triggerAttackParam);

        if (verboseLogging) Debug.Log($"[{name}] Attack(index={index})");
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
        if (index == 0)
        {
            Debug.LogWarning($"{nameof(CharacterAnimatorBridge)} on '{name}': no attack variants configured for weight={weight} ('{animationId}').", this);
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
            Debug.LogWarning($"{nameof(CharacterAnimatorBridge)} on '{name}': no hit variants configured for weight={weight} ('{animationId}').", this);
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
            Debug.LogWarning($"{nameof(CharacterAnimatorBridge)} on '{name}': TakeHitByAttackIndex called with index 0.", this);
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
        if (_isDead) return; // Death has highest priority; ignore hits once dead.
        if (!IsValidHitIndex(hitIndex, isHeavy))
        {
            Debug.LogWarning($"{nameof(CharacterAnimatorBridge)} on '{name}': TakeHit({hitIndex}, heavy={isHeavy}) is not in the expected hit pool.", this);
            return;
        }

        if (_hasHitIndex) animator.SetInteger(_hitIndexHash, hitIndex);

        if (isHeavy)
        {
            // Flag the knockdown for controllers that author a knockdown idle.
            if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, true);
            if (knockdownBlocksActions) _isKnockedDown = true;
        }

        FireTrigger(_hasTriggerHit, _triggerHitHash, triggerHitParam);

        if (verboseLogging) Debug.Log($"[{name}] TakeHit(index={hitIndex}, heavy={isHeavy})");
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
    /// Explicitly forces the knockdown state without a specific hit reaction
    /// (useful if a knockdown is triggered by something other than a hit event).
    /// </summary>
    public void Knockdown()
    {
        if (_isDead) return;
        if (knockdownBlocksActions) _isKnockedDown = true;
        if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, true);
        if (verboseLogging) Debug.Log($"[{name}] Knockdown()");
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
        if (_isDead) return;
        if (type != (int)GetupDirection.Back && type != (int)GetupDirection.Front)
        {
            Debug.LogWarning($"{nameof(CharacterAnimatorBridge)} on '{name}': Getup({type}) must be 1 (Back) or 2 (Front).", this);
            return;
        }

        if (_hasGetupType) animator.SetInteger(_getupTypeHash, type);

        // Leave the knockdown state and fire the getup trigger in a safe order:
        // set the target state selector first, then lower the knockdown flag, then trigger.
        _isKnockedDown = false;
        if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, false);
        FireTrigger(_hasTriggerGetup, _triggerGetupHash, triggerGetupParam);

        if (verboseLogging) Debug.Log($"[{name}] Getup(type={type})");
    }

    /// <summary>
    /// Convenience overload for the getup direction enum.
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
        if (_isDead) return;
        _isDead = true;
        _isKnockedDown = false;

        // Clear residual one-shots so nothing re-fires during the death pose.
        if (_hasTriggerAttack) animator.ResetTrigger(_triggerAttackHash);
        if (_hasTriggerHit) animator.ResetTrigger(_triggerHitHash);
        if (_hasTriggerGetup) animator.ResetTrigger(_triggerGetupHash);
        if (_hasIsKnockedDown) animator.SetBool(_isKnockedDownHash, false);
        if (_hasSpeed) animator.SetFloat(_speedHash, 0f);

        // Death flag last: this is what the Any-State -> KB_TopKO transition listens for.
        if (_hasIsDead) animator.SetBool(_isDeadHash, true);

        if (verboseLogging) Debug.Log($"[{name}] Die()");
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

        if (verboseLogging) Debug.Log($"[{name}] ResetToIdle()");
    }

    // ------------------------------------------------------------------
    // Read-only state (for other systems / UI)
    // ------------------------------------------------------------------

    public bool IsDead => _isDead;
    public bool IsKnockedDown => _isKnockedDown;

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
    [Tooltip("Maximum seconds the queue will wait for a single animation before forcing the next event.")]
    [SerializeField] private float maxAnimationWait = 5f;

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
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.length > 0f && !float.IsNaN(info.length) && !float.IsInfinity(info.length))
                return info.length;
        }
        catch { }
        return -1f;
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

    // Hash of the state that was playing when we last started watching for completion.
    private int _watchedStateHash;
    private bool _watching;

    /// <summary>
    /// Begins watching the currently-playing animation so <see cref="IsAnimationFinished"/> can
    /// report when it completes. Call this right AFTER issuing the animation command; the queue
    /// then polls IsAnimationFinished() each frame to advance as soon as the clip ends.
    /// </summary>
    public void BeginWatchCurrentAnimation()
    {
        if (animator == null) { _watching = false; return; }
        _watchedStateHash = animator.GetCurrentAnimatorStateInfo(0).fullPathHash;
        _watching = true;
    }

    /// <summary>
    /// True once the watched animation has finished. Completion means either:
    ///   - the watched state's normalizedTime has reached 1 (clip played through), OR
    ///   - the animator has transitioned away to a different state.
    /// Returns true immediately when not watching (so the queue never stalls).
    /// </summary>
    public bool IsAnimationFinished()
    {
        if (animator == null || !_watching) return true;
        try
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);

            // State changed -> the watched animation is no longer current, so it's done.
            if (info.fullPathHash != _watchedStateHash) return true;

            // Still the same state: finished when it has played through (or is looping past 1).
            if (info.normalizedTime >= 1f) return true;

            // A non-looping clip that has been interrupted also counts as done.
            if (!info.loop && info.normalizedTime >= 0.99f) return true;

            return false;
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

    private bool HasParameter(int hash, AnimatorControllerParameterType type)
    {
        if (animator == null) return false;
        var parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].nameHash == hash && parameters[i].type == type)
                return true;
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
            Debug.LogWarning($"{nameof(CharacterAnimatorBridge)} on '{name}': no Animator controller assigned.", this);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{nameof(CharacterAnimatorBridge)} on '{name}' -- controller states:");
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
