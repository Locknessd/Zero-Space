/// <summary>
/// PROJECT ARCHITECTURE: Core Logic Layer (Controller trung tâm)
/// ROLE: Điều phối luồng trận đấu, nhận dữ liệu từ WebSocketManager và phân phối xuống các hệ thống UI/Animation.
/// RESPONSIBILITIES:
/// - Lưu trữ trạng thái trận đấu hiện tại (MatchState) theo tài liệu thiết kế.
/// - Phân tích (Parse) các gói tin JSON từ Server thành các C# Object tương ứng.
/// - Gọi các hàm cập nhật giao diện của PlayerUI, RoundManager và AnimationController khi có sự kiện mới.
/// AI NOTE: Script này đóng vai trò bộ não trung tâm của Client, không tự tính toán sát thương (vì BE đã tính), chỉ nhận kết quả và điều phối hiển thị.
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
    public AnimationController animationController;

    [Header("Animator Bridges (optional, per side)")]
    [Tooltip("Optional CharacterAnimatorBridge for the Left (bot_a) fighter. When assigned, gameplay events are routed to it in addition to AnimationController.")]
    public CharacterAnimatorBridge leftBridge;
    [Tooltip("Optional CharacterAnimatorBridge for the Right (bot_b) fighter. When assigned, gameplay events are routed to it in addition to AnimationController.")]
    public CharacterAnimatorBridge rightBridge;
    [Tooltip("When true, only the bridge drives animation (AnimationController calls are skipped). Defaults to true: CharacterAnimatorBridge is the single animation authority.")]
    public bool bridgesTakePriority = true;

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

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
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
        var p = GetRandomPreset();
        if (p != null)
        {
            Debug.Log($"[DebugTrigger] Q -> Play preset '{p.key}' (Left attacks)");
            PlayBothAnimations(p.leftState, p.rightState);
        }
        else Debug.LogWarning("[DebugTrigger] Q -> no valid presets");
    }

    public void DebugTriggerE()
    {
        var p = GetRandomPreset();
        if (p != null)
        {
            Debug.Log($"[DebugTrigger] E -> Play preset '{p.key}' (Right attacks)");
            PlayBothAnimations(p.rightState, p.leftState);
        }
        else Debug.LogWarning("[DebugTrigger] E -> no valid presets");
    }

    void Update()
    {
        if (!enableLocalInputTesting) return;
        if (IsKeyDown(KeyCode.Q))
        {
            var p = GetRandomPreset();
            if (p != null) PlayBothAnimations(p.leftState, p.rightState);
            MortalKombatCamera.Instance?.TriggerImpactShake();
        }
        if (IsKeyDown(KeyCode.E))
        {
            var p = GetRandomPreset();
            if (p != null) PlayBothAnimations(p.rightState, p.leftState);
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
                uiManager?.UpdateHealth(ToUISide(side), hp, max);
            }
        }

        // Also update PlayerUI health display
        UpdatePlayerHealthDisplay(snap);
    }

    void UpdatePlayerHealthDisplay(MemeBattleMatchSnapshot snap)
    {
        if (snap == null) return;

        // Resolve actual character ids for each side (fall back to side-key if mapping missing)
        string leftId = _characterNames.TryGetValue("bot_a", out var l) ? l : "bot_a";
        string rightId = _characterNames.TryGetValue("bot_b", out var r) ? r : "bot_b";

        long hpA = 0, hpB = 0, maxHpA = 0, maxHpB = 0;

        if (snap.characterHpAtomic != null)
        {
            snap.characterHpAtomic.TryGetValue(leftId, out hpA);
            snap.characterHpAtomic.TryGetValue(rightId, out hpB);
        }

        if (snap.characterMaxHpAtomic != null)
        {
            snap.characterMaxHpAtomic.TryGetValue(leftId, out maxHpA);
            snap.characterMaxHpAtomic.TryGetValue(rightId, out maxHpB);
        }

        // Fallback: check local _characterMaxHp store if snapshot doesn't provide a max
        if (maxHpA == 0) _characterMaxHp.TryGetValue(leftId, out maxHpA);
        if (maxHpA == 0) _characterMaxHp.TryGetValue("bot_a", out maxHpA);
        if (maxHpB == 0) _characterMaxHp.TryGetValue(rightId, out maxHpB);
        if (maxHpB == 0) _characterMaxHp.TryGetValue("bot_b", out maxHpB);

        // Update health display for both sides (slider + text)
        uiManager?.UpdateHealth(MemeBattleUI.Side.Left, hpA, maxHpA);
        uiManager?.UpdateHealth(MemeBattleUI.Side.Right, hpB, maxHpB);

        Debug.Log($"UpdatePlayerHealthDisplay: Left={hpA}/{maxHpA}, Right={hpB}/{maxHpB}");
    }

    /// <summary>
    /// Entry point for a gameplay event. Rather than apply it immediately (which would let a
    /// new event overwrite an animation still mid-play), the event is placed on the sequential
    /// MemeEventQueue. The queue runs one event at a time and waits for the animation it triggers
    /// to finish before applying the next one.
    /// </summary>
    void HandleMemeBattleEvent(MemeBattleEvent ev)
    {
        if (ev == null) return;

        // Events that carry an animation hold the queue until that animation finishes.
        // Advancement is driven by the PRECISE AnimatorStateInfo.normalizedTime >= 1 check
        // (via CharacterAnimatorBridge.IsAnimationFinished). The estimated duration below is
        // only a SAFETY UPPER BOUND so a stuck/looping state can never wedge the queue.
        float duration = EstimateEventDuration(ev);

        string label = $"{ev.eventType} seq={ev.sequence}";

        // Queue is optional: when disabled, apply immediately (legacy behaviour).
        if (!useSequentialEventQueue)
        {
            ApplyMemeBattleEvent(ev);
            return;
        }

        // Capture the bridges this event will animate so we can watch them precisely.
        var watched = BridgesForEvent(ev);

        Action apply = () =>
        {
            ApplyMemeBattleEvent(ev);
            // Start watching each involved fighter's animation right after the command is issued.
            for (int i = 0; i < watched.Count; i++)
                watched[i]?.BeginWatchCurrentAnimation();
        };

        // Advance as soon as ALL watched fighters have finished their animation (precise),
        // with the estimated duration as a safety upper bound.
        Func<bool> finished = watched.Count == 0
            ? (Func<bool>)null
            : () => AllBridgesFinished(watched);

        _eventQueue.Enqueue(new EventWorkItem(label, apply, duration, finished));
        EnsureQueueRunning();
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
                        Debug.Log($"✅ [ARGUMENT_SELECTED] Playing MATCHED: attacker {attackerSide} plays '{animationId}' → target {targetSide} plays '{hitAnimationId}'");

                        // Play and capture the numeric variant chosen so later DAMAGE_APPLIED events can reuse it
                        int variant = 0;
                        if (!bridgesTakePriority)
                        {
                            try
                            {
                                variant = animationController?.PlayBothAnimationsWithMatchedVariant(attackerSide.Value, animationId, targetSide.Value, hitAnimationId) ?? 0;
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"GameManager: PlayBothAnimationsWithMatchedVariant failed: {ex}");
                            }
                        }

                        // Route the selected argument to the optional animator bridges:
                        // attacker plays an attack variant, target plays the matching hit reaction.
                        try
                        {
                            var attackerBridge = GetBridgeForSide(attackerSide.Value);
                            var targetBridge = GetBridgeForSide(targetSide.Value);
                            if (attackerBridge != null || targetBridge != null)
                            {
                                int attackIndex = attackerBridge != null ? attackerBridge.AttackFromAnimationId(animationId) : 0;
                                targetBridge?.TakeHitFromAnimationId(animationId, attackIndex);
                                if (attackIndex > 0) variant = attackIndex; // reuse variant for DAMAGE_APPLIED pairing
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"GameManager: bridge ARGUMENT_SELECTED routing failed: {ex}");
                        }

                        // Store mapping keyed by turnId:actor:target so DAMAGE_APPLIED can reuse same variant
                        try
                        {
                            if (variant > 0 && !string.IsNullOrEmpty(ev.turnId))
                            {
                                string key = $"{ev.turnId}:{actorCharacterId}:{targetCharacterId}";
                                _matchedVariantMap[key] = variant;
                                Debug.Log($"GameManager: Stored matched variant {variant} for key {key}");
                            }
                        }
                        catch { }
                        // Request camera to focus toward attacker briefly and trigger a subtle shake
                        try
                        {
                            MortalKombatCamera.Instance?.FocusOnSide(attackerSide.Value, 0.35f, 0.45f);
                            MortalKombatCamera.Instance?.TriggerImpactShake();
                        }
                        catch { }
                    }
                    else if (!string.IsNullOrEmpty(animationId) && attackerSide.HasValue)
                    {
                        // Fallback if we only have attacker side
                        Debug.LogWarning($"❌ [ARGUMENT_SELECTED] Could not determine target side. Attacker={attackerSide}, Target={targetSide}, using single animation");
                        animationController?.PlayAnimation(attackerSide.Value, animationId);
                    }
                    else
                    {
                        Debug.LogError($"❌ [ARGUMENT_SELECTED] Missing required data: animationId='{animationId}', attackerSide={attackerSide}");
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
                        string attAnim = "(none)";
                        string tarAnim = "(none)";
                        if (animationController != null)
                        {
                            attAnim = attackerSide == PlayerUI.Side.Left ? (animationController.leftAnimator?.gameObject.name ?? "(no animator)") : (animationController.rightAnimator?.gameObject.name ?? "(no animator)");
                            tarAnim = targetSide == PlayerUI.Side.Left ? (animationController.leftAnimator?.gameObject.name ?? "(no animator)") : (animationController.rightAnimator?.gameObject.name ?? "(no animator)");
                        }
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
                    string targetId = payload.Value<string>("targetCharacterId");
                    long damage = payload.Value<long?>("damageAtomic") ?? 0;
                    long hpAfter = payload.Value<long?>("hpAfterAtomic") ?? 0;
                    string animationId = payload.Value<string>("animationId");
                    var side = SideFromCharacterId(targetId);

                    if (side.HasValue)
                    {
                        try
                        {
                            var uiObj = uiManager != null ? uiManager.name : "(no UI)";
                            var animObj = animationController != null ? (side == PlayerUI.Side.Left ? (animationController.leftAnimator?.gameObject.name ?? "(no animator)") : (animationController.rightAnimator?.gameObject.name ?? "(no animator)")) : "(no animctrl)";
                            Debug.Log($"HP_CHANGED DIAG: characterId={targetId} side={side} ui={uiObj} animator={animObj}");
                        }
                        catch { }
                        try
                        {
                            var uiObj = uiManager != null ? uiManager.name : "(no UI)";
                            var animObj = animationController != null ? (side == PlayerUI.Side.Left ? (animationController.leftAnimator?.gameObject.name ?? "(no animator)") : (animationController.rightAnimator?.gameObject.name ?? "(no animator)")) : "(no animctrl)";
                            Debug.Log($"DAMAGE_APPLIED DIAG: targetId={targetId} side={side} ui={uiObj} animator={animObj}");
                        }
                        catch { }
                        // Record any known hp values as candidate max so future displays can use a stable max.
                        long hpBefore = payload.Value<long?>("hpBeforeAtomic") ?? 0;
                        EnsureKnownMaxHp(targetId, hpBefore, hpAfter);
                        // Resolve max HP: try local store, then latest snapshot, then fall back to hpBefore/hpAfter
                        long maxHp = 0;
                        if (!_characterMaxHp.TryGetValue(targetId, out maxHp) || maxHp == 0)
                        {
                            // try mapping from side key
                            if (_characterNames.TryGetValue(targetId, out var mapped) && _characterMaxHp.TryGetValue(mapped, out var mm)) maxHp = mm;
                        }
                        if ((maxHp == 0 || maxHp < hpAfter) && _latestSnapshot?.characterMaxHpAtomic != null)
                        {
                            _latestSnapshot.characterMaxHpAtomic.TryGetValue(targetId, out maxHp);
                            if (maxHp == 0 && _characterNames.TryGetValue("bot_a", out var aid) && _latestSnapshot.characterMaxHpAtomic.TryGetValue(aid, out var altA)) maxHp = altA;
                        }
                        // final fallback: use recorded hpBefore/hpAfter if no other max known
                        if (maxHp == 0)
                        {
                            long hpBeforeVal = payload.Value<long?>("hpBeforeAtomic") ?? 0;
                            long candidate = hpBeforeVal > 0 ? hpBeforeVal : hpAfter;
                            if (candidate > 0) maxHp = candidate;
                            // persist candidate as known max for next time
                            if (maxHp > 0) _characterMaxHp[targetId] = maxHp;
                        }

                        // Update both slider and text display
                        uiManager?.UpdateHealth(ToUISide(side.Value), hpAfter, maxHp);

                        // Show damage popup
                        if (damage > 0)
                        {
                            uiManager?.ShowDamage(ToUISide(side.Value), (int)damage);
                        }

                        // Play hit animation (or use provided animationId) then if HP reached zero play die after a short delay
                        try
                        {
                            // When DAMAGE_APPLIED is received for a target, server-provided animationId may be the
                            // attack id (from the actor). Map attack -> hit for the target so the visual shows a hit.
                            // Prefer to reuse the variant previously chosen for this turn/actor/target if available
                            string toPlay = "hit";
                            int chosenVariant = 0;
                            try
                            {
                                var actorId = payload.Value<string>("actorCharacterId");
                                if (!string.IsNullOrEmpty(ev.turnId) && !string.IsNullOrEmpty(actorId) && !string.IsNullOrEmpty(targetId))
                                {
                                    string key = $"{ev.turnId}:{actorId}:{targetId}";
                                    if (_matchedVariantMap.TryGetValue(key, out var v))
                                    {
                                        chosenVariant = v;
                                    }
                                }
                            }
                            catch { }

                            if (!string.IsNullOrEmpty(animationId))
                            {
                                var lower = animationId.ToLowerInvariant();
                                if (lower.Contains("attack"))
                                {
                                    // if we have a stored variant, construct hitN to match
                                    if (chosenVariant > 0)
                                    {
                                        toPlay = $"hit{chosenVariant}";
                                    }
                                    else
                                    {
                                        toPlay = animationId.Replace("attack", "hit");
                                    }
                                }
                                else
                                {
                                    // If payload already contained a hit id, use it. Otherwise default to 'hit'
                                    toPlay = animationId;
                                }
                            }

                            // If HP reached zero, play die immediately instead of a hit animation
                            if (hpAfter == 0)
                            {
                                try
                                {
                                    // Ensure we only play die once per loser
                                    if (!_loserSideForDieAnimation.HasValue || _loserSideForDieAnimation != side)
                                    {
                                        _loserSideForDieAnimation = side;
                                        Debug.Log($"GameManager: DAMAGE_APPLIED -> HP is 0, playing KB_TopKO (die) on side={side}");
                                        // Lethal hit: play the permanent death pose immediately.
                                        // Works for BOTH light and heavy hits that drained the last HP:
                                        // the character collapses into KB_TopKO and never gets up.
                                        if (!bridgesTakePriority) animationController?.PlayAnimation(side.Value, "die", 0f);
                                        GetBridgeForSide(side.Value)?.TakeFatalHit();
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogWarning($"GameManager: failed to play die animation: {ex}");
                                }
                            }
                            else
                            {
                                if (!bridgesTakePriority && !string.IsNullOrEmpty(toPlay))
                                {
                                    animationController?.PlayAnimation(side.Value, toPlay);
                                }

                                // Route the hit reaction to the optional bridge.
                                // HitIndex mirrors the ATTACK's index (no randomisation): the variant chosen
                                // in ARGUMENT_SELECTED is reused here so attackN pairs with hitN. Weight is
                                // inferred from the BE animationId ("heavy" -> knockdown / KB_Idle_1).
                                try
                                {
                                    var targetBridge = GetBridgeForSide(side.Value);
                                    if (targetBridge != null)
                                    {
                                        // Heavy hits drive the knockdown flow; light hits stay inline.
                                        bool isHeavy = !string.IsNullOrEmpty(animationId) &&
                                                       animationId.ToLowerInvariant().Contains("heavy");
                                        if (chosenVariant > 0)
                                        {
                                            // Authoritative pairing: hit follows the attack variant exactly.
                                            targetBridge.TakeHitByAttackIndex(chosenVariant, isHeavy, animationId);
                                        }
                                        else
                                        {
                                            // No stored attack variant (e.g. missing turnId): fall back to
                                            // string-based weight classification.
                                            Debug.LogWarning($"GameManager: no matched attack variant for target {targetId} (turnId='{ev.turnId}'); falling back to animationId-based hit.");
                                            targetBridge.TakeHitFromAnimationId(animationId);
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogWarning($"GameManager: bridge DAMAGE_APPLIED hit routing failed: {ex}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"GameManager: failed to play damage animation: {ex}");
                        }

                        Debug.Log($"DAMAGE_APPLIED {targetId}: -{damage} = {hpAfter}/{maxHp}");
                    }
                }
                break;
            case "HP_CHANGED":
                {
                    string targetId = payload.Value<string>("characterId");
                    long hpAfter = payload.Value<long?>("hpAfterAtomic") ?? 0;
                    var side = SideFromCharacterId(targetId);

                    if (side.HasValue)
                    {
                        // Try to persist any known values as max for future updates
                        EnsureKnownMaxHp(targetId, 0, hpAfter);

                        long maxHp = 0;
                        // Prefer locally cached max
                        if (!_characterMaxHp.TryGetValue(targetId, out maxHp) || maxHp == 0)
                        {
                            // try snapshot
                            if (_latestSnapshot?.characterMaxHpAtomic != null) _latestSnapshot.characterMaxHpAtomic.TryGetValue(targetId, out maxHp);
                            if (maxHp == 0 && _characterNames.TryGetValue(targetId, out var mapped) && _latestSnapshot?.characterMaxHpAtomic != null) _latestSnapshot.characterMaxHpAtomic.TryGetValue(mapped, out maxHp);
                        }
                        // try local store again
                        if (maxHp == 0) _characterMaxHp.TryGetValue(targetId, out maxHp);
                        if (maxHp == 0 && _characterNames.TryGetValue("bot_a", out var aid)) _characterMaxHp.TryGetValue(aid, out maxHp);

                        // fallback to hpAfter if still unknown, and persist it (so slider text doesn't show current/current)
                        if (maxHp == 0)
                        {
                            maxHp = hpAfter;
                            if (maxHp > 0) _characterMaxHp[targetId] = maxHp;
                        }

                        // Update both slider and text display
                        uiManager?.UpdateHealth(ToUISide(side.Value), hpAfter, maxHp);

                        Debug.Log($"HP_CHANGED {targetId}: {hpAfter}/{maxHp}");
                    }

                    // Also update snapshot for future reference
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
                            if (animationController != null)
                            {
                                Debug.Log($"GameManager: AnimationController leftAnimator={(animationController.leftAnimator!=null?animationController.leftAnimator.gameObject.name:"(null)" )} rightAnimator={(animationController.rightAnimator!=null?animationController.rightAnimator.gameObject.name:"(null)")}");
                            }
                        }
                        catch { }
                        // Restore normal speech behavior when declaring winner
                        _swapSpeechDuringMatch = false;
                        // Winner: show victory dialogue and play victory animation
                        uiManager?.SetDialogue(ToUISide(winnerSide.Value), "VICTORY!");
                        if (!bridgesTakePriority) animationController?.PlayAnimation(winnerSide.Value, "victory");

                        // Loser: play die animation (only once)
                        if (!_loserSideForDieAnimation.HasValue || _loserSideForDieAnimation != loserSide)
                        {
                            _loserSideForDieAnimation = loserSide;
                            Debug.Log($"GameManager: Attempting to play die animation on loser side={loserSide}");
                            if (loserSide.HasValue)
                            {
                                if (!bridgesTakePriority) animationController?.PlayAnimation(loserSide.Value, "die", 0f);
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

    private System.Collections.IEnumerator PlayDieAfterDelay(PlayerUI.Side side, float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);
        try
        {
            animationController?.PlayAnimation(side, "die");
        }
        catch { }
    }

    // Debug helper: print setup info to console
    void PrintDebugInfo()
    {
        if (!debugMode) return;
        Debug.Log("=== GameManager Debug Info ===");
        Debug.Log($"UIManager: {(uiManager != null ? "✓" : "✗")}");
        Debug.Log($"Legacy PlayerUI: {(playerUI != null ? "✓" : "✗")}");
        Debug.Log($"RoundManager: {(roundManager != null ? "✓" : "✗")}");
        Debug.Log($"AnimationController: {(animationController != null ? "✓" : "✗")}");
        if (animationController != null)
        {
            Debug.Log($"  - LeftAnimator: {(animationController.leftAnimator != null ? "✓" : "✗")}");
            Debug.Log($"  - RightAnimator: {(animationController.rightAnimator != null ? "✓" : "✗")}");
        }
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
        animationController?.PlayAnimation(side, animationId, crossFade);
    }

    public void PlayBothAnimations(string leftAnimationId, string rightAnimationId, float crossFade = -1f)
    {
        animationController?.PlayBoth(leftAnimationId, rightAnimationId, crossFade);
    }

    AnimationController.AnimationPair GetRandomPreset()
    {
        if (animationController == null || animationController.presets == null || animationController.presets.Length == 0) return null;
        var valid = new System.Collections.Generic.List<AnimationController.AnimationPair>();
        foreach (var p in animationController.presets) { if (p == null) continue; if (string.IsNullOrEmpty(p.leftState) || string.IsNullOrEmpty(p.rightState)) continue; valid.Add(p); }
        if (valid.Count == 0) return null; int idx = UnityEngine.Random.Range(0, valid.Count); return valid[idx];
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
    // Sequential event queue (built-in)
    // ------------------------------------------------------------------
    // Applies incoming events strictly one at a time, holding the queue
    // for the duration of the animation each event triggers, so a new
    // event can never cut off an animation still mid-play.
    // ==================================================================

    /// <summary>
    /// One queued unit of work: an action, a safety duration, and an optional completion
    /// predicate. The queue advances as soon as the predicate reports done, or when the
    /// duration elapses (whichever comes first).
    /// </summary>
    private class EventWorkItem
    {
        public readonly string label;
        public readonly Action action;
        public readonly float duration;
        public readonly Func<bool> isFinished;

        public EventWorkItem(string label, Action action, float duration, Func<bool> isFinished = null)
        {
            this.label = label;
            this.action = action;
            this.duration = Mathf.Max(0f, duration);
            this.isFinished = isFinished;
        }
    }

    private readonly System.Collections.Generic.Queue<EventWorkItem> _eventQueue =
        new System.Collections.Generic.Queue<EventWorkItem>();
    private Coroutine _eventQueueRunner;
    private EventWorkItem _eventQueueCurrent;

    /// <summary>Number of events waiting to be applied (excludes the one executing).</summary>
    public int PendingEventCount => _eventQueue.Count;

    /// <summary>True while an event is being applied or events are waiting.</summary>
    public bool IsEventQueueBusy => _eventQueueCurrent != null || _eventQueue.Count > 0;

    /// <summary>Drops all pending events (the one currently executing still completes).</summary>
    public void ClearEventQueue()
    {
        int n = _eventQueue.Count;
        _eventQueue.Clear();
        if (n > 0 && debugMode) Debug.Log($"GameManager: cleared {n} pending event(s).");
    }

    private void EnsureQueueRunning()
    {
        if (_eventQueueRunner == null) _eventQueueRunner = StartCoroutine(RunEventQueue());
    }

    private System.Collections.IEnumerator RunEventQueue()
    {
        while (_eventQueue.Count > 0)
        {
            _eventQueueCurrent = _eventQueue.Dequeue();
            if (debugMode) Debug.Log($"GameManager: [queue] apply '{_eventQueueCurrent.label}' (pending left {_eventQueue.Count})");

            try
            {
                _eventQueueCurrent.action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"GameManager: event '{_eventQueueCurrent.label}' threw: {ex}");
            }

            // Hold until the animation finishes (precise predicate) OR the safety duration
            // elapses, whichever comes first. A short grace frame lets the action's animation
            // command take effect before we start polling the animator state.
            yield return null;

            float hold = Mathf.Min(_eventQueueCurrent.duration, Mathf.Max(0f, maxEventHoldSeconds));
            float elapsed = 0f;
            while (elapsed < hold)
            {
                if (_eventQueueCurrent.isFinished != null)
                {
                    bool done;
                    try { done = _eventQueueCurrent.isFinished(); }
                    catch { done = true; }
                    if (done) break;
                }
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _eventQueueCurrent = null;
        }

        _eventQueueRunner = null;
    }
}
