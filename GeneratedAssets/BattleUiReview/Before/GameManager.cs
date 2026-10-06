using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Serialization;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("UI")]
    public MemeBattleUI uiManager;
    public PlayerUI playerUI;
    public RoundManager roundManager;

    [Header("Combat")]
    [FormerlySerializedAs("leftBridge")] public CharacterCombat leftCombat;
    [FormerlySerializedAs("rightBridge")] public CharacterCombat rightCombat;
    public CombatPositioningController positioningController;
    public bool choreographAttackPositions = true;
    [Min(1f)] public float maxEventHoldSeconds = 30f;

    [Header("Connection / Debug")]
    public string initialMatchId = "";
    public long defaultInitialMaxHpAtomic = 1000;
    public bool enableLocalInputTesting = true;
    public bool debugMode = true;

    [Header("Battle Audio")]
    public BattleSfxPlayer battleSfx;

    [Header("Battle VFX")]
    public BattleVfxPlayer battleVfx;

    public int PendingEventCount => _queue.Count;
    public bool IsEventQueueBusy => _running || _queue.Count > 0;
    public string QueueError { get; private set; }

    private sealed class BattleStack
    {
        public string key;
        public bool ready;
        public readonly List<MemeBattleEvent> events = new List<MemeBattleEvent>();
        public Action apply;
        public PlayerUI.Side? localAttacker;
        public bool heavy;
    }

    private readonly Queue<BattleStack> _queue = new Queue<BattleStack>();
    private readonly Dictionary<string, BattleStack> _pendingTurns = new Dictionary<string, BattleStack>();
    private readonly Dictionary<string, MemeBattleEvent> _arguments = new Dictionary<string, MemeBattleEvent>();
    private readonly HashSet<string> _receivedEvents = new HashSet<string>();
    private readonly HashSet<string> _playedTurns = new HashSet<string>();
    private readonly HashSet<string> _hpChangedAppliedKeys = new HashSet<string>();
    private Dictionary<string, long> _characterMaxHp = new Dictionary<string, long>();
    private readonly Dictionary<string, string> _characterNames = new Dictionary<string, string>();
    private MemeBattleMatchSnapshot _latestSnapshot;
    private string _collectingTurn;
    private bool _running;
    private bool _matchEnded;
    private bool _swapSpeechDuringMatch = true;
    private WebSocketManager _socket;
    private Coroutine _runner;

    private MemeBattleUI UI => uiManager != null ? uiManager : MemeBattleUI.Instance;
    private PlayerUI LegacyUI(PlayerUI.Side side) => playerUI;
    private static MemeBattleUI.Side ToUISide(PlayerUI.Side side) =>
        side == PlayerUI.Side.Left ? MemeBattleUI.Side.Left : MemeBattleUI.Side.Right;
    private CharacterCombat CombatFor(PlayerUI.Side side) =>
        side == PlayerUI.Side.Left ? leftCombat : rightCombat;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (uiManager == null) uiManager = MemeBattleUI.Instance;
        if (positioningController == null) positioningController = CombatPositioningController.Instance;
        if (leftCombat != null) leftCombat.Initialize();
        if (rightCombat != null) rightCombat.Initialize();
        if (positioningController != null)
            positioningController.SetFighters(leftCombat != null ? leftCombat.Animator.transform : null,
                rightCombat != null ? rightCombat.Animator.transform : null);
        _characterMaxHp["bot_a"] = defaultInitialMaxHpAtomic;
        _characterMaxHp["bot_b"] = defaultInitialMaxHpAtomic;
        UI?.ResetForNewMatch();
        SubscribeSocket();
    }

    private void SubscribeSocket()
    {
        if (_socket != null || WebSocketManager.Instance == null) return;
        _socket = WebSocketManager.Instance;
        _socket.OnRawMessageReceived += HandleRawMessage;
        if (string.IsNullOrEmpty(initialMatchId)) return;
        if (_socket.IsConnected()) _ = _socket.SubscribeToMatch(initialMatchId, 0);
        else _socket.OnConnectionProgress += WaitAndSubscribe;
    }

    private void WaitAndSubscribe(float progress, string status)
    {
        if (progress < 1f || _socket == null || string.IsNullOrEmpty(initialMatchId)) return;
        _ = _socket.SubscribeToMatch(initialMatchId, 0);
        _socket.OnConnectionProgress -= WaitAndSubscribe;
    }

    public void ApplyRawMessage(string json) => HandleRawMessage(json);

    private void HandleRawMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var message = JObject.Parse(json);
            if (message.Value<string>("type") == "meme_battle_event")
            {
                HandleMemeBattleEvent(message["event"]?.ToObject<MemeBattleEvent>());
                return;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("GameManager: invalid message: " + exception.Message);
            return;
        }
        CloseCollectingTurn();
        _queue.Enqueue(new BattleStack { ready = true, apply = () => ApplyRawMessageNow(json) });
    }

    private static string TurnKey(MemeBattleEvent battleEvent) =>
        battleEvent.matchId + ":" + battleEvent.turnId;

    private void HandleMemeBattleEvent(MemeBattleEvent battleEvent)
    {
        if (battleEvent == null || battleEvent.payload == null) return;
        string identity = !string.IsNullOrEmpty(battleEvent.eventId)
            ? battleEvent.matchId + ":event:" + battleEvent.eventId
            : battleEvent.sequence > 0 ? battleEvent.matchId + ":sequence:" + battleEvent.sequence : null;
        if (identity != null && !_receivedEvents.Add(identity)) return;

        if (string.IsNullOrEmpty(battleEvent.turnId) || battleEvent.eventType == "WINNER_DECLARED")
        {
            CloseCollectingTurn();
            var single = new BattleStack { ready = true };
            single.events.Add(battleEvent);
            _queue.Enqueue(single);
            return;
        }

        string key = TurnKey(battleEvent);
        if (_collectingTurn != key) CloseCollectingTurn();
        _collectingTurn = key;
        if (!_pendingTurns.TryGetValue(key, out var stack))
        {
            stack = new BattleStack { key = key };
            _pendingTurns.Add(key, stack);
            _queue.Enqueue(stack);
        }
        stack.events.Add(battleEvent);
        if (battleEvent.eventType == "DAMAGE_APPLIED" || battleEvent.eventType == "HP_CHANGED" ||
            battleEvent.eventType == "TURN_RESOLVED" || _playedTurns.Contains(key))
            stack.ready = true;
    }

    private void CloseCollectingTurn()
    {
        if (_collectingTurn != null && _pendingTurns.TryGetValue(_collectingTurn, out var stack))
            stack.ready = true;
        _collectingTurn = null;
    }

    private void Update()
    {
        SubscribeSocket();
        if (enableLocalInputTesting)
        {
            if (IsKeyDown(KeyCode.Q)) DebugTriggerQ();
            if (IsKeyDown(KeyCode.E)) DebugTriggerE();
        }
        if (!_running && QueueError == null && _queue.Count > 0 && _queue.Peek().ready)
        {
            _running = true;
            _runner = StartCoroutine(RunQueue());
        }
    }

    private IEnumerator RunQueue()
    {
        try
        {
            while (QueueError == null && _queue.Count > 0 && _queue.Peek().ready)
            {
                BattleStack stack = _queue.Dequeue();
                if (stack.key != null) _pendingTurns.Remove(stack.key);
                yield return RunSafely(ProcessStack(stack));
            }
        }
        finally
        {
            _running = false;
            _runner = null;
        }
    }

    private IEnumerator RunSafely(IEnumerator action)
    {
        var routines = new Stack<IEnumerator>();
        routines.Push(action);
        try
        {
            while (routines.Count > 0)
            {
                IEnumerator current = routines.Peek();
                bool next = false;
                object yielded = null;
                try
                {
                    next = current.MoveNext();
                    if (next) yielded = current.Current;
                }
                catch (Exception exception)
                {
                    Fault(exception.ToString());
                    yield break;
                }
                if (!next)
                {
                    (routines.Pop() as IDisposable)?.Dispose();
                    continue;
                }
                if (yielded is IEnumerator nested) routines.Push(nested);
                else yield return yielded;
            }
        }
        finally
        {
            while (routines.Count > 0) (routines.Pop() as IDisposable)?.Dispose();
        }
    }

    private IEnumerator ProcessStack(BattleStack stack)
    {
        stack.apply?.Invoke();
        if (stack.localAttacker.HasValue)
        {
            yield return RunExchange(stack.localAttacker.Value, stack.heavy, false, null);
            yield break;
        }

        MemeBattleEvent argument = null;
        MemeBattleEvent damage = null;
        MemeBattleEvent hp = null;
        foreach (var battleEvent in stack.events)
        {
            if (battleEvent.eventType == "ARGUMENT_SELECTED")
            {
                argument = battleEvent;
                if (stack.key != null) _arguments[stack.key] = battleEvent;
            }
            if (battleEvent.eventType == "DAMAGE_APPLIED") damage = battleEvent;
            if (battleEvent.eventType == "HP_CHANGED") hp = battleEvent;
            if (battleEvent.eventType != "DAMAGE_APPLIED" && battleEvent.eventType != "HP_CHANGED")
                ApplyMemeBattleEvent(battleEvent);
        }
        if (argument == null && stack.key != null) _arguments.TryGetValue(stack.key, out argument);

        string targetId = hp?.payload.Value<string>("characterId") ??
                          damage?.payload.Value<string>("targetCharacterId");
        long hpAfter = hp?.payload.Value<long?>("hpAfterAtomic") ??
                       damage?.payload.Value<long?>("hpAfterAtomic") ?? -1;
        long damageAmount = damage?.payload.Value<long?>("damageAtomic") ?? 0;
        string animationId = argument?.payload.Value<string>("animationId") ??
                             damage?.payload.Value<string>("animationId") ?? "";
        PlayerUI.Side? attacker = SideFromCharacterId(argument?.payload.Value<string>("actorCharacterId") ??
                                                       damage?.payload.Value<string>("actorCharacterId"));
        PlayerUI.Side? receiver = SideFromCharacterId(argument?.payload.Value<string>("targetCharacterId") ?? targetId);
        if (!attacker.HasValue && receiver.HasValue && damage != null)
            attacker = receiver.Value == PlayerUI.Side.Left ? PlayerUI.Side.Right : PlayerUI.Side.Left;
        bool resolved = damage != null || hp != null;
        bool hasExchange = resolved && attacker.HasValue && receiver.HasValue && attacker != receiver &&
                           (stack.key == null || !_playedTurns.Contains(stack.key));
        Action applyHealth = () =>
        {
            ApplyTurnHpOnce(stack.key, targetId, hpAfter, damageAmount, animationId);
            if (_latestSnapshot?.characterHpAtomic != null && !string.IsNullOrEmpty(targetId) && hpAfter >= 0)
                _latestSnapshot.characterHpAtomic[targetId] = hpAfter;
        };

        if (hasExchange)
        {
            yield return RunExchange(attacker.Value, IsHeavy(animationId), hpAfter == 0, applyHealth);
            if (QueueError == null && stack.key != null)
            {
                _playedTurns.Add(stack.key);
                if (_pendingTurns.TryGetValue(stack.key, out var lateStack)) lateStack.ready = true;
            }
        }
        else
        {
            applyHealth();
            if (hpAfter == 0 && receiver.HasValue) CombatFor(receiver.Value)?.MarkDead();
        }

        if (QueueError != null) yield break;
        if (leftCombat != null && leftCombat.IsBusy) yield return WaitForSequence(leftCombat);
        if (rightCombat != null && rightCombat.IsBusy) yield return WaitForSequence(rightCombat);
    }

    private IEnumerator RunExchange(PlayerUI.Side side, bool heavy, bool lethal, Action applyHealth)
    {
        CharacterCombat attacker = CombatFor(side);
        CharacterCombat receiver = CombatFor(side == PlayerUI.Side.Left ? PlayerUI.Side.Right : PlayerUI.Side.Left);
        if (attacker == null || receiver == null || !attacker.Initialize() || !receiver.Initialize())
        {
            Fault("Chưa cấu hình đủ hai CharacterCombat.");
            yield break;
        }
        CombatTripletData move = heavy ? attacker.GetRandomHeavyMove() : attacker.GetRandomLightMove();
        if (move == null)
        {
            Fault("Pool " + (heavy ? "Heavy" : "Light") + " không có bộ animation hợp lệ trên " + attacker.name);
            yield break;
        }
        if (choreographAttackPositions && positioningController != null)
            yield return positioningController.MoveIntoRange(attacker, receiver, move.attackRange);

        bool attackDone = false;
        bool receiverDone = false;
        bool succeeded = true;
        int attackId = attacker.PlaybackId + 1;
        int receiverId = receiver.PlaybackId + 1;
        Action<CharacterCombat, int, bool> onEnd = (combat, playback, completed) =>
        {
            if (combat == attacker && playback == attackId) { attackDone = true; succeeded &= completed; }
            if (combat == receiver && playback == receiverId) { receiverDone = true; succeeded &= completed; }
        };
        attacker.SequenceEnded += onEnd;
        receiver.SequenceEnded += onEnd;
        try
        {
            if (!attacker.ExecuteAttack(move, receiver, lethal))
            {
                Fault("Không thể bắt đầu đòn " + move.moveName + ".");
                yield break;
            }
            applyHealth?.Invoke();
            MortalKombatCamera.Instance?.OnCharacterAttack(attacker.Animator.transform);
            MortalKombatCamera.Instance?.TriggerImpactShake();
            float elapsed = 0f;
            while (!attackDone || !receiverDone)
            {
                if (!succeeded || attacker == null || receiver == null)
                {
                    Fault("Animation bị ngắt trước khi hoàn tất.");
                    yield break;
                }
                if (elapsed > maxEventHoldSeconds)
                {
                    Fault("Không nhận đủ event end anim; queue đã dừng để không cắt lượt đang chạy.");
                    yield break;
                }
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (!succeeded)
            {
                Fault("Animation bị ngắt trước khi hoàn tất.");
                yield break;
            }
            positioningController?.FinishExchange(leftCombat, rightCombat);
            if (!lethal) MortalKombatCamera.Instance?.EndAttackFocus();
            if (debugMode) Debug.Log("Combat stack completed: " + move.moveName);
        }
        finally
        {
            if (attacker != null) attacker.SequenceEnded -= onEnd;
            if (receiver != null) receiver.SequenceEnded -= onEnd;
        }
    }

    private IEnumerator WaitForSequence(CharacterCombat combat)
    {
        bool done = !combat.IsBusy;
        bool succeeded = combat.LastSequenceSucceeded;
        int playback = combat.PlaybackId;
        Action<CharacterCombat, int, bool> onEnd = (actor, id, completed) =>
        {
            if (id != playback) return;
            done = true;
            succeeded = completed;
        };
        combat.SequenceEnded += onEnd;
        try
        {
            float elapsed = 0f;
            while (!done && combat != null && elapsed <= maxEventHoldSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
            if (!done || !succeeded) Fault("Animation kết thúc không hợp lệ hoặc thiếu callback.");
        }
        finally
        {
            if (combat != null) combat.SequenceEnded -= onEnd;
        }
    }

    private void Fault(string reason)
    {
        QueueError = reason;
        Debug.LogError("GameManager combat queue: " + reason, this);
    }

    private static bool IsHeavy(string animationId) =>
        !string.IsNullOrEmpty(animationId) && animationId.IndexOf("heavy", StringComparison.OrdinalIgnoreCase) >= 0;

    public void DebugTriggerQ() => EnqueueLocalAttack(PlayerUI.Side.Left, false);
    public void DebugTriggerE() => EnqueueLocalAttack(PlayerUI.Side.Right, true);

    public void EnqueueLocalAttack(PlayerUI.Side side, bool heavy)
    {
        if (!Application.isPlaying) return;
        CloseCollectingTurn();
        _queue.Enqueue(new BattleStack { ready = true, localAttacker = side, heavy = heavy });
    }

    public void PlaySingleAnimation(PlayerUI.Side side, string animationId, float crossFade = -1f)
    {
        if (string.IsNullOrEmpty(animationId)) return;
        if (animationId.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0)
            EnqueueLocalAttack(side, IsHeavy(animationId));
        else if (animationId.Equals("victory", StringComparison.OrdinalIgnoreCase))
        {
            CloseCollectingTurn();
            _queue.Enqueue(new BattleStack { ready = true, apply = () => CombatFor(side)?.PlayVictory() });
        }
    }

    public void PlayBothAnimations(string leftAnimationId, string rightAnimationId, float crossFade = -1f)
    {
        bool rightAttacks = rightAnimationId != null &&
                           rightAnimationId.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0;
        EnqueueLocalAttack(rightAttacks ? PlayerUI.Side.Right : PlayerUI.Side.Left,
            IsHeavy(rightAttacks ? rightAnimationId : leftAnimationId));
    }

    public void ClearEventQueue()
    {
        _queue.Clear();
        _pendingTurns.Clear();
        _collectingTurn = null;
    }

    [ContextMenu("Reset Combat Queue")]
    public void ResetCombatQueue()
    {
        if (battleSfx) battleSfx.ResetForMatch();
        if (battleVfx) battleVfx.ResetForMatch();
        if (_runner != null) StopCoroutine(_runner);
        _runner = null;
        _running = false;
        ClearEventQueue();
        QueueError = null;
        leftCombat?.ResetCombat();
        rightCombat?.ResetCombat();
        MortalKombatCamera.Instance?.EndAttackFocus();
    }

    private void OnDisable()
    {
        if (_running) Fault("GameManager bị tắt khi đang chạy lượt; reset queue trước khi chạy tiếp.");
        if (_runner != null) StopCoroutine(_runner);
        _runner = null;
        _running = false;
    }

    private void OnDestroy()
    {
        if (_socket != null)
        {
            _socket.OnRawMessageReceived -= HandleRawMessage;
            _socket.OnConnectionProgress -= WaitAndSubscribe;
        }
        if (Instance == this) Instance = null;
    }

    private void ApplyMemeBattleEvent(MemeBattleEvent battleEvent)
    {
        JObject payload = battleEvent.payload;
        switch (battleEvent.eventType)
        {
            case "MATCH_CREATED":
                if (battleSfx) battleSfx.ResetForMatch();
                if (battleVfx) battleVfx.ResetForMatch();
                _characterNames.Clear();
                _characterMaxHp.Clear();
                _hpChangedAppliedKeys.Clear();
                _playedTurns.Clear();
                _arguments.Clear();
                _matchEnded = false;
                _swapSpeechDuringMatch = true;
                leftCombat?.ResetCombat();
                rightCombat?.ResetCombat();
                UI?.ResetForNewMatch();
                var ids = payload["characterIds"] as JArray;
                long initialHp = payload.Value<long?>("initialHpAtomic") ?? defaultInitialMaxHpAtomic;
                if (ids != null && ids.Count >= 2)
                {
                    _characterNames["bot_a"] = ids[0].ToString();
                    _characterNames["bot_b"] = ids[1].ToString();
                }
                foreach (PlayerUI.Side side in new[] { PlayerUI.Side.Left, PlayerUI.Side.Right })
                {
                    string id = CharacterIdForSide(side);
                    _characterMaxHp[id] = initialHp;
                    _characterMaxHp[side == PlayerUI.Side.Left ? "bot_a" : "bot_b"] = initialHp;
                    UI?.SetFighterName(ToUISide(side), id);
                    UI?.UpdateHealth(ToUISide(side), initialHp, initialHp);
                }
                break;
            case "TURN_STARTED":
                roundManager?.StartTurnTimer(payload.Value<int?>("turnNumber") ?? 0,
                    payload.Value<string>("opensAt"), payload.Value<string>("closesAt"));
                UI?.ClearDialogue(MemeBattleUI.Side.Left);
                UI?.ClearDialogue(MemeBattleUI.Side.Right);
                break;
            case "ARGUMENT_SELECTED":
                var actor = SideFromCharacterId(payload.Value<string>("actorCharacterId"));
                string meme = payload.Value<string>("memeText");
                if (actor.HasValue && !string.IsNullOrEmpty(meme))
                {
                    PlayerUI.Side display = _swapSpeechDuringMatch && !_matchEnded
                        ? (actor.Value == PlayerUI.Side.Left ? PlayerUI.Side.Right : PlayerUI.Side.Left)
                        : actor.Value;
                    UI?.SetDialogue(ToUISide(display), meme);
                    UI?.ShowMemeResult(ToUISide(actor.Value), meme, 2f);
                }
                if (payload["finalTotalsAtomic"] != null)
                    roundManager?.ShowFinalTotals(payload["finalTotalsAtomic"].ToObject<Dictionary<string, long>>());
                break;
            case "MULTIPLIER_SELECTED":
                int scale = payload.Value<int?>("multiplierScale") ?? 1;
                roundManager?.ShowMultiplier(scale != 0 ? (payload.Value<int?>("multiplierScaled") ?? 0) /
                    (float)scale : 0f);
                break;
            case "WINNER_DECLARED":
                if (_matchEnded) break;
                _matchEnded = true;
                if (battleSfx) battleSfx.PlayVictoryOnce();
                _swapSpeechDuringMatch = false;
                var winner = SideFromCharacterId(payload.Value<string>("winnerCharacterId"));
                if (winner.HasValue)
                {
                    UI?.SetDialogue(ToUISide(winner.Value), "VICTORY!");
                    CombatFor(winner.Value)?.PlayVictory();
                    CombatFor(winner.Value == PlayerUI.Side.Left ? PlayerUI.Side.Right : PlayerUI.Side.Left)?.MarkDead();
                }
                break;
        }
    }

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

    private void ApplyTurnHpOnce(string turnId, string targetId, long hpAfter, long damage, string animationIdForWeight)
    {
        if (hpAfter < 0 || string.IsNullOrEmpty(targetId)) return;

        var side = SideFromCharacterId(targetId);
        if (!side.HasValue)
        {
            Debug.LogWarning($"Animation [GameManager] turn HP ignored: could not resolve '{targetId}' to a side.");
            return;
        }

        string hpKey = !string.IsNullOrEmpty(turnId) ? $"{turnId}:{side.Value}" : null;
        if (!string.IsNullOrEmpty(hpKey) && !_hpChangedAppliedKeys.Add(hpKey))
        {
            if (debugMode) AnimLogChecker.Log("GM", $"turn HP for {targetId} ignored: '{hpKey}' already applied.");
            return;
        }

        // Record the max candidate from any known hp value so the bar's max stays stable.
        EnsureKnownMaxHp(targetId, 0, hpAfter);

        ApplyHealthToUi(side.Value, hpAfter);
        if (damage > 0) uiManager?.ShowDamage(ToUISide(side.Value), (int)damage);

        if (debugMode)
            AnimLogChecker.Log("GM", $"turn HP applied ONCE for {targetId}: -{damage} = {hpAfter}");
    }

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

    private void ApplyHealthToUi(PlayerUI.Side side, long hpAfter)
    {
        string charId = CharacterIdForSide(side);
        long maxHp = ResolveMaxHpFor(charId, 0, hpAfter);
        uiManager?.UpdateHealth(ToUISide(side), hpAfter, maxHp);
    }

    private string CharacterIdForSide(PlayerUI.Side side)
    {
        string key = side == PlayerUI.Side.Left ? "bot_a" : "bot_b";
        return _characterNames.TryGetValue(key, out var id) && !string.IsNullOrEmpty(id) ? id : key;
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


    void ApplyRawMessageNow(string json)
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
}
