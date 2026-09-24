/// <summary>
/// PROJECT ARCHITECTURE: Core Logic Layer (Controller trung tâm)
/// ROLE: Điều phối luồng trận đấu, nhận dữ liệu từ WebSocketManager và phân phối xuống các hệ thống UI/Animation.
/// RESPONSIBILITIES:
/// - Lưu trữ trạng thái trận đấu hiện tại (MatchState) theo tài liệu thiết kế.
/// - Phân tích (Parse) các gói tin JSON từ Server thành các C# Object tương ứng.
/// - Gọi các hàm cập nhật giao diện của PlayerUI, RoundManager và CombatPositioningController khi có sự kiện mới.
/// AI NOTE: Script này đóng vai trò bộ não trung tâm của Client, không tự tính toán sát thương (vì BE đã tính), chỉ nhận kết quả và điều phối hiển thị.
/// QUEUE: Mọi sự kiện gameplay được đẩy vào MỘT hàng đợi duy nhất do CombatPositioningController sở hữu
/// (xem HandleMemeBattleEvent). Hàng đợi đó chạy tuần tự từng item: APPLY (UI/state/hit routing) -> MOVE -> ANIMATE -> WAIT,
/// nên không có scheduler thứ hai nào tranh chấp vị trí/animation của nhân vật.
/// </summary>
using System;
using System.Reflection;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    // References
    [Header("UI")]
    [Tooltip("Central UI manager for the whole game. When assigned, all UI updates route through it.")]
    public MemeBattleUI uiManager;

    [Header("Event Queue")]
    [Tooltip("When true, incoming events are applied strictly one at a time, waiting for each triggered animation to finish before the next event (prevents new events from cutting off a running animation).")]
    public bool useSequentialEventQueue = true;
    [Tooltip("Safety cap (seconds) a single queued event may hold the queue before force-advancing.")]
    public float maxEventHoldSeconds = 15f;

    [Header("Legacy UI (deprecated)")]
    [Tooltip("DEPRECATED: use uiManager (MemeBattleUI) instead. Kept only so old scenes still compile/run.")]
    public PlayerUI playerUI;
    public RoundManager roundManager;

    [Header("Animator Bridges (optional, per side)")]
    [Tooltip("Optional CharacterAnimatorBridge for the Left (bot_a) fighter. This is the animation authority for the left side.")]
    public CharacterAnimatorBridge leftBridge;
    [Tooltip("Optional CharacterAnimatorBridge for the Right (bot_b) fighter. This is the animation authority for the right side.")]
    public CharacterAnimatorBridge rightBridge;
    [Tooltip("Deprecated: bridges are now always the animation authority. Kept so old scenes still expose the toggle.")]
    public bool bridgesTakePriority = true;
    [Tooltip("When true, a heavy hit that does not kill the target automatically plays the getup animation after the knockdown hold, so the fighter returns to Idle.")]
    public bool autoGetupAfterKnockdown = true;
    [Tooltip("Seconds the fighter stays knocked down before the getup animation is triggered.")]
    public float getupDelay = 0.8f;
    [Tooltip("Which getup direction to play: 1 = Back, 2 = Front.")]
    public int getupType = 1;

    [Header("Combat Positioning")]
    [Tooltip("Route attack/damage events through CombatPositioningController so the attacker MOVES to the attack spot before the animation pair plays. Each event is queued and runs to completion before the next.")]
    public bool choreographAttackPositions = true;
    [Tooltip("Optional CombatPositioningController. Auto-found in the scene when left empty.")]
    public CombatPositioningController positioningController;

    [Tooltip("Default crossfade (seconds) used when the positioning flow fires a combat clip.")]
    public float defaultCrossFade = 0.1f;

    [Header("Local Test")]
    public bool enableLocalInputTesting = true;

    [Header("Defaults")]
    [Tooltip("Default initial max HP to show for both players before server snapshot arrives")]
    public long defaultInitialMaxHpAtomic = 1000;

    [Header("Auto Subscribe")]
    [Tooltip("If set, GameManager will subscribe to this matchId when WebSocket is connected")]
    public string initialMatchId = "";

    [Header("Debug")]
    [Tooltip("Show debug info in console")]
    public bool debugMode = true;

    // state
    private System.Collections.Generic.Dictionary<string, long> _characterMaxHp = new System.Collections.Generic.Dictionary<string, long>();
    private System.Collections.Generic.Dictionary<string, string> _characterNames = new System.Collections.Generic.Dictionary<string, string>();
    private MemeBattleMatchSnapshot _latestSnapshot;
    private bool _matchEnded = false;
    private PlayerUI.Side? _loserSideForDieAnimation = null;  // Track which side should play die animation
    // When true, speech/dialogue text will be swapped between sides during match play (to match legacy UI behaviour).
    // We keep this separate so that on WINNER_DECLARED we can revert to normal behaviour.
    private bool _swapSpeechDuringMatch = true;

    // Map to remember which numeric variant was used when playing attack/hit pairs for a given turn+actor+target.
    private System.Collections.Generic.Dictionary<string, int> _matchedVariantMap = new System.Collections.Generic.Dictionary<string, int>();

    // Exchange keys (turnId:actor:target) whose knockout getup was already chained onto the
    // ARGUMENT_SELECTED item. The later DAMAGE_APPLIED for the same exchange must NOT schedule a
    // second getup, otherwise the victim would stand up twice (and the second one could stall a turn).
    private readonly System.Collections.Generic.HashSet<string> _exchangeGetupHandled = new System.Collections.Generic.HashSet<string>();

    // Lethal-hit tracking. Set the moment a DAMAGE_APPLIED with hpAfter <= 0 arrives (before its queue
    // item runs), so the still-running ARGUMENT_SELECTED exchange can suppress the victim's hit reaction
    // and cancel its chained getup, and the death pose takes over immediately.
    private readonly System.Collections.Generic.HashSet<string> _lethalExchangeKeys = new System.Collections.Generic.HashSet<string>();

    // (turnId:characterId) pairs whose per-turn HP has already been written to the bar.
    //
    // HP for a turn comes from the turn's HP event (HP_CHANGED, or DAMAGE_APPLIED as a fallback) and is
    // applied by ApplyTurnHpOnce -- the SINGLE guarded writer. The value is ABSOLUTE (hpAfterAtomic), so
    // even a stray duplicate would not corrupt the number; this set guarantees each turn moves the bar
    // EXACTLY ONCE, and also stops a reconnect replay from repainting/flickering it.
    private readonly System.Collections.Generic.HashSet<string> _hpChangedAppliedKeys = new System.Collections.Generic.HashSet<string>();

    [Tooltip("Extra seconds the queue holds after a chained getup so the getup clip can fully play before the next turn starts.")]
    public float getupSettleSeconds = 1.5f;

    [Tooltip("Seconds the queue waits before triggering a chained getup after a knockout exchange, so the hit reaction clip finishes first (the hit must run through before the fighter stands up).")]
    public float getupAfterHitDelay = 1.0f;

    // Key (turnId:actor:target) of the exchange currently being animated by the queue. Set when an
    // ARGUMENT_SELECTED is enqueued and consumed by PlayAnimationForSide to record the chosen attack
    // variant so the matching DAMAGE_APPLIED pairs attackN with hitN.
    private string _pendingExchangeKey;

    // Variant index the attacker just played in the current exchange. The victim's hit reaction in
    // the SAME exchange reads this so attackN pairs with hitN (no random mismatch between fighters).
    private int _lastAttackVariant;

    // ==================================================================
    // ONE TURN = ONE STACK
    // ------------------------------------------------------------------
    // The backend streams a turn as SEVERAL events that all share the SAME turnId:
    //   TURN_STARTED -> ARGUMENT_SELECTED -> MULTIPLIER_SELECTED -> DAMAGE_APPLIED -> HP_CHANGED
    //
    // Applying them as separate queue items let the turn's fight "leak" across the queue: HP could drop
    // at the wrong moment or twice, and the next turn could interleave with the current one.
    //
    // Every event of a turn is therefore BUFFERED here and FLUSHED as ONE single queue stack, so the
    // whole turn (who attacks, with what animation, who is hit, how much HP is lost) runs as one atomic
    // beat. The next turn's stack only starts once this one has fully finished, and each stack applies
    // its HP change EXACTLY ONCE per hit.
    private string _bufferedTurnId;
    private readonly System.Collections.Generic.List<MemeBattleEvent> _bufferedTurnEvents = new System.Collections.Generic.List<MemeBattleEvent>();

    [Tooltip("Seconds to wait after the LAST buffered event of a turn before flushing it as one stack, when no following TURN_STARTED arrives (safety for the final turn of a match).")]
    public float turnStackFlushDelay = 0.5f;

    // Wall-clock (Time.unscaledTime) deadline after which the buffered turn is force-flushed.
    private float _turnFlushDeadline;
    private bool _turnFlushScheduled;

    // Helper: ensure we have a sensible recorded max HP for a given character id
    private void EnsureKnownMaxHp(string characterId, long hpBefore = 0, long hpAfter = 0)
    {
        if (string.IsNullOrEmpty(characterId)) return;
        long existing = 0;
        _characterMaxHp.TryGetValue(characterId, out existing);
        long candidate = existing;
        if (hpBefore > candidate) candidate = hpBefore;
        if (hpAfter > candidate) candidate = hpAfter;
        // If candidate still zero, do nothing
        if (candidate > 0)
        {
            _characterMaxHp[characterId] = candidate;
            // Also set side-key mappings if applicable
            if (_characterNames.TryGetValue("bot_a", out var aid) && aid == characterId) _characterMaxHp["bot_a"] = candidate;
            if (_characterNames.TryGetValue("bot_b", out var bid) && bid == characterId) _characterMaxHp["bot_b"] = candidate;
        }
    }

    /// <summary>
    /// Clears every PER-TURN idempotency key and the queue's per-turn barrier state. Called when a new
    /// match starts so nothing from a previous match (or a reconnect replay) can leak into the new one:
    /// a turn id the backend REUSES would otherwise look like an already-applied exchange / already-seen
    /// turn and its HP update would be silently skipped.
    /// </summary>
    private void ResetPerTurnGuardsForNewMatch()
    {
        _hpChangedAppliedKeys.Clear();
        _lethalExchangeKeys.Clear();
        _exchangeGetupHandled.Clear();
        _matchedVariantMap.Clear();
        _pendingExchangeKey = null;

        // Drop any half-collected turn from a previous match; a fresh match starts with an empty buffer.
        _bufferedTurnId = null;
        _bufferedTurnEvents.Clear();
        _turnFlushScheduled = false;

        try { Positioning?.ResetTurnBarrier(); }
        catch (System.Exception ex) { Debug.LogWarning($"GameManager: ResetTurnBarrier failed: {ex}"); }
    }

    void Awake()
    {
        Instance = this;
        WirePositioningHandlers();
    }

    /// <summary>
    /// Wires the CombatPositioningController's handler hooks to this GameManager's delegates.
    /// Called from Awake, Start and again whenever the controller is (re)resolved, because the
    /// controller's Awake may run AFTER GameManager's -- in which case CombatPositioningController
    /// .Instance is still null during our Awake and the hooks would never be assigned (which makes
    /// every queued animation a silent no-op: the queued attack never reaches the bridge).
    /// </summary>
    private void WirePositioningHandlers()
    {
        var pos = Positioning;
        if (pos == null) return;

        // Re-assign every time; delegates are cheap and this guarantees the hooks are live even if
        // the controller was created late or the reference was reset.
        pos.PlayAnimationHandler = PlayAnimationForSide;
        pos.MoveSpeedHandler = DriveMoveSpeed;
        pos.LocomotionSettledHandler = IsLocomotionSettled;
        pos.AnimationFinishedHandler = IsSideAnimationFinished;
        pos.SideActionPlayingHandler = IsSideActionPlaying;
        pos.SideIdleSettledHandler = IsSideIdleSettled;
        pos.SideDeadHandler = IsSideDead;
        pos.SideAnimationEndSignalledHandler = IsSideAnimationEndSignalled;
        pos.AttackFinishedHandler = EndCameraAttackFocus;
    }

    /// <summary>
    /// Drives a side's locomotion blend while it steps to the attack spot. Routes to the bridge's
    /// Move(value) which writes the Animator "Speed" float (1 = run, 0 = idle). Used as the
    /// CombatPositioningController move-speed hook.
    /// </summary>
    private void DriveMoveSpeed(PlayerUI.Side side, float value)
    {
        var bridge = GetBridgeForSide(side);
        if (bridge == null) return;
        try { bridge.Move(value); }
        catch (System.Exception ex) { Debug.LogWarning($"Animation [GameManager] DriveMoveSpeed on {side} failed: {ex}"); }
    }

    /// <summary>
    /// True when the side's animator has left its locomotion (Walk/Run) blend and is safe to fire a
    /// one-shot trigger. Used as the CombatPositioningController settled hook. Returns true when no
    /// bridge is wired so the flow is never blocked.
    /// </summary>
    private bool IsLocomotionSettled(PlayerUI.Side side)
    {
        var bridge = GetBridgeForSide(side);
        if (bridge == null) return true;
        try { return bridge.IsLocomotionSettled(); }
        catch { return true; }
    }

    /// <summary>
    /// True when the side's currently-playing clip has finished. Used as the
    /// CombatPositioningController animation-finished hook so the queue waits out the real clip.
    /// Returns true when no bridge is wired so the flow is never blocked.
    /// </summary>
    private bool IsSideAnimationFinished(PlayerUI.Side side)
    {
        var bridge = GetBridgeForSide(side);
        if (bridge == null) return true;
        try { return bridge.IsAnimationFinished(); }
        catch { return true; }
    }

    /// <summary>
    /// True when the side's AnimationEndAction behaviour has reported the watched state EXITING.
    /// This is the AUTHORITATIVE end-of-clip signal (an actual event, not a guessed duration) and is
    /// what the queue should prefer. Returns false when no bridge is wired.
    /// </summary>
    private bool IsSideAnimationEndSignalled(PlayerUI.Side side)
    {
        var bridge = GetBridgeForSide(side);
        if (bridge == null) return false;
        try { return bridge.HasAnimationEndSignal; }
        catch { return false; }
    }

    /// <summary>
    /// True while the side's watched action clip is still playing. Non-mutating companion to
    /// IsSideAnimationFinished, used to hold a chained getup until the hit reaction truly finishes.
    /// Returns false when no bridge is wired so the flow is never blocked.
    /// </summary>
    private bool IsSideActionPlaying(PlayerUI.Side side)
    {
        var bridge = GetBridgeForSide(side);
        if (bridge == null) return false;
        try { return bridge.IsActionPlaying(); }
        catch { return false; }
    }

    /// <summary>
    /// True when the side is standing idle AND fully settled (idle pose, not knocked down, no pending
    /// getup). The positioning queue uses this as a fast-path so the next queued stack starts the moment
    /// both fighters are visibly at rest, instead of waiting out the remaining timing holds.
    /// Returns true when no bridge is wired so the flow is never blocked.
    /// </summary>
    /// <summary>
    /// True when the side's fighter is permanently dead. Used by the queue to cut a post-attack hold
    /// short on a lethal hit only (a dead fighter's KO pose never reports clip-finished). A knocked-down
    /// survivor must NOT be treated as dead, otherwise the getup fires mid-hit.
    /// </summary>
    private bool IsSideDead(PlayerUI.Side side)
    {
        var bridge = GetBridgeForSide(side);
        if (bridge == null) return false;
        try { return bridge.IsDead; }
        catch { return false; }
    }

    /// <summary>
    /// STRICT idle test used by the queue to decide when the camera may return to the centre: the side
    /// must literally be playing the Idle state, with no action in flight and not knocked down / mid-
    /// getup. The looser "idle + settled" check returned true during the transition frames of a hit /
    /// knockdown, which is why the camera snapped to the middle while a heavy hit reaction was still on
    /// screen.
    /// </summary>
    private bool IsSideIdleSettled(PlayerUI.Side side)
    {
        var bridge = GetBridgeForSide(side);
        if (bridge == null) return true;
        try { return bridge.IsFullyIdleNow; }
        catch { return true; }
    }

    /// <summary>
    /// Releases the camera's attack shot so it eases back to the midpoint between both fighters. Used
    /// as the CombatPositioningController attack-finished hook (an exchange has fully completed).
    /// </summary>
    private void EndCameraAttackFocus()
    {
        try { MortalKombatCamera.Instance?.EndAttackFocus(); }
        catch { }
    }

    /// <summary>
    /// True when this side already took a LETHAL hit for the exchange currently being animated
    /// (hpAfter reached 0). Checked when the exchange's victim animation is about to be fired, so a
    /// lethal light hit skips the hit reaction and collapses straight into the death pose, and a lethal
    /// heavy hit never gets chained a getup.
    /// </summary>
    private bool IsVictimLethalForCurrentExchange(PlayerUI.Side side)
    {
        var bridge = GetBridgeForSide(side);
        if (bridge != null && bridge.IsDead) return true;

        // The ONLY reliable lethal test for the CURRENT exchange is its own key. The old code also
        // matched a plain side flag (_lethalVictimSide), which was never cleared -- so once a fighter
        // had ever been hit lethally, EVERY later attack on that side was treated as lethal and the
        // death pose played while the fighter still had HP ("die qua som, chua het mau da die").
        if (!string.IsNullOrEmpty(_pendingExchangeKey) && _lethalExchangeKeys.Contains(_pendingExchangeKey)) return true;

        return false;
    }

    /// <summary>
    /// Plays an animation id on a side through that side's CharacterAnimatorBridge and returns the
    /// clip length in seconds (0 when unknown). Used as the CombatPositioningController playback hook.
    /// </summary>
    private float PlayAnimationForSide(PlayerUI.Side side, string animationId, float crossFade)
    {
        if (string.IsNullOrEmpty(animationId)) return 0f;

        var bridge = GetBridgeForSide(side);
        if (bridge == null) return 0f;

        string lower = animationId.ToLowerInvariant();
        try
        {
            if (lower.Contains("attack"))
            {
                // Fires the attack and returns the variant applied (paired below). Record it under the pending
                // exchange key so the matching DAMAGE_APPLIED (queued right after) reuses the same
                // variant -- this is where attackN/hitN pairing is established now that firing the
                // attack happens inside the queued move+animate item (after the move).
                int index = bridge.AttackFromAnimationId(animationId);
                _lastAttackVariant = index; // used to pair the victim's hit in the same exchange
                if (index > 0 && !string.IsNullOrEmpty(_pendingExchangeKey))
                {
                    _matchedVariantMap[_pendingExchangeKey] = index;
                    if (debugMode) Debug.Log($"Animation [GameManager] matched variant {index} stored for {_pendingExchangeKey}");
                }
            }
            else if (lower.Contains("hit"))
            {
                bool isHeavyHit = lower.Contains("heavy");

                // LETHAL GUARD -- LIGHT HITS ONLY.
                //
                // A light hit that drains the last HP skips its hit reaction and collapses straight into
                // the death pose (the tiny light reaction would otherwise play and the collapse only
                // happened afterwards, reading as a delayed death).
                //
                // A HEAVY hit ALWAYS plays its normal hit reaction, even when it is lethal -- the heavy
                // knockdown is the whole point of the animation, so it must not be replaced.
                if (!isHeavyHit && IsVictimLethalForCurrentExchange(side))
                {
                    if (debugMode) Debug.Log($"Animation [GameManager] lethal LIGHT hit on {side}: skipping hit reaction, playing KB_TopKO.");
                    _loserSideForDieAnimation = side;
                    bridge.TakeFatalHit();
                }
                else
                {
                    // Pair the hit reaction with the attack variant chosen moments earlier in the SAME
                    // exchange (attackN -> hitN). Without this the victim picks a random hit index and the
                    // two fighters can play mismatched variants.
                    int variant = _lastAttackVariant;
                    if (variant > 0)
                    {
                        bridge.TakeHitByAttackIndex(variant, isHeavyHit, animationId);
                    }
                    else
                    {
                        bridge.TakeHitFromAnimationId(animationId);
                    }
                }
            }
            else if (lower.Contains("getup") || lower.Contains("standup"))
            {
                // Route through GetupFromKnockdown so the same getup direction policy applies here too
                // (currently forced to type 1 so a fighter can never get stuck in a hit pose).
                bridge.GetupFromKnockdown();
            }
            else if (lower.Contains("die") || lower.Contains("ko"))
            {
                bridge.TakeFatalHit();
            }
            else
            {
                // Unknown id: treat it as a one-shot hit-style reaction so something still plays.
                bridge.TakeHitFromAnimationId(animationId);
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"Animation [GameManager] PlayAnimationForSide '{animationId}' on {side} failed: {ex}");
            return 0f;
        }

        bridge.BeginWatchCurrentAnimation();
        return bridge.GetCurrentAnimationDuration();
    }

    void Start()
    {
        // Re-wire now that ALL Awakes have run, so the positioning controller's hooks are guaranteed
        // to be assigned (the controller's own Awake may have been later than ours).
        WirePositioningHandlers();

        PrintDebugInfo();

        // Set default initial HP for both sides so UI shows full health immediately
        try
        {
            if (!_characterMaxHp.ContainsKey("bot_a")) _characterMaxHp["bot_a"] = defaultInitialMaxHpAtomic;
            if (!_characterMaxHp.ContainsKey("bot_b")) _characterMaxHp["bot_b"] = defaultInitialMaxHpAtomic;

            // Initialize UI health displays to full
            if (uiManager != null)
            {
                uiManager.ResetForNewMatch();
            }
            else
            {
                LegacyUI(PlayerUI.Side.Left)?.UpdateHealthDisplay(PlayerUI.Side.Left, defaultInitialMaxHpAtomic, defaultInitialMaxHpAtomic);
                LegacyUI(PlayerUI.Side.Right)?.UpdateHealthDisplay(PlayerUI.Side.Right, defaultInitialMaxHpAtomic, defaultInitialMaxHpAtomic);
            }
        }
        catch { }

        if (WebSocketManager.Instance != null)
        {
            WebSocketManager.Instance.OnRawMessageReceived += HandleRawMessage;
            if (!string.IsNullOrEmpty(initialMatchId))
            {
                if (WebSocketManager.Instance.IsConnected())
                {
                    _ = WebSocketManager.Instance.SubscribeToMatch(initialMatchId, 0);
                }
                else
                {
                    WebSocketManager.Instance.OnConnectionProgress += WaitAndSubscribe;
                }
            }
        }



    }

    void OnDestroy()
    {
        if (WebSocketManager.Instance != null)
        {
            WebSocketManager.Instance.OnRawMessageReceived -= HandleRawMessage;
            WebSocketManager.Instance.OnConnectionProgress -= WaitAndSubscribe;
        }
        if (Instance == this) Instance = null;
    }
    // Resolves the UI manager: inspector-assigned first, else the scene singleton.
    private MemeBattleUI UI => uiManager != null ? uiManager : MemeBattleUI.Instance;

    // Converts the game's PlayerUI.Side enum to the UIManager side enum.
    private static MemeBattleUI.Side ToUISide(PlayerUI.Side side)
    {
        return side == PlayerUI.Side.Left ? MemeBattleUI.Side.Left : MemeBattleUI.Side.Right;
    }

    // Legacy-only UI resolver. Prefer uiManager; this exists so old scenes still function.
    private PlayerUI LegacyUI(PlayerUI.Side side) => playerUI;

    // Helper to resolve the optional CharacterAnimatorBridge for a given side (may be null).
    private CharacterAnimatorBridge GetBridgeForSide(PlayerUI.Side side)
    {
        return side == PlayerUI.Side.Left ? leftBridge : rightBridge;
    }

    // Lazily resolve the positioning controller so older scenes without an explicit reference work.
    private CombatPositioningController Positioning
    {
        get
        {
            if (positioningController == null)
                positioningController = CombatPositioningController.Instance;
            return positioningController;
        }
    }

    /// <summary>
    /// Resolves the world Transform that represents a side's fighter: the bridge's Animator (the
    /// object the animations actually drive), else the positioning controller's fighter root.
    /// Returns null when nothing is wired up.
    /// </summary>
    private Transform GetFighterTransform(PlayerUI.Side side)
    {
        var bridge = GetBridgeForSide(side);
        if (bridge != null && bridge.Animator != null) return bridge.Animator.transform;

        return Positioning?.GetFighter(side);
    }


    void WaitAndSubscribe(float p, string status)
    {
        if (p >= 1f && !string.IsNullOrEmpty(initialMatchId))
        {
            _ = WebSocketManager.Instance.SubscribeToMatch(initialMatchId, 0);
            WebSocketManager.Instance.OnConnectionProgress -= WaitAndSubscribe;
        }
    }

    // Public API for forwarding buffered messages
    public void ApplyRawMessage(string json)
    {
        HandleRawMessage(json);
    }

    // Debug helpers
    public void DebugTriggerQ()
    {
        // Local test: Left attacks Right, routed through the same queue the backend events use.
        Debug.Log("[DebugTrigger] Q -> Left attacks Right");
        RouteCombatAction(PlayerUI.Side.Left, PlayerUI.Side.Right, "attack1", "hit1");
    }

    public void DebugTriggerE()
    {
        // Local test: Right attacks Left.
        Debug.Log("[DebugTrigger] E -> Right attacks Left");
        RouteCombatAction(PlayerUI.Side.Right, PlayerUI.Side.Left, "attack1", "hit1");
    }

    void Update()
    {
        // SAFETY FLUSH: the FINAL turn of a match has no following TURN_STARTED to trigger its flush, so
        // we flush a buffered turn once its deadline passes. The deadline is pushed back on every new
        // event of the same turn, so this only fires when the turn has really stopped arriving.
        if (_turnFlushScheduled && Time.unscaledTime >= _turnFlushDeadline)
        {
            FlushBufferedTurn("idle timeout");
        }

        if (!enableLocalInputTesting) return;
        if (IsKeyDown(KeyCode.Q))
        {
            RouteCombatAction(PlayerUI.Side.Left, PlayerUI.Side.Right, "attack1", "hit1");
            MortalKombatCamera.Instance?.TriggerImpactShake();
        }
        if (IsKeyDown(KeyCode.E))
        {
            RouteCombatAction(PlayerUI.Side.Right, PlayerUI.Side.Left, "attack1", "hit1");
            MortalKombatCamera.Instance?.TriggerImpactShake();
        }
    }

    // Input helper (kept existing behavior)
    private bool _inputChecked = false;
    private bool _useNewInputSystem = false;
    private Type _keyboardType;
    private PropertyInfo _keyboardCurrentProp;
    private PropertyInfo _keyQProp;
    private PropertyInfo _keyEProp;
    private PropertyInfo _wasPressedProp;

    bool IsKeyDown(KeyCode key)
    {
        if (!_inputChecked)
        {
            try
            {
                bool v = Input.GetKeyDown(key);
                _useNewInputSystem = false;
                _inputChecked = true;
                Debug.Log("GameManager: Input system check - using legacy Input");
                return v;
            }
            catch (System.InvalidOperationException)
            {
                _useNewInputSystem = true;
                _inputChecked = true;
                Debug.Log("GameManager: falling back to new Input System (reflection)");
            }
        }

        if (_useNewInputSystem)
        {
            try
            {
                if (_keyboardType == null)
                {
                    _keyboardType = Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem") ?? Type.GetType("UnityEngine.InputSystem.Keyboard, UnityEngine.InputSystem");
                    if (_keyboardType != null)
                    {
                        _keyboardCurrentProp = _keyboardType.GetProperty("current", BindingFlags.Public | BindingFlags.Static);
                        _keyQProp = _keyboardType.GetProperty("qKey");
                        _keyEProp = _keyboardType.GetProperty("eKey");
                        var keyControlType = Type.GetType("UnityEngine.InputSystem.Controls.KeyControl, Unity.InputSystem") ?? Type.GetType("UnityEngine.InputSystem.Controls.KeyControl, UnityEngine.InputSystem");
                        if (keyControlType != null) _wasPressedProp = keyControlType.GetProperty("wasPressedThisFrame", BindingFlags.Public | BindingFlags.Instance);
                    }
                }

                if (_keyboardType == null || _keyboardCurrentProp == null) return false;
                var current = _keyboardCurrentProp.GetValue(null);
                if (current == null) return false;

                PropertyInfo keyProp = null;
                if (key == KeyCode.Q) keyProp = _keyQProp; else if (key == KeyCode.E) keyProp = _keyEProp; else return false;
                if (keyProp == null || _wasPressedProp == null) return false;
                var keyControl = keyProp.GetValue(current);
                var val = _wasPressedProp.GetValue(keyControl);
                return val is bool b && b;
            }
            catch { return false; }
        }

        return Input.GetKeyDown(key);
    }

    void HandleRawMessage(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        Debug.Log($"GameManager INCOMING [{System.DateTime.UtcNow:o}]: HandleRawMessage raw={json}");
        try
        {
            var j = JObject.Parse(json);
            var type = j.Value<string>("type");
            if (!string.IsNullOrEmpty(type))
            {
                switch (type)
                {
                    case "meme_battle_start_result":
                        {
                            // payload may be under j["result"] or under j["raw"] as a string
                            JToken res = j["result"];
                            if (res == null)
                            {
                                var raw = j.Value<string>("raw");
                                if (!string.IsNullOrEmpty(raw))
                                {
                                    try { var parsed = JToken.Parse(raw); if (parsed.Type == JTokenType.Array && parsed.HasValues) res = parsed.First; else res = parsed; } catch { }
                                }
                            }

                            if (res != null && res.Type == JTokenType.Object)
                            {
                                var err = res["error"];
                                if (err != null)
                                {
                                    var code = err.Value<string>("code");
                                    var msg = err.Value<string>("message");
                                    Debug.LogError($"GameManager: start_result error code={code} message={msg}");
                                    // try to show message on loading UI if present
                                    var lm = UnityEngine.Object.FindObjectOfType<LoaddingManager>();
                                    if (lm != null) lm.HandleExternalError(code ?? "MATCH_START_FAILED", msg ?? "Start match failed");
                                    break;
                                }

                                // success: update match snapshot if provided
                                var matchId = res.Value<string>("matchId");
                                // If server provided characterIds here, capture mapping for side keys
                                try
                                {
                                    var charIds = res["characterIds"] as JArray ?? j["characterIds"] as JArray;
                                    if (charIds != null && charIds.Count >= 2)
                                    {
                                        // Be defensive: server may send explicit side keys like "bot_b","bot_a"
                                        // If values are the literal side keys, map them to the correct side.
                                        string first = charIds[0].ToString();
                                        string second = charIds[1].ToString();
                                        // If server returned literal side keys ("bot_a","bot_b") then treat as no-op
                                        // and keep client-side default side mapping. Otherwise assume array order = left, right.
                                        var fLower = first?.ToLowerInvariant();
                                        var sLower = second?.ToLowerInvariant();
                                        if (fLower == "bot_a" && sLower == "bot_b")
                                        {
                                            // provided in expected order; nothing to change
                                        }
                                        else if (fLower == "bot_b" && sLower == "bot_a")
                                        {
                                            // server gave reversed side-key tokens; ignore to avoid creating confusing mappings
                                        }
                                        else
                                        {
                                            // Fallback: assume server array order = left, right (map actual character ids)
                                            _characterNames["bot_a"] = first;
                                            _characterNames["bot_b"] = second;
                                        }
                                    }
                                }
                                catch { }

                                var snapshotToken = res["snapshot"];
                                if (snapshotToken != null && snapshotToken.Type == JTokenType.Object)
                                {
                                    try
                                    {
                                        var startSnap = snapshotToken.ToObject<MemeBattleMatchSnapshot>();
                                        if (startSnap != null)
                                        {
                                            _latestSnapshot = startSnap;
                                            // store max HP map locally
                                            _characterMaxHp = startSnap.characterMaxHpAtomic ?? new System.Collections.Generic.Dictionary<string, long>();
                                            // ensure both side-keys and actual character ids exist in _characterMaxHp
                                            try
                                            {
                                                if (startSnap.characterMaxHpAtomic != null)
                                                {
                                                    foreach (var kv in startSnap.characterMaxHpAtomic)
                                                    {
                                                        if (!_characterMaxHp.ContainsKey(kv.Key)) _characterMaxHp[kv.Key] = kv.Value;
                                                    }
                                                }
                                                if (_characterNames.TryGetValue("bot_a", out var aid) && !_characterMaxHp.ContainsKey(aid))
                                                {
                                                    // try to copy from 'bot_a' if exists
                                                    if (startSnap.characterMaxHpAtomic != null && startSnap.characterMaxHpAtomic.TryGetValue("bot_a", out var ma)) _characterMaxHp[aid] = ma;
                                                }
                                                if (_characterNames.TryGetValue("bot_b", out var bid) && !_characterMaxHp.ContainsKey(bid))
                                                {
                                                    if (startSnap.characterMaxHpAtomic != null && startSnap.characterMaxHpAtomic.TryGetValue("bot_b", out var mb)) _characterMaxHp[bid] = mb;
                                                }
                                            }
                                            catch { }
                                            if (startSnap.characterHpAtomic != null)
                                            {
                                                UpdatePlayerHpFromSnapshot("bot_a", PlayerUI.Side.Left, startSnap);
                                                UpdatePlayerHpFromSnapshot("bot_b", PlayerUI.Side.Right, startSnap);
                                            }
                                            if (startSnap.currentTurn != null)
                                            {
                                                roundManager?.StartTurnTimer(startSnap.currentTurn.turnNumber, startSnap.currentTurn.opensAt, startSnap.currentTurn.closesAt);
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.LogWarning($"GameManager: failed to apply start_result snapshot: {ex}");
                                    }
                                }
                            }
                            break;
                        }
                    case "meme_battle_snapshot":
                        var snap = j["snapshot"].ToObject<MemeBattleMatchSnapshot>();
                        if (snap != null)
                        {
                            _latestSnapshot = snap;
                            _characterMaxHp = snap.characterMaxHpAtomic ?? new System.Collections.Generic.Dictionary<string, long>();
                            // Ensure mapping under side keys if we know characterIds
                                try
                                {
                                    // if top-level contains characterIds, capture mapping
                                    var chars = j["characterIds"] as JArray;
                                    if (chars != null && chars.Count >= 2)
                                    {
                                        string first = chars[0].ToString();
                                        string second = chars[1].ToString();
                                        // If server returned literal side keys ("bot_a","bot_b") then treat as no-op
                                        // and keep client-side default side mapping. Otherwise assume array order = left, right.
                                        var fLower = first?.ToLowerInvariant();
                                        var sLower = second?.ToLowerInvariant();
                                        if (fLower == "bot_a" && sLower == "bot_b")
                                        {
                                            // provided in expected order; nothing to change
                                        }
                                        else if (fLower == "bot_b" && sLower == "bot_a")
                                        {
                                            // server gave reversed side-key tokens; ignore to avoid creating confusing mappings
                                        }
                                        else
                                        {
                                            // Fallback: assume server array order = left, right (map actual character ids)
                                            _characterNames["bot_a"] = first;
                                            _characterNames["bot_b"] = second;
                                        }
                                    }

                                    if (snap.characterMaxHpAtomic != null)
                                    {
                                        foreach (var kv in snap.characterMaxHpAtomic)
                                        {
                                            if (!_characterMaxHp.ContainsKey(kv.Key)) _characterMaxHp[kv.Key] = kv.Value;
                                        }
                                        if (_characterNames.TryGetValue("bot_a", out var aid) && !_characterMaxHp.ContainsKey(aid))
                                        {
                                            if (snap.characterMaxHpAtomic.TryGetValue("bot_a", out var ma)) _characterMaxHp[aid] = ma;
                                        }
                                        if (_characterNames.TryGetValue("bot_b", out var bid) && !_characterMaxHp.ContainsKey(bid))
                                        {
                                            if (snap.characterMaxHpAtomic.TryGetValue("bot_b", out var mb)) _characterMaxHp[bid] = mb;
                                        }
                                    }
                                }
                                catch { }

                            if (snap.characterHpAtomic != null)
                            {
                                UpdatePlayerHpFromSnapshot("bot_a", PlayerUI.Side.Left, snap);
                                UpdatePlayerHpFromSnapshot("bot_b", PlayerUI.Side.Right, snap);
                            }
                            if (snap.currentTurn != null)
                            {
                                roundManager?.StartTurnTimer(snap.currentTurn.turnNumber, snap.currentTurn.opensAt, snap.currentTurn.closesAt);
                            }
                        }
                        break;
                    case "meme_battle_event":
                        var ev = j["event"].ToObject<MemeBattleEvent>();
                        if (ev != null) HandleMemeBattleEvent(ev);
                        break;
                    case "error":
                        Debug.LogError($"GameManager: socket error: {j.Value<string>("error")}");
                        break;
                    default:
                        break;
                }
                return;
            }
        }
        catch (Exception)
        {
            // legacy
        }

        string evtName = ExtractEventName(json);
        if (string.IsNullOrEmpty(evtName)) return;
        switch (evtName)
        {
            case "playerLeftAttack":
                int dmg = ExtractInt(json, "damage");
                PlaySingleAnimation(PlayerUI.Side.Left, "LeftAttack");
                    uiManager?.ShowDamage(MemeBattleUI.Side.Left, dmg);
                break;
            case "playerRightHit":
                int dmg2 = ExtractInt(json, "damage");
                PlaySingleAnimation(PlayerUI.Side.Right, "RightHit");
                    uiManager?.ShowDamage(MemeBattleUI.Side.Right, dmg2);
                break;
            case "playerLeftAnswer":
                string text = ExtractString(json, "answer") ?? ExtractString(json, "text");
                    uiManager?.SetDialogue(MemeBattleUI.Side.Left, text);
                PlaySingleAnimation(PlayerUI.Side.Left, "Talk");
                break;
            case "ShowQuestion":
            case "question_start":
                string q = ExtractString(json, "question") ?? ExtractString(json, "text");
                roundManager?.ShowQuestion(q);
                break;
            default:
                break;
        }
    }

    void UpdatePlayerHpFromSnapshot(string characterId, PlayerUI.Side side, MemeBattleMatchSnapshot snap)
    {
        if (snap.characterHpAtomic != null)
        {
            string lookupId = characterId;
            // If snapshot doesn't have the provided key, try mapping from known side->characterId
            if (!snap.characterHpAtomic.ContainsKey(lookupId) && _characterNames.TryGetValue(characterId, out var mapped))
            {
                lookupId = mapped;
            }

            if (snap.characterHpAtomic.TryGetValue(lookupId, out long hp))
            {
                long max = 0;
                if (snap.characterMaxHpAtomic != null) snap.characterMaxHpAtomic.TryGetValue(lookupId, out max);
                if (max <= 0) max = hp;
                uiManager?.UpdateHealth(ToUISide(side), hp, max);
            }
        }

        // NOTE: UpdatePlayerHealthDisplay(snap) used to be called here as well. It is a SECOND writer
        // of the SAME two bars, and it reads from the snapshot rather than from this call's side/hp
        // pair -- so wherever the snapshot's key order or mapping differed it silently overwrote the
        // value just written, which is how a side could appear to lose HP it never lost. One snapshot
        // application now writes each bar exactly once, through the side it was resolved for.
    }

    /// <summary>
    /// Entry point for a gameplay event.
    ///
    /// ONE TURN = ONE STACK: every event of a turn (they all share the turnId) is BUFFERED here instead of
    /// being queued individually. The buffered turn is flushed as ONE single queue stack either when the
    /// NEXT turn's first event arrives (a new turnId) or, for the final turn, after
    /// <see cref="turnStackFlushDelay"/> of silence. The queue then runs the whole turn as one atomic
    /// beat: UI/dialogue + move + attack/hit pair + the single HP write + getup.
    ///
    /// This is what guarantees the turn never interleaves with the next turn, and that HP is deducted
    /// EXACTLY ONCE per hit -- the HP write happens inside the stack, driven by the turn's HP event.
    /// </summary>
    void HandleMemeBattleEvent(MemeBattleEvent ev)
    {
        if (ev == null) return;

        // WINNER_DECLARED / match-level events are NOT part of a turn's stack: flush whatever turn is
        // pending first so the result only plays after the last turn has fully resolved, then queue the
        // result event on its own.
        if (ev.eventType == "WINNER_DECLARED" || string.IsNullOrEmpty(ev.turnId))
        {
            FlushBufferedTurn("non-turn event: " + ev.eventType);
            EnqueueSingleEventStack(ev);
            return;
        }

        // A DIFFERENT turnId means the previous turn is complete: flush it as one stack before we start
        // buffering this new turn. This is the primary turn boundary.
        if (!string.IsNullOrEmpty(_bufferedTurnId) &&
            !string.Equals(_bufferedTurnId, ev.turnId, StringComparison.Ordinal))
        {
            FlushBufferedTurn($"turn changed to {ev.turnId}");
        }

        // Buffer this event into the current turn.
        _bufferedTurnId = ev.turnId;
        _bufferedTurnEvents.Add(ev);

        // (Re)arm the safety flush: if no further event of this turn arrives, flush it after the delay.
        _turnFlushScheduled = true;
        _turnFlushDeadline = Time.unscaledTime + Mathf.Max(0.05f, turnStackFlushDelay);
    }

    /// <summary>
    /// Flushes the buffered turn as ONE queue stack. Does nothing when no turn is buffered.
    /// </summary>
    private void FlushBufferedTurn(string reason)
    {
        if (_bufferedTurnEvents.Count == 0)
        {
            _bufferedTurnId = null;
            _turnFlushScheduled = false;
            return;
        }

        string turnId = _bufferedTurnId;
        var events = new System.Collections.Generic.List<MemeBattleEvent>(_bufferedTurnEvents);

        _bufferedTurnId = null;
        _bufferedTurnEvents.Clear();
        _turnFlushScheduled = false;

        if (debugMode)
            Debug.Log($"[TurnStack] flushing turn '{turnId}' ({events.Count} events) -> {reason}");

        EnqueueTurnStack(turnId, events);
    }

    /// <summary>
    /// Builds and enqueues the SINGLE stack for one turn. The stack is one queue item whose apply step:
    ///   1) applies the turn's non-animation events in order (TURN_STARTED, MULTIPLIER_SELECTED, dialogue,
    ///      camera framing), fully state-driven;
    ///   2) plays the ARGUMENT_SELECTED attack/hit pair (moved to the attack spot by the queue);
    ///   3) writes the turn's HP change EXACTLY ONCE, from the turn's HP event;
    ///   4) routes the hit reaction / death pose / getup.
    /// Because it is one item, the next turn's stack cannot start until this one has finished.
    /// </summary>
    private void EnqueueTurnStack(string turnId, System.Collections.Generic.List<MemeBattleEvent> events)
    {
        var pos = Positioning;

        // Identify the turn's key events up front.
        MemeBattleEvent turnStarted = null;
        MemeBattleEvent multiplier = null;
        MemeBattleEvent argumentSelected = null;
        MemeBattleEvent damageApplied = null;
        MemeBattleEvent hpChanged = null;
        foreach (var e in events)
        {
            switch (e.eventType)
            {
                case "TURN_STARTED": turnStarted = e; break;
                case "MULTIPLIER_SELECTED": multiplier = e; break;
                case "ARGUMENT_SELECTED": argumentSelected = e; break;
                case "DAMAGE_APPLIED": damageApplied = e; break;
                case "HP_CHANGED": hpChanged = e; break;
            }
        }

        // ---- Derived exchange info (who attacks whom, with what) -------------------------------------
        string animationId = argumentSelected?.payload?.Value<string>("animationId");
        PlayerUI.Side? attackerSide = SideFromCharacterId(argumentSelected?.payload?.Value<string>("actorCharacterId"));
        PlayerUI.Side? victimSide = SideFromCharacterId(argumentSelected?.payload?.Value<string>("targetCharacterId"));

        // The HP target/damage for this turn. HP comes from the turn's HP event (HP_CHANGED carries the
        // authoritative characterId + hpAfterAtomic). When only DAMAGE_APPLIED is present we fall back to
        // its target/hpAfterAtomic so the turn still lands. In BOTH cases the value is applied ONCE here.
        string hpTargetId = hpChanged?.payload?.Value<string>("characterId");
        long hpAfter = hpChanged?.payload?.Value<long?>("hpAfterAtomic") ?? -1;
        if (string.IsNullOrEmpty(hpTargetId) && damageApplied != null)
        {
            hpTargetId = damageApplied.payload?.Value<string>("targetCharacterId");
            hpAfter = damageApplied.payload?.Value<long?>("hpAfterAtomic") ?? -1;
        }
        PlayerUI.Side? hpSide = SideFromCharacterId(hpTargetId);

        long damageAmount = damageApplied?.payload?.Value<long?>("damageAtomic") ?? 0;

        // Bridges this turn animates (attacker + victim) -- used to wait the clips out precisely.
        var watched = new System.Collections.Generic.List<CharacterAnimatorBridge>();
        if (argumentSelected != null)
        {
            AddBridge(watched, argumentSelected.payload?.Value<string>("actorCharacterId"));
            AddBridge(watched, argumentSelected.payload?.Value<string>("targetCharacterId"));
        }
        if (damageApplied != null)
        {
            AddBridge(watched, damageApplied.payload?.Value<string>("targetCharacterId"));
        }

        // Whether this turn has an actual attack exchange to move+animate.
        bool hasExchange = !string.IsNullOrEmpty(animationId) && attackerSide.HasValue && victimSide.HasValue;
        string hitAnimationId = hasExchange
            ? System.Text.RegularExpressions.Regex.Replace(animationId, "attack", "hit", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            : null;

        string exchangeKey = !string.IsNullOrEmpty(turnId) && hasExchange
            ? $"{turnId}:{argumentSelected.payload?.Value<string>("actorCharacterId")}:{argumentSelected.payload?.Value<string>("targetCharacterId")}"
            : null;

        bool lethalThisTurn = hpAfter == 0;
        if (lethalThisTurn && !string.IsNullOrEmpty(exchangeKey)) _lethalExchangeKeys.Add(exchangeKey);

        // ---- The turn's single apply step -------------------------------------------------------------
        Action apply = () =>
        {
            // 1) Turn bookkeeping / non-animation UI (turn timer, dialogue reset, multiplier).
            if (turnStarted != null) ApplyMemeBattleEvent(turnStarted);
            if (multiplier != null) ApplyMemeBattleEvent(multiplier);

            // 2) The attack itself (dialogue, camera framing, meme popup, totals). The MOVE + the
            //    attack/hit clip pair are issued by the queue around this call.
            if (argumentSelected != null)
            {
                _pendingExchangeKey = exchangeKey;
                ApplyMemeBattleEvent(argumentSelected);
            }

            // 3) THE SINGLE HP WRITE FOR THIS TURN. Applied exactly once, here, inside the stack.
            ApplyTurnHpOnce(turnId, hpTargetId, hpAfter, damageAmount, damageApplied?.payload?.Value<string>("animationId") ?? animationId);

            // 4) Death pose / hit routing that depends on the damage event.
            if (damageApplied != null)
            {
                RouteTurnDamageReaction(damageApplied, victimSide, hpSide, exchangeKey, hpAfter, hasExchange);
            }

            for (int i = 0; i < watched.Count; i++)
                watched[i]?.BeginWatchCurrentAnimation();
        };

        Func<bool> finished = watched.Count == 0
            ? (Func<bool>)null
            : () => AllBridgesFinished(watched);

        float duration = hasExchange ? Mathf.Max(EstimateAnimationWait(argumentSelected), EstimateAnimationWait(damageApplied)) : 0f;

        if (!useSequentialEventQueue || pos == null)
        {
            // Legacy / no-queue fallback: apply the whole turn immediately, in order.
            WirePositioningHandlers();
            try { apply(); } catch (Exception ex) { Debug.LogWarning($"GameManager: turn stack apply failed: {ex}"); }
            return;
        }

        WirePositioningHandlers();
        pos.SetFighters(GetFighterTransform(PlayerUI.Side.Left), GetFighterTransform(PlayerUI.Side.Right));

        if (hasExchange)
        {
            // HEAVY exchanges knock the victim down: chain the getup INSIDE this same stack so the victim
            // stands back up before the next turn's stack can run (the fighter must never attack while
            // still on the ground). Lethal turns never chain a getup (the death pose owns the fighter).
            bool isHeavyExchange = animationId.ToLowerInvariant().Contains("heavy");
            Action afterExchange = null;
            float afterHold = 0f;
            var knockedBridge = GetBridgeForSide(victimSide.Value);
            if (isHeavyExchange && autoGetupAfterKnockdown && knockedBridge != null && !lethalThisTurn)
            {
                if (!string.IsNullOrEmpty(exchangeKey)) _exchangeGetupHandled.Add(exchangeKey);
                afterExchange = () =>
                {
                    bool lethal = knockedBridge == null || knockedBridge.IsDead ||
                                  (exchangeKey != null && _lethalExchangeKeys.Contains(exchangeKey));
                    if (lethal)
                    {
                        if (debugMode) Debug.Log($"Animation [GameManager] chained getup suppressed: lethal hit on {victimSide}.");
                        return;
                    }
                    try { knockedBridge.GetupFromKnockdown(); }
                    catch (Exception ex) { Debug.LogWarning($"Animation [GameManager] chained getup failed: {ex}"); }
                };
                afterHold = Mathf.Max(0f, getupAfterHitDelay) + Mathf.Max(0f, getupDelay);
            }

            pos.EnqueueMoveThenAnimate($"TURN {turnId}", apply, attackerSide.Value, victimSide.Value,
                                       animationId, hitAnimationId, finished, duration,
                                       afterExchange, afterHold, Mathf.Max(0f, getupSettleSeconds));
        }
        else
        {
            // No attack exchange this turn (e.g. only a TURN_STARTED or an HP-only turn): still one stack.
            pos.EnqueueWork($"TURN {turnId}", apply, finished, duration);
        }
    }

    /// <summary>
    /// Writes a turn's HP change to the bar EXACTLY ONCE. Guarded by (turnId:target) so no re-delivery,
    /// re-flush or second event of the same turn can move the bar a second time. This is the ONLY place
    /// that applies per-turn HP, so "trừ máu 1 lần khi bị hit" holds by construction.
    /// </summary>
    private void ApplyTurnHpOnce(string turnId, string targetId, long hpAfter, long damage, string animationIdForWeight)
    {
        if (hpAfter < 0 || string.IsNullOrEmpty(targetId)) return;

        var side = SideFromCharacterId(targetId);
        if (!side.HasValue)
        {
            Debug.LogWarning($"Animation [GameManager] turn HP ignored: could not resolve '{targetId}' to a side.");
            return;
        }

        string hpKey = !string.IsNullOrEmpty(turnId) ? $"{turnId}:{targetId}" : null;
        if (!string.IsNullOrEmpty(hpKey) && !_hpChangedAppliedKeys.Add(hpKey))
        {
            if (debugMode) Debug.Log($"Animation [GameManager] turn HP for {targetId} ignored: '{hpKey}' already applied.");
            return;
        }

        // Record the max candidate from any known hp value so the bar's max stays stable.
        EnsureKnownMaxHp(targetId, 0, hpAfter);

        ApplyHealthToUi(side.Value, hpAfter);
        if (damage > 0) uiManager?.ShowDamage(ToUISide(side.Value), (int)damage);

        if (debugMode)
            Debug.Log($"Animation [GameManager] turn HP applied ONCE for {targetId}: -{damage} = {hpAfter}");
    }

    /// <summary>
    /// Routes the DAMAGE_APPLIED reaction for a turn: death pose on a lethal hit, otherwise the hit
    /// reaction paired to the attack variant (attackN -> hitN). Does NOT touch HP -- the single HP write
    /// for the turn happened in <see cref="ApplyTurnHpOnce"/>.
    /// </summary>
    private void RouteTurnDamageReaction(MemeBattleEvent damageApplied, PlayerUI.Side? victimSide,
                                         PlayerUI.Side? hpSide, string exchangeKey, long hpAfter,
                                         bool hasExchange)
    {
        var payload = damageApplied?.payload;
        if (payload == null) return;

        string targetId = payload.Value<string>("targetCharacterId");
        string animationId = payload.Value<string>("animationId");
        var side = SideFromCharacterId(targetId);
        if (!side.HasValue) return;

        // Is this hit HEAVY? The BE animationId carries the weight ("...heavy").
        bool isHeavy = !string.IsNullOrEmpty(animationId) && animationId.ToLowerInvariant().Contains("heavy");

        // LETHAL DEATH POSE -- LIGHT HITS ONLY, and ONLY when this turn has NO attack exchange.
        //
        // When the turn HAS an exchange, the victim's reaction clip is fired by the exchange's animate
        // step via PlayAnimationForSide -- which already implements the rule we want:
        //   * lethal LIGHT hit -> skip the light reaction, play the death pose;
        //   * lethal HEAVY hit -> play the normal (knockdown) hit reaction, NO death pose.
        // Forcing the death pose HERE as well would both double-trigger it and (because apply runs before
        // the animate step) pre-empt the heavy hit reaction -- so we leave the exchange path alone.
        //
        // The no-exchange path (a DAMAGE_APPLIED with no ARGUMENT_SELECTED) has no clip of its own, so we
        // resolve the reaction here: lethal light -> death pose; heavy -> (handled by WINNER_DECLARED later).
        if (hasExchange)
        {
            // Exchange path: the clip + lethal rule were already applied by the exchange step. Nothing to
            // do for the reaction here.
            return;
        }

        if (hpAfter == 0 && !isHeavy)
        {
            if (!_loserSideForDieAnimation.HasValue || _loserSideForDieAnimation != side)
            {
                _loserSideForDieAnimation = side;
                if (debugMode) Debug.Log($"Animation [GameManager] lethal LIGHT hit (no exchange) -> KB_TopKO (die) on side={side}");
                GetBridgeForSide(side.Value)?.TakeFatalHit();
            }
            return;
        }

        if (isHeavy)
        {
            if (debugMode) Debug.Log($"Animation [GameManager] heavy hit on {targetId} (hp={hpAfter}) with no exchange -> normal hit kept, no forced death pose.");
            return;
        }

        // Non-lethal LIGHT with NO exchange: no hit clip of its own was fired, so schedule the getup for a
        // knocked-down survivor directly.
        var targetBridge = GetBridgeForSide(side.Value);
        if (targetBridge == null) return;

        bool knockedDownNow = targetBridge.IsKnockedDown ||
                              (targetBridge.Animator != null && targetBridge.Animator.GetBool("IsKnockedDown"));

        if (autoGetupAfterKnockdown && knockedDownNow)
            ScheduleGetupThroughQueue(side.Value, targetBridge, getupType, Mathf.Max(0f, getupDelay));
    }

    /// <summary>
    /// Queues a single event that is NOT part of a turn (e.g. WINNER_DECLARED) as its own stack.
    /// </summary>
    private void EnqueueSingleEventStack(MemeBattleEvent ev)
    {
        var watched = BridgesForEvent(ev);
        Action apply = () =>
        {
            ApplyMemeBattleEvent(ev);
            for (int i = 0; i < watched.Count; i++)
                watched[i]?.BeginWatchCurrentAnimation();
        };
        Func<bool> finished = watched.Count == 0 ? (Func<bool>)null : () => AllBridgesFinished(watched);
        float duration = EstimateEventDuration(ev);

        var pos = Positioning;
        if (!useSequentialEventQueue || pos == null)
        {
            try { apply(); } catch (Exception ex) { Debug.LogWarning($"GameManager: event stack apply failed: {ex}"); }
            return;
        }

        WirePositioningHandlers();
        pos.SetFighters(GetFighterTransform(PlayerUI.Side.Left), GetFighterTransform(PlayerUI.Side.Right));
        pos.EnqueueWork($"{ev.eventType} seq={ev.sequence}", apply, finished, duration);
    }

    /// <summary>
    /// Collects the distinct bridges involved in the given event. Only events that actually
    /// drive an animation will return entries; UI-only events return an empty list.
    /// </summary>
    System.Collections.Generic.List<CharacterAnimatorBridge> BridgesForEvent(MemeBattleEvent ev)
    {
        var list = new System.Collections.Generic.List<CharacterAnimatorBridge>();
        if (ev?.payload == null) return list;

        switch (ev.eventType)
        {
            case "ARGUMENT_SELECTED":
            case "DAMAGE_APPLIED":
            {
                AddBridge(list, ev.payload.Value<string>("actorCharacterId"));
                AddBridge(list, ev.payload.Value<string>("targetCharacterId"));
                break;
            }
            case "WINNER_DECLARED":
            {
                AddBridge(list, ev.payload.Value<string>("winnerCharacterId"));
                // The loser plays the death pose; consider both fighters so the queue waits it out.
                AddBridge(list, _characterNames.TryGetValue("bot_a", out var a) ? a : "bot_a");
                AddBridge(list, _characterNames.TryGetValue("bot_b", out var b) ? b : "bot_b");
                break;
            }
        }
        return list;
    }

    void AddBridge(System.Collections.Generic.List<CharacterAnimatorBridge> list, string characterId)
    {
        var side = SideFromCharacterId(characterId);
        if (!side.HasValue) return;
        var bridge = GetBridgeForSide(side.Value);
        if (bridge != null && !list.Contains(bridge)) list.Add(bridge);
    }

    static bool AllBridgesFinished(System.Collections.Generic.List<CharacterAnimatorBridge> bridges)
    {
        for (int i = 0; i < bridges.Count; i++)
        {
            var b = bridges[i];
            if (b == null) continue;
            if (!b.IsAnimationFinished()) return false;
        }
        return true;
    }

    /// <summary>
    /// Estimates how long the queue should wait after applying this event, based on the
    /// animation it will trigger. Uses the longest of the involved fighters' current animation
    /// lengths, with a sane fallback for animation-less events. This is the SAFETY upper bound
    /// that complements the precise animation-finished predicate.
    /// </summary>
    float EstimateEventDuration(MemeBattleEvent ev)
    {
        if (ev == null) return 0f;
        // (keep in sync with the ApplyMemeBattleEvent switch below)
        switch (ev.eventType)
        {
            case "MATCH_CREATED":
            case "MATCH_STARTED":
            case "TURN_STARTED":
            case "MULTIPLIER_SELECTED":
            case "HP_CHANGED":
                // No animation -> apply promptly (still ordered).
                return 0f;

            case "ARGUMENT_SELECTED":
            case "DAMAGE_APPLIED":
            case "WINNER_DECLARED":
                return EstimateAnimationWait(ev);

            default:
                return 0f;
        }
    }

    /// <summary>
    /// Picks the longest recommended wait across the bridges involved in this event
    /// (attacker/target for arguments and damage, loser/winner for the result).
    /// </summary>
    float EstimateAnimationWait(MemeBattleEvent ev)
    {
        var payload = ev.payload;
        if (payload == null) return 0.6f;

        float wait = 0f;

        string actorId = payload.Value<string>("actorCharacterId");
        string targetId = payload.Value<string>("targetCharacterId");
        string winnerId = payload.Value<string>("winnerCharacterId");
        string charId = payload.Value<string>("characterId");

        // Consider every character referenced by the event that has a bridge.
        wait = Mathf.Max(wait, WaitForCharacter(actorId));
        wait = Mathf.Max(wait, WaitForCharacter(targetId));
        wait = Mathf.Max(wait, WaitForCharacter(winnerId));
        wait = Mathf.Max(wait, WaitForCharacter(charId));

        // Both fighters are involved in a turn exchange -> fall back to the shorter side if neither
        // resolved, so the flow still paces.
        if (wait <= 0f) wait = 0.6f;
        return wait;
    }

    float WaitForCharacter(string characterId)
    {
        PlayerUI.Side? side = SideFromCharacterId(characterId);
        if (!side.HasValue) return 0f;
        CharacterAnimatorBridge bridge = GetBridgeForSide(side.Value);
        if (bridge == null) return 0f;
        return bridge.GetRecommendedWaitSeconds();
    }

    /// <summary>
    /// Resolves the max HP to display for a character, using every source in the same priority order so
    /// DAMAGE_APPLIED, HP_CHANGED and the snapshot all agree (the bar used to jump because each caller
    /// resolved max HP with its own slightly different fallback chain).
    /// </summary>
    private long ResolveMaxHpFor(string characterId, long hpBefore, long hpAfter)
    {
        long maxHp = 0;

        // 1) Locally cached max (populated by MATCH_CREATED / the snapshot / any earlier event).
        if (!string.IsNullOrEmpty(characterId))
        {
            _characterMaxHp.TryGetValue(characterId, out maxHp);

            // Also accept the side-key alias for this character.
            if (maxHp == 0 && _characterNames.TryGetValue("bot_a", out var a) && a == characterId) _characterMaxHp.TryGetValue("bot_a", out maxHp);
            if (maxHp == 0 && _characterNames.TryGetValue("bot_b", out var b) && b == characterId) _characterMaxHp.TryGetValue("bot_b", out maxHp);
        }

        // 2) The latest snapshot's max HP table.
        if (maxHp == 0 && _latestSnapshot?.characterMaxHpAtomic != null)
        {
            if (!string.IsNullOrEmpty(characterId)) _latestSnapshot.characterMaxHpAtomic.TryGetValue(characterId, out maxHp);
        }

        // 3) Record the highest HP we have EVER seen for this character as the max.
        EnsureKnownMaxHp(characterId, hpBefore, hpAfter);
        if (maxHp == 0 && !string.IsNullOrEmpty(characterId)) _characterMaxHp.TryGetValue(characterId, out maxHp);

        // 4) Last resort: use the HP we were given so the display never reads "current/current".
        if (maxHp <= 0) maxHp = System.Math.Max(hpBefore, hpAfter);

        return maxHp;
    }

    /// <summary>
    /// Single entry point for writing a side's HP to the UI. All HP sources (DAMAGE_APPLIED, HP_CHANGED,
    /// the pending flush) go through here so they cannot disagree about max HP or double-apply.
    /// </summary>
    private void ApplyHealthToUi(PlayerUI.Side side, long hpAfter)
    {
        string charId = CharacterIdForSide(side);
        long maxHp = ResolveMaxHpFor(charId, 0, hpAfter);
        uiManager?.UpdateHealth(ToUISide(side), hpAfter, maxHp);
    }

    /// <summary>Resolves the backend character id for a side (falls back to the conventional side key).</summary>
    private string CharacterIdForSide(PlayerUI.Side side)
    {
        string key = side == PlayerUI.Side.Left ? "bot_a" : "bot_b";
        return _characterNames.TryGetValue(key, out var id) && !string.IsNullOrEmpty(id) ? id : key;
    }

    /// <summary>
    /// Actually applies a gameplay event to game state / UI / animation. Called by the queue in
    /// strict sequence. Do NOT call directly from networking code.
    /// </summary>
    void ApplyMemeBattleEvent(MemeBattleEvent ev)
    {
        var t = ev.eventType;
        var payload = ev.payload;
        switch (t)
        {
            case "MATCH_CREATED":
                {
                    var characterIds = payload.Value<Newtonsoft.Json.Linq.JArray>("characterIds");
                    long initialHpAtomic = payload.Value<long?>("initialHpAtomic") ?? 1000;
                    if (characterIds != null && characterIds.Count >= 2)
                    {
                        string botA = characterIds[0].ToString();
                        string botB = characterIds[1].ToString();
                        // store mapping from side keys to actual character ids
                        _characterNames["bot_a"] = botA;
                        _characterNames["bot_b"] = botB;

                        uiManager?.SetFighterName(MemeBattleUI.Side.Left, botA);
                        uiManager?.SetFighterName(MemeBattleUI.Side.Right, botB);

                        // store max HP under both the side-key and the actual character id so lookups succeed
                        // Persist initial max under both side keys and actual ids
                        _characterMaxHp["bot_a"] = initialHpAtomic;
                        _characterMaxHp["bot_b"] = initialHpAtomic;
                        _characterMaxHp[botA] = initialHpAtomic;
                        _characterMaxHp[botB] = initialHpAtomic;
                        // Store character names for health display
                        // Initialize health display with initial HP
                        uiManager?.UpdateHealth(MemeBattleUI.Side.Left, initialHpAtomic, initialHpAtomic);
                        uiManager?.UpdateHealth(MemeBattleUI.Side.Right, initialHpAtomic, initialHpAtomic);
                    }

                    // A new match starts a fresh turn timeline: drop the queue's per-turn barrier state and
                    // every per-turn idempotency key, so a turn id the backend REUSES for the new match is
                    // not mistaken for a stale replay of the previous match's turn.
                    ResetPerTurnGuardsForNewMatch();
                    Debug.Log($"MATCH_CREATED initialHpAtomic={initialHpAtomic}");
                }
                break;
            case "MATCH_STARTED":
                bool open = payload.Value<bool?>("contributionWindowOpen") ?? false;
                Debug.Log($"MATCH_STARTED contributionWindowOpen={open}");
                break;
            case "TURN_STARTED":
                {
                    int turnNumber = payload.Value<int?>("turnNumber") ?? 0;
                    string opensAtIso = payload.Value<string>("opensAt");
                    string closesAtIso = payload.Value<string>("closesAt");
                    roundManager?.StartTurnTimer(turnNumber, opensAtIso, closesAtIso);

                    // Clear old dialogue from previous turn
                    uiManager?.ClearDialogue(MemeBattleUI.Side.Left);
                    uiManager?.ClearDialogue(MemeBattleUI.Side.Right);

                    Debug.Log($"TURN_STARTED turnNumber={turnNumber}");
                }
                break;
            case "ARGUMENT_SELECTED":
                {
                    string argumentId = payload.Value<string>("argumentId");
                    string actorCharacterId = payload.Value<string>("actorCharacterId");
                    string targetCharacterId = payload.Value<string>("targetCharacterId");
                    string memeText = payload.Value<string>("memeText");
                    string animationId = payload.Value<string>("animationId");

                    Debug.Log($"ARGUMENT_SELECTED argumentId={argumentId} actor={actorCharacterId} target={targetCharacterId} anim={animationId} meme='{memeText}'");

                    // Determine which side is attacking and which is being hit
                    PlayerUI.Side? attackerSide = SideFromCharacterId(actorCharacterId);
                    PlayerUI.Side? targetSide = SideFromCharacterId(targetCharacterId);

                    // Debug: Log sides
                    Debug.Log($"[ARGUMENT_SELECTED] attackerSide={attackerSide}, targetSide={targetSide}, animationId='{animationId}'");

                    // Play matched attack & hit animations (same variant number for both)
                    if (!string.IsNullOrEmpty(animationId) && attackerSide.HasValue && targetSide.HasValue)
                    {
                        // Default behaviour: attacker plays the attack animation, target plays the hit animation.
                        string hitAnimationId = animationId.Replace("attack", "hit");
                        Debug.Log($"Animation ✅ [ARGUMENT_SELECTED] Playing MATCHED: attacker {attackerSide} plays '{animationId}' → target {targetSide} plays '{hitAnimationId}'");

                        // NOTE: neither the move nor the attack/hit playback is issued here.
                        //   - HandleMemeBattleEvent composes the single queue item (move first, then the
                        //     attack/hit pair) so the attacker steps into position BEFORE the clip plays.
                        //   - The attack itself fires inside that item (PlayAnimationForSide), which is
                        //     also where the chosen variant is recorded for the matching DAMAGE_APPLIED.
                        // Firing the attack here as well would double-play it, so this block only does
                        // the non-animation work (camera framing below).

                        // Hold the camera on the ATTACKER for the whole attack; it eases back to the
                        // midpoint between the fighters when the exchange ends (see the queue item's
                        // completion below). This is the persistent attack shot, not a short bias.
                        try
                        {
                            MortalKombatCamera.Instance?.BeginAttackFocus(attackerSide.Value);
                            MortalKombatCamera.Instance?.TriggerImpactShake();
                        }
                        catch { }
                    }
                    else if (!string.IsNullOrEmpty(animationId) && attackerSide.HasValue)
                    {
                        // Fallback if we only have attacker side: play the attack in place (no move).
                        Debug.LogWarning($"Animation ❌ [ARGUMENT_SELECTED] Could not determine target side. Attacker={attackerSide}, Target={targetSide}, using single animation");
                        PlayAnimationForSide(attackerSide.Value, animationId, defaultCrossFade);
                    }
                    else
                    {
                        Debug.LogError($"Animation ❌ [ARGUMENT_SELECTED] Missing required data: animationId='{animationId}', attackerSide={attackerSide}");
                    }

                    // Show meme text on attacker
                    if (attackerSide.HasValue && !string.IsNullOrEmpty(memeText))
                    {
                        // Some scenes expect the speech bubble to be shown on the opposite side during play.
                        var displaySide = attackerSide.Value;
                        if (_swapSpeechDuringMatch && !_matchEnded)
                        {
                            displaySide = (attackerSide.Value == PlayerUI.Side.Left) ? PlayerUI.Side.Right : PlayerUI.Side.Left;
                        }
                        uiManager?.SetDialogue(ToUISide(displaySide), memeText);
                        // Also show the meme result popup on the attacker side for a short duration
                        try
                        {
                            uiManager?.ShowMemeResult(ToUISide(attackerSide.Value), memeText, 2f);
                        }
                        catch { }
                    }

                    // Diagnostic: log which PlayerUI GameObject and Animator GameObject will be used for attacker/target
                    try
                    {
                        string attUI = attackerSide.HasValue ? (uiManager != null ? uiManager.name : "(no UI)") : "(unknown)";
                        string tarUI = targetSide.HasValue ? (uiManager != null ? uiManager.name : "(no UI)") : "(unknown)";
                        string attAnim = GetFighterTransform(attackerSide ?? PlayerUI.Side.Left)?.name ?? "(none)";
                        string tarAnim = GetFighterTransform(targetSide ?? PlayerUI.Side.Right)?.name ?? "(none)";
                        Debug.Log($"ARGUMENT_SELECTED DIAG: attackerSide={attackerSide} attackerUI={attUI} attackerAnim={attAnim} | targetSide={targetSide} targetUI={tarUI} targetAnim={tarAnim}");
                    }
                    catch { }

                    // Show voting totals if provided
                    if (payload["finalTotalsAtomic"] != null)
                    {
                        try
                        {
                            var dict = payload["finalTotalsAtomic"].ToObject<System.Collections.Generic.Dictionary<string, long>>();
                            roundManager?.ShowFinalTotals(dict);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"Failed to parse finalTotalsAtomic: {ex}");
                        }
                    }
                }
                break;
            case "MULTIPLIER_SELECTED":
                {
                    int multiplierScaled = payload.Value<int?>("multiplierScaled") ?? 0;
                    int multiplierScale = payload.Value<int?>("multiplierScale") ?? 1;
                    float mult = multiplierScale != 0 ? ((float)multiplierScaled) / multiplierScale : 0f;
                    roundManager?.ShowMultiplier(mult);
                }
                break;
            case "DAMAGE_APPLIED":
                {
                    // ONE TURN = ONE STACK: this event is normally consumed by the turn stack, which
                    // applies HP exactly once (ApplyTurnHpOnce) and routes the reaction
                    // (RouteTurnDamageReaction). This case only runs when a DAMAGE_APPLIED arrives OUTSIDE
                    // a turn (no turnId), so it must NOT write HP on its own -- it delegates to the SAME
                    // guarded writer, keeping "mỗi turn chỉ trừ máu 1 lần khi bị hit" true in every path.
                    string targetId = payload.Value<string>("targetCharacterId");
                    string animationId = payload.Value<string>("animationId");
                    long damage = payload.Value<long?>("damageAtomic") ?? 0;
                    long hpAfter = payload.Value<long?>("hpAfterAtomic") ?? -1;

                    var side = SideFromCharacterId(targetId);
                    if (!side.HasValue)
                    {
                        Debug.LogWarning($"Animation [GameManager] DAMAGE_APPLIED ignored: could not resolve '{targetId}' to a side.");
                        break;
                    }

                    // The single HP write (idempotent per turnId:target).
                    ApplyTurnHpOnce(ev.turnId, targetId, hpAfter, damage, animationId);

                    // Death pose / hit routing (no HP touched here). This is the NO-EXCHANGE path (a
                    // DAMAGE_APPLIED with no ARGUMENT_SELECTED / already fired by its turn stack), so there
                    // is no victim clip of its own -> hasExchange = false.
                    string exchangeKey = null;
                    if (!string.IsNullOrEmpty(ev.turnId) && !string.IsNullOrEmpty(targetId))
                    {
                        var actorId = payload.Value<string>("actorCharacterId");
                        if (!string.IsNullOrEmpty(actorId)) exchangeKey = $"{ev.turnId}:{actorId}:{targetId}";
                    }
                    RouteTurnDamageReaction(ev, side, side, exchangeKey, hpAfter, false);

                    Debug.Log($"DAMAGE_APPLIED {targetId}: -{damage} = {hpAfter}");
                }
                break;
            case "HP_CHANGED":
                {
                    string targetId = payload.Value<string>("characterId");
                    long hpAfter = payload.Value<long?>("hpAfterAtomic") ?? 0;
                    var side = SideFromCharacterId(targetId);

                    // HP_CHANGED is the BE's AUTHORITATIVE HP value for this character. It is routed
                    // through the SAME single guarded writer as the turn stack (ApplyTurnHpOnce), so no
                    // matter which path reaches here the bar is written AT MOST ONCE per (turnId:character).
                    if (side.HasValue)
                    {
                        ApplyTurnHpOnce(ev.turnId, targetId, hpAfter, 0, null);
                    }
                    else
                    {
                        Debug.LogWarning($"Animation [GameManager] HP_CHANGED ignored: could not resolve '{targetId}' to a side.");
                    }

                    // Keep the snapshot in sync so later comparisons/repaints agree with the BE.
                    if (_latestSnapshot != null && _latestSnapshot.characterHpAtomic != null)
                    {
                        _latestSnapshot.characterHpAtomic[targetId] = hpAfter;
                    }
                }
                break;
            case "WINNER_DECLARED":
                {
                    // Only process once
                    if (_matchEnded) return;
                    _matchEnded = true;

                    string winnerId = payload.Value<string>("winnerCharacterId");
                    var winnerSide = SideFromCharacterId(winnerId);
                    PlayerUI.Side? loserSide = null;
                    if (winnerSide.HasValue)
                    {
                        loserSide = (winnerSide.Value == PlayerUI.Side.Left) ? PlayerUI.Side.Right : PlayerUI.Side.Left;
                    }
                    else
                    {
                        // Try to resolve winner by known _characterNames mapping (server may send actual character ids)
                        try
                        {
                            if (_characterNames.TryGetValue("bot_a", out var aid) && !string.IsNullOrEmpty(aid) && aid == winnerId)
                            {
                                winnerSide = PlayerUI.Side.Left;
                                loserSide = PlayerUI.Side.Right;
                            }
                            else if (_characterNames.TryGetValue("bot_b", out var bid) && !string.IsNullOrEmpty(bid) && bid == winnerId)
                            {
                                winnerSide = PlayerUI.Side.Right;
                                loserSide = PlayerUI.Side.Left;
                            }
                        }
                        catch { }
                    }

                    Debug.Log($"WINNER_DECLARED winnerId={winnerId} side={winnerSide}");
                    if (winnerSide.HasValue)
                    {
                        // Diagnostic: log mapping tables and animator assignments to help track 'die' playing on wrong side
                        try
                        {
                            Debug.Log($"GameManager: _characterNames map: bot_a='{(_characterNames.TryGetValue("bot_a", out var _a) ? _a : "(none)")}', bot_b='{(_characterNames.TryGetValue("bot_b", out var _b) ? _b : "(none)")}'");
                            Debug.Log($"GameManager: _characterMaxHp keys: {string.Join(",", _characterMaxHp.Keys)}");
                        }
                        catch { }
                        // Restore normal speech behavior when declaring winner
                        _swapSpeechDuringMatch = false;
                        // Winner: show victory dialogue and play victory animation
                        uiManager?.SetDialogue(ToUISide(winnerSide.Value), "VICTORY!");
                        PlayAnimationForSide(winnerSide.Value, "victory", defaultCrossFade);

                        // Loser: play die animation (only once)
                        if (!_loserSideForDieAnimation.HasValue || _loserSideForDieAnimation != loserSide)
                        {
                            _loserSideForDieAnimation = loserSide;
                            Debug.Log($"Animation [GameManager] Attempting to play die animation on loser side={loserSide}");
                            if (loserSide.HasValue)
                            {
                                GetBridgeForSide(loserSide.Value)?.Die();
                            }
                        }
                    }
                    OnMatchEnded(winnerId);
                }
                break;
            default:
                Debug.LogWarning($"GameManager: Unknown MemeBattleEvent type: {t}");
                break;
        }
    }

    void OnMatchEnded(string winnerCharacterId)
    {
        Debug.Log($"OnMatchEnded: winner={winnerCharacterId}");
        // Future: Show match result UI, offer rematch button, etc.
    }

    /// <summary>
    /// Routes a combat exchange through the CombatPositioningController's queue so the SEQUENCE is
    /// guaranteed: attacker moves to the spot in front of the victim, THEN the attack/hit pair plays,
    /// and only then does the next queued action run. When positioning is not set up (or choreography
    /// is disabled) it degrades to playing the clips directly.
    /// </summary>
    private void RouteCombatAction(PlayerUI.Side attackerSide, PlayerUI.Side victimSide,
                                   string attackerAnimationId, string hitAnimationId)
    {
        var pos = Positioning;
        bool canChoreograph = choreographAttackPositions && pos != null &&
                              pos.LeftFighter != null && pos.RightFighter != null;

        if (canChoreograph)
        {
            // Keep the controller's fighter references in sync (in case the scene rebuilt them).
            pos.SetFighters(GetFighterTransform(PlayerUI.Side.Left), GetFighterTransform(PlayerUI.Side.Right));
            pos.EnqueueMoveThenAnimate(attackerSide, victimSide, attackerAnimationId, hitAnimationId);
            return;
        }

        // No positioning: play both clips back-to-back directly so the exchange still reads.
        PlayAnimationForSide(attackerSide, attackerAnimationId, defaultCrossFade);
        PlayAnimationForSide(victimSide, hitAnimationId, defaultCrossFade);
    }

    /// <summary>
    /// Schedules the getup on the knocked-down fighter through the SINGLE queue so it serializes
    /// behind whatever is already playing. Two items are enqueued:
    ///   1) a hold item that occupies the queue for <paramref name="delay"/> seconds (the knockdown
    ///      beat), guarded so a fighter that died during the hold stays down;
    ///   2) an animate item that triggers the getup and waits for it to finish.
    /// Facing lock keeps the getup-facing correct automatically, so no spot recompute is needed.
    /// </summary>
    private void ScheduleGetupThroughQueue(PlayerUI.Side side, CharacterAnimatorBridge bridge, int type, float delay)
    {
        var pos = Positioning;
        if (pos == null || bridge == null) return;

        pos.SetFighters(GetFighterTransform(PlayerUI.Side.Left), GetFighterTransform(PlayerUI.Side.Right));

        // 1) Knockdown hold: a no-op item that simply occupies the queue for `delay` seconds, and
        //    bails early if the fighter died during the hold (IsDead -> predicate true -> advance).
        if (delay > 0f)
        {
            pos.EnqueueWork(
                $"getup hold {side} {delay:0.00}s",
                null,
                () => bridge == null || bridge.IsDead,
                delay);
        }

        // 2) Getup: triggers the getup clip. This stack does NOT complete until the fighter has
        //    actually stood back up AND returned to the Idle state (IsIdleAndSettled), so the next
        //    queued stack only starts once the getup has fully finished. That keeps hit/attack from
        //    ever firing while the fighter is still mid-getup (which would be swallowed, since those
        //    transitions are authored from Idle).
        pos.EnqueueWork(
            $"getup {side}",
            () =>
            {
                if (bridge == null || bridge.IsDead) return;
                try
                {
                    // Derive the getup direction from the heavy hit that knocked the fighter down
                    // (hit 5/6 -> GetupType 1, hit 7 -> GetupType 2), instead of a fixed Inspector
                    // value. GetupFromKnockdown() falls back to the default when nothing was recorded.
                    bridge.GetupFromKnockdown();
                }
                catch (Exception ex) { Debug.LogWarning($"Animation [GameManager] failed to play getup animation: {ex}"); }
            },
            () => bridge == null || bridge.IsDead || bridge.IsFullyIdleNow,
            maxEventHoldSeconds);
    }

    // Debug helper: print setup info to console
    void PrintDebugInfo()
    {
        if (!debugMode) return;
        Debug.Log("=== GameManager Debug Info ===");
        Debug.Log($"UIManager: {(uiManager != null ? "✓" : "✗")}");
        Debug.Log($"Legacy PlayerUI: {(playerUI != null ? "✓" : "✗")}");
        Debug.Log($"RoundManager: {(roundManager != null ? "✓" : "✗")}");
        Debug.Log($"CombatPositioningController: {(Positioning != null ? "✓" : "✗")}");
        Debug.Log($"  - LeftFighter: {(Positioning?.LeftFighter != null ? Positioning.LeftFighter.name : "(none)")}");
        Debug.Log($"  - RightFighter: {(Positioning?.RightFighter != null ? Positioning.RightFighter.name : "(none)")}");
        Debug.Log($"WebSocketManager: {(WebSocketManager.Instance != null ? "✓" : "✗")}");
        Debug.Log($"Event queue: {(useSequentialEventQueue ? "ON" : "OFF")} (pending={PendingEventCount})");
    }

    PlayerUI.Side? SideFromCharacterId(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id.Equals("bot_a", StringComparison.OrdinalIgnoreCase)) return PlayerUI.Side.Left;
        if (id.Equals("bot_b", StringComparison.OrdinalIgnoreCase)) return PlayerUI.Side.Right;
        // Also check if we have mappings from side keys to actual character ids
        try
        {
            if (_characterNames.TryGetValue("bot_a", out var aid) && !string.IsNullOrEmpty(aid) && id.Equals(aid, StringComparison.OrdinalIgnoreCase)) return PlayerUI.Side.Left;
            if (_characterNames.TryGetValue("bot_b", out var bid) && !string.IsNullOrEmpty(bid) && id.Equals(bid, StringComparison.OrdinalIgnoreCase)) return PlayerUI.Side.Right;
        }
        catch { }

        return null;
    }

    public void PlaySingleAnimation(PlayerUI.Side side, string animationId, float crossFade = -1f)
    {
        PlayAnimationForSide(side, animationId, crossFade < 0f ? defaultCrossFade : crossFade);
    }

    public void PlayBothAnimations(string leftAnimationId, string rightAnimationId, float crossFade = -1f)
    {
        float fade = crossFade < 0f ? defaultCrossFade : crossFade;
        PlayAnimationForSide(PlayerUI.Side.Left, leftAnimationId, fade);
        PlayAnimationForSide(PlayerUI.Side.Right, rightAnimationId, fade);
    }

    string ExtractEventName(string json)
    {
        try { var j = JObject.Parse(json); return j.Value<string>("eventName") ?? j.Value<string>("event"); }
        catch { return null; }
    }

    int ExtractInt(string json, string key)
    {
        try { var j = JObject.Parse(json); return j.Value<int?>(key) ?? 0; }
        catch { return 0; }
    }

    string ExtractString(string json, string key)
    {
        try { var j = JObject.Parse(json); return j.Value<string>(key); }
        catch { return null; }
    }

    // ==================================================================
    // ==================================================================
    // Event queue (delegated)
    // ------------------------------------------------------------------
    // GameManager no longer runs its own queue. Every gameplay event is handed to the SINGLE queue
    // owned by CombatPositioningController (see HandleMemeBattleEvent), which applies the event and
    // waits out the animation it triggers, one at a time. The members below are thin views onto that
    // one queue so existing debug/UI code keeps working.
    // ==================================================================

    /// <summary>Number of events/actions still waiting on the single queue.</summary>
    public int PendingEventCount => Positioning != null ? Positioning.PendingCount : 0;

    /// <summary>True while the single queue is applying an action or has actions waiting.</summary>
    public bool IsEventQueueBusy => Positioning != null && Positioning.IsBusy;

    /// <summary>Drops all pending actions (the one currently executing still completes).</summary>
    public void ClearEventQueue()
    {
        var pos = Positioning;
        if (pos != null)
        {
            int n = pos.PendingCount;
            pos.ClearQueue();
            if (n > 0 && debugMode) Debug.Log($"GameManager: cleared {n} pending queued action(s).");
        }
    }
}
