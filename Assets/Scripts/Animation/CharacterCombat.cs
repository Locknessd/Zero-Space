using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class CharacterCombat : MonoBehaviour
{
    [SerializeField] private Animator animator;
    public RuntimeAnimatorController combatController;

    [Header("Pool Đòn Nhẹ")]
    public CombatTripletData[] lightCombatMoves = Array.Empty<CombatTripletData>();
    [Header("Pool Đòn Nặng")]
    public CombatTripletData[] heavyCombatMoves = Array.Empty<CombatTripletData>();

    [Header("Animation mốc trong Controller")]
    public AnimationClip targetAttackClip;
    public AnimationClip targetHitClip;
    public AnimationClip targetGetUpClip;
    public AnimationClip idleAnim;
    public AnimationClip walkAnim;
    public AnimationClip victoryAnim;
    public ParticleSystem hitEffect;
    public BattleSfxPlayer battleSfx;
    public BattleVfxPlayer battleVfx;

    public event Action<CharacterCombat, int, bool> SequenceEnded;
    public Animator Animator => animator;
    public bool IsBusy { get; private set; }
    public bool IsDead { get; private set; }
    public bool LastSequenceSucceeded { get; private set; } = true;
    public int PlaybackId { get; private set; }
    public bool IsIdleAndSettled => !IsBusy && !IsDead && animator != null &&
                                   !animator.IsInTransition(0) &&
                                   animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle");

    private AnimatorOverrideController _overrideController;
    private RuntimeAnimatorController _originalController;
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> _overrides =
        new List<KeyValuePair<AnimationClip, AnimationClip>>();
    private int _expectedState;
    private bool _hasGetUp;
    public FrankRetarget.FrankCombatWeaponRig weaponRig;
    public FrankRetarget.FrankBattlePairPlayback SourcePlayback { get; private set; }

    // Plays the controller GetUp state after a source reaction finishes. This
    // keeps a surviving hit receiver from completing directly into Idle.
    public bool BeginSourceGetUp(AnimationClip getUp)
    {
        if (getUp == null || IsDead || !Initialize()) return false;
        Replace(targetGetUpClip, getUp);
        _overrideController.ApplyOverrides(_overrides);
        IsDead = false;
        _hasGetUp = false;
        animator.SetBool("HasGetUp", false);
        animator.SetBool("IsDead", false);
        // Keep the same PlaybackId for the whole source sequence. GameManager
        // subscribes to the attack and receiver IDs before ExecuteAttack; a
        // second ID here would make the GetUp callback invisible to the queue.
        IsBusy = true;
        LastSequenceSucceeded = false;
        _expectedState = UnityEngine.Animator.StringToHash("Base Layer.GetUp");
        animator.speed = 1f;
        animator.ResetTrigger("TriggerAttack");
        animator.ResetTrigger("TriggerHit");
        animator.ResetTrigger("TriggerVictory");
        animator.Play(_expectedState, 0, 0f);
        animator.Update(0f);
        return true;
    }

    public void BeginSourceSequence(FrankRetarget.FrankBattlePairPlayback playback, bool lethal)
    {
        SourcePlayback = playback;
        PlaybackId++;
        IsBusy = true;
        IsDead = lethal;
        LastSequenceSucceeded = false;
        _expectedState = 0;
    }

    public void CompleteSourceSequence(bool succeeded)
    {
        if (IsBusy) Finish(succeeded);
    }

    internal bool PrepareSourceCompletion(FrankRetarget.FrankBattlePairPlayback playback,
        int playbackId, bool succeeded)
    {
        if (!IsBusy || SourcePlayback != playback || PlaybackId != playbackId) return false;
        IsBusy = false;
        LastSequenceSucceeded = succeeded;
        _expectedState = 0;
        _hasGetUp = false;
        if (weaponRig != null) weaponRig.Release();
        return true;
    }

    internal void InvalidateSourceRecovery(FrankRetarget.FrankBattlePairPlayback playback, int playbackId)
    {
        if (SourcePlayback == playback && PlaybackId == playbackId) _expectedState = 0;
    }

    internal void PublishSourceCompletion(int playbackId, bool succeeded)
    {
        if (PlaybackId == playbackId) SequenceEnded?.Invoke(this, playbackId, succeeded);
    }

    private void Awake()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        if (weaponRig == null) weaponRig = GetComponent<FrankRetarget.FrankCombatWeaponRig>();
    }

    public bool Initialize()
    {
        if (_overrideController != null) return animator != null;
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null) return ConfigurationError("Chưa gán Animator.");
        var controller = ResolveCombatController();
        if (controller == null) return ConfigurationError("Chưa có TripletCombat controller.");

        _originalController = animator.runtimeAnimatorController;
        _overrideController = new AnimatorOverrideController(controller);
        _overrideController.name = name + " Combat Overrides";
        _overrideController.GetOverrides(_overrides);
        if (!HasOverrideSlots(_overrides))
        {
            var fallback = Resources.Load<RuntimeAnimatorController>("Combat/TripletCombat");
            if (fallback != null && fallback != controller)
            {
                DestroyOverrideController();
                _overrides.Clear();
                _overrideController = new AnimatorOverrideController(fallback);
                _overrideController.name = name + " Combat Overrides (Fallback)";
                _overrideController.GetOverrides(_overrides);
            }
        }
        targetAttackClip = ResolveSlot(targetAttackClip, "AttackPlaceholder");
        targetHitClip = ResolveSlot(targetHitClip, "HitPlaceholder");
        targetGetUpClip = ResolveSlot(targetGetUpClip, "GetUpPlaceholder");
        if (targetAttackClip == null || targetHitClip == null || targetGetUpClip == null)
        {
            if (Application.isPlaying) Destroy(_overrideController);
            else DestroyImmediate(_overrideController);
            _overrideController = null;
            return ConfigurationError("Controller thiếu clip mốc Attack / Hit / GetUp.");
        }

        Replace(ResolveSlot(null, "IdlePlaceholder"), idleAnim);
        Replace(ResolveSlot(null, "WalkPlaceholder"), walkAnim != null ? walkAnim : idleAnim);
        Replace(ResolveSlot(null, "VictoryPlaceholder"), victoryAnim);
        _overrideController.ApplyOverrides(_overrides);
        animator.runtimeAnimatorController = _overrideController;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.applyRootMotion = false;
        animator.stabilizeFeet = true;
        animator.Rebind();
        animator.Update(0f);
        return true;
    }

    private RuntimeAnimatorController ResolveCombatController()
    {
        RuntimeAnimatorController candidate = combatController;
        if (candidate is AnimatorOverrideController overrideAsset)
            candidate = overrideAsset.runtimeAnimatorController;

        var fallback = Resources.Load<RuntimeAnimatorController>("Combat/TripletCombat");
        if (HasRequiredSlots(candidate)) return candidate;
        if (HasRequiredSlots(fallback)) return fallback;
        return candidate != null ? candidate : fallback;
    }

    private static bool HasRequiredSlots(RuntimeAnimatorController controller)
    {
        if (controller == null) return false;
        var clips = controller.animationClips;
        return clips != null &&
               Array.Exists(clips, clip => clip != null && clip.name == "AttackPlaceholder") &&
               Array.Exists(clips, clip => clip != null && clip.name == "HitPlaceholder") &&
               Array.Exists(clips, clip => clip != null && clip.name == "GetUpPlaceholder");
    }

    private static bool HasOverrideSlots(List<KeyValuePair<AnimationClip, AnimationClip>> overrides)
    {
        if (overrides == null) return false;
        bool attack = false;
        bool hit = false;
        bool getUp = false;
        foreach (var entry in overrides)
        {
            if (entry.Key == null) continue;
            if (entry.Key.name == "AttackPlaceholder") attack = true;
            else if (entry.Key.name == "HitPlaceholder") hit = true;
            else if (entry.Key.name == "GetUpPlaceholder") getUp = true;
        }
        return attack && hit && getUp;
    }

    private void DestroyOverrideController()
    {
        if (_overrideController == null) return;
        if (Application.isPlaying) Destroy(_overrideController);
        else DestroyImmediate(_overrideController);
        _overrideController = null;
    }

    private AnimationClip ResolveSlot(AnimationClip assigned, string slotName)
    {
        foreach (var entry in _overrides)
        {
            if (assigned != null)
            {
                if (entry.Key == assigned ||
                    (!string.IsNullOrEmpty(assigned.name) && entry.Key.name == assigned.name))
                    return entry.Key;
            }
            else if (entry.Key.name == slotName)
            {
                return entry.Key;
            }
        }
        return null;
    }

    private void Replace(AnimationClip slot, AnimationClip replacement)
    {
        if (slot == null) return;
        for (int index = 0; index < _overrides.Count; index++)
            if (_overrides[index].Key == slot)
            {
                _overrides[index] = new KeyValuePair<AnimationClip, AnimationClip>(slot, replacement);
                return;
            }
    }

    public CombatTripletData GetRandomLightMove() => GetRandomMove(lightCombatMoves);
    public CombatTripletData GetRandomHeavyMove() => GetRandomMove(heavyCombatMoves);

    private static CombatTripletData GetRandomMove(CombatTripletData[] pool)
    {
        if (pool == null) return null;
        CombatTripletData selected = null;
        int eligible = 0;
        foreach (var move in pool)
            if (move != null && !move.requiresExplicitSelection && move.IsValid &&
                UnityEngine.Random.Range(0, ++eligible) == 0) selected = move;
        return selected;
    }

    public bool PerformLightAttack(CharacterCombat targetCombat) => ExecuteAttack(GetRandomLightMove(), targetCombat);
    public bool PerformHeavyAttack(CharacterCombat targetCombat) => ExecuteAttack(GetRandomHeavyMove(), targetCombat);

    public bool ExecuteAttack(CombatTripletData move, CharacterCombat targetCombat, bool lethal = false)
    {
        if (move == null || !move.IsValid) return ConfigurationError("Pool không có bộ animation hợp lệ.");
        if (!isActiveAndEnabled || targetCombat == null || !targetCombat.isActiveAndEnabled ||
            targetCombat == this || IsBusy || IsDead ||
            targetCombat.IsBusy || targetCombat.IsDead) return false;
        if (!Initialize() || !targetCombat.Initialize()) return false;
        // BattleScene is a side-on lane. Attack range is the horizontal fighter-root
        // separation; vertical pose offsets and tiny depth corrections must not make a
        // correctly positioned attack fail its range check.
        float attackDistance = Mathf.Abs(animator.transform.position.x -
                                         targetCombat.Animator.transform.position.x);
        if (attackDistance > move.attackRange + 0.01f)
            return ConfigurationError("Mục tiêu ngoài tầm: " + move.moveName);
        // A direct grapple entry must already fit its shared source frame. The
        // normal queue approaches this range before acquiring either participant.
        if (move.grapple && Mathf.Abs(attackDistance - move.attackRange) > .15f)
            return false;

        if (move.sourcePair != null && move.sourcePair.maximumAlignmentError > 0 &&
            Mathf.Abs(attackDistance - move.attackRange) > move.sourcePair.maximumAlignmentError)
            return false;

        if (move.sourcePair != null && move.sourcePair.Valid)
        {
            if (SourcePlayback) SourcePlayback.Cancel();
            targetCombat.SourcePlayback?.Cancel();
            var pairPlayer = GetComponent<FrankRetarget.FrankBattlePairPlayback>();
            if (!pairPlayer) pairPlayer = gameObject.AddComponent<FrankRetarget.FrankBattlePairPlayback>();
            return pairPlayer.Begin(this, targetCombat, move, lethal);
        }

        Replace(targetAttackClip, move.attackAnim);
        _overrideController.ApplyOverrides(_overrides);
        if (weaponRig != null && !weaponRig.Begin(animator, move.weapon)) return false;
        if (!targetCombat.ReceiveHit(move.hitAnim, move.getUpAnim, lethal))
        {
            if (weaponRig != null) weaponRig.Release();
            return false;
        }
        Begin("Attack");
        return true;
    }

    public bool ReceiveHit(AnimationClip hit, AnimationClip getUp, bool lethal = false)
    {
        if (hit == null || IsBusy || IsDead || !Initialize()) return false;
        Replace(targetHitClip, hit);
        Replace(targetGetUpClip, getUp);
        _overrideController.ApplyOverrides(_overrides);
        IsDead = lethal;
        _hasGetUp = getUp != null && !lethal;
        animator.SetBool("HasGetUp", _hasGetUp);
        animator.SetBool("IsDead", lethal);
        if (battleVfx && battleVfx.isActiveAndEnabled) battleVfx.PlayHit(this);
        else if (hitEffect != null)
        {
            hitEffect.gameObject.SetActive(true);
            hitEffect.Play(true);
        }
        Begin("Hit");
        return true;
    }

    public bool PlayVictory()
    {
        if (IsBusy || IsDead || victoryAnim == null || !Initialize()) return false;
        Begin("Victory");
        return true;
    }

    private void Begin(string state)
    {
        PlaybackId++;
        IsBusy = true;
        LastSequenceSucceeded = false;
        _expectedState = UnityEngine.Animator.StringToHash("Base Layer." + state);
        animator.SetFloat("Speed", 0f);
        animator.ResetTrigger("TriggerAttack");
        animator.ResetTrigger("TriggerHit");
        animator.ResetTrigger("TriggerVictory");
        // Enter once. Trigger + Update followed by Play re-entered the same state and
        // fired OnStateExit at time zero, releasing the weapon before the first frame.
        animator.Play(_expectedState, 0, 0f);
        animator.Update(0f);
    }

    public void NotifyAnimationEnded(int stateHash, int playbackId, bool completed)
    {
        if (!IsBusy || playbackId != PlaybackId || stateHash != _expectedState) return;
        if (SourcePlayback && SourcePlayback.Playing &&
            stateHash == UnityEngine.Animator.StringToHash("Base Layer.GetUp"))
        {
            SourcePlayback.NotifySourceRecoveryEnded(this, playbackId, completed);
            return;
        }
        if (completed && stateHash == UnityEngine.Animator.StringToHash("Base Layer.Hit") && _hasGetUp)
        {
            _expectedState = UnityEngine.Animator.StringToHash("Base Layer.GetUp");
            return;
        }
        if (completed && IsDead) animator.speed = 0f;
        Finish(completed);
    }

    private void Finish(bool succeeded)
    {
        // Called by the Animator itself. Do not evaluate it recursively or during OnDisable;
        // its normal transition restores idle while this method releases the source weapon rig.
        IsBusy = false;
        LastSequenceSucceeded = succeeded;
        if (weaponRig != null) weaponRig.Release();
        SequenceEnded?.Invoke(this, PlaybackId, succeeded);
    }

    public void Move(float speed)
    {
        if (!IsDead && !IsBusy && Initialize()) animator.SetFloat("Speed", speed);
    }

    public void MarkDead()
    {
        IsDead = true;
        SourcePlayback?.CancelIfUnexpectedDeath(this);
        _hasGetUp = false;
        if (Initialize())
        {
            animator.SetBool("IsDead", true);
            animator.SetBool("HasGetUp", false);
            if (!IsBusy) animator.speed = 0f;
        }
    }

    public void ResetCombat()
    {
        if (SourcePlayback) SourcePlayback.Cancel();
        if (IsBusy) Finish(false);
        if (weaponRig != null) weaponRig.Release();
        IsDead = false;
        foreach (var equipment in GetComponentsInChildren<TrumpWeaponManager>(true))
            equipment.ResetUnarmedPresentation();
        _hasGetUp = false;
        PlaybackId++;
        LastSequenceSucceeded = true;
        if (!Initialize()) return;
        animator.speed = 1f;
        animator.Rebind();
        animator.Update(0f);
    }

    private bool ConfigurationError(string message)
    {
        Debug.LogError("CharacterCombat [" + name + "]: " + message, this);
        return false;
    }

    private void OnDisable()
    {
        if (SourcePlayback) SourcePlayback.Cancel();
        if (IsBusy) Finish(false);
        if (weaponRig != null) weaponRig.Release();
    }

    private void OnDestroy()
    {
        if (_overrideController == null) return;
        if (animator != null && animator.runtimeAnimatorController == _overrideController)
            animator.runtimeAnimatorController = _originalController;
        if (Application.isPlaying) Destroy(_overrideController);
        else DestroyImmediate(_overrideController);
    }
}
