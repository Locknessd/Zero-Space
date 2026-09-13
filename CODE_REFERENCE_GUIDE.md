# Meme Battle — Code Reference & Integration Guide

## Quick Start for Developers

### 1. Setup WebSocketManager

```csharp
// In your scene, create an empty GameObject named "WebSocketManager"
// Attach the WebSocketManager script

// In Inspector:
[✓] Server URL:      http://<backend-host>:9092
[✓] Auto Reconnect:  true
[✓] Reconnect Delay: 3
[✓] Service Token:   test-unity-secret
[✓] Emit Start Match On Connect: true (for auto-testing)
[✓] Start Match Event Name:       start_match
[✓] Verbose Logging:              false (set true for debug)
```

### 2. Setup GameManager

```csharp
// In your BattleScene, create an empty GameObject named "GameManager"
// Attach the GameManager script

// In Inspector, assign references:
[✓] Player UI:        (PlayerUI component)
[✓] Round Manager:    (RoundManager component)
[✓] Animation Controller: (AnimationController component)
[✓] Initial Match ID: (optional, leave empty for now)
[✓] Enable Local Input Testing: false (only for Q/E debug keys)
```

### 3. Setup UI Components

```csharp
// Create a Canvas with:
// - PlayerUI script (health bars, dialogue, damage popups)
// - RoundManager script (timer, round number, multiplier)

// PlayerUI needs:
public Text leftNameText;
public Text rightNameText;
public Image leftHealthBar;
public Image rightHealthBar;
public Text leftHealthText;
public Text rightHealthText;
public Transform leftDamagePopupAnchor;
public Transform rightDamagePopupAnchor;
```

### 4. Start Match Programmatically

```csharp
// In LoadingManager or similar:
public async void StartMemeMatch()
{
    if (WebSocketManager.Instance == null)
    {
        Debug.LogError("WebSocketManager not found!");
        return;
    }

    // Wait for connection
    while (!WebSocketManager.Instance.IsConnected())
    {
        await Task.Delay(100);
    }

    // Emit start request
    var requestId = $"unity-start-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
    string payload = JsonConvert.SerializeObject(new { requestId });
    await WebSocketManager.Instance.SendMessageToServer("meme_battle_start", payload);
}
```

---

## Event Handling Reference

### Handle Incoming Event

```csharp
// In GameManager.HandleMemeBattleEvent()
void HandleMemeBattleEvent(MemeBattleEvent ev)
{
    var t = ev.eventType;
    var payload = ev.payload;  // JObject from Newtonsoft.Json.Linq

    switch (t)
    {
        case "MATCH_CREATED":
            HandleMatchCreated(payload);
            break;
        case "TURN_STARTED":
            HandleTurnStarted(payload);
            break;
        case "ARGUMENT_SELECTED":
            HandleArgumentSelected(payload);
            break;
        case "DAMAGE_APPLIED":
            HandleDamageApplied(payload);
            break;
        case "WINNER_DECLARED":
            HandleWinnerDeclared(payload);
            break;
        default:
            Debug.LogWarning($"Unknown event type: {t}");
            break;
    }
}
```

### Parse Event Payload

```csharp
// MATCH_CREATED payload
{
    "characterIds": ["bot_a", "bot_b"],
    "initialHpAtomic": 1000,
    "rulesetVersion": 1,
    "rngCommitment": "..."
}

// Extract in code:
var payload = ev.payload;  // JObject
var characterIds = payload.Value<JArray>("characterIds");
var initialHp = payload.Value<long?>("initialHpAtomic") ?? 1000;

// Iterate array:
if (characterIds != null)
{
    foreach (var charId in characterIds)
    {
        Debug.Log($"Character: {charId}");
    }
}
```

---

## Animation Integration

### Play Single Animation

```csharp
// Play attack animation on Left
animationController?.PlayAnimation(PlayerUI.Side.Left, "bot_a_attack_light");

// Play hit animation on Right
animationController?.PlayAnimation(PlayerUI.Side.Right, "bot_b_hit_light");

// What happens internally:
// 1. ResolveAnimationId("bot_a_attack_light") → "bot_a_attack_light_3"
//    (picks random variant 1-5)
// 2. animator.CrossFade("bot_a_attack_light_3", 0.12f)
```

### Play Matched Attack & Hit

```csharp
// When ARGUMENT_SELECTED event arrives:
// Backend specifies: animationId = "bot_a_attack_light"
// We need to:
//   1. Determine attacker side
//   2. Determine target side
//   3. Convert attack → hit
//   4. Play both with same variant number

animationController?.PlayBothAnimationsWithMatchedVariant(
    attackerSide: PlayerUI.Side.Left,
    attackAnimId: "bot_a_attack_light",
    targetSide: PlayerUI.Side.Right,
    hitAnimId: "bot_a_hit_light"  // Convert by replacing "attack" with "hit"
);

// Result:
// Attacker (Left): plays "bot_a_attack_light_3"
// Target (Right):  plays "bot_a_hit_light_3"  (same variant)
// Both crossfade in 0.12s
// Both auto-return to Idle
```

### Animation State Resolver

```csharp
// Internal: AnimationController.ResolveAnimationId()
private string ResolveAnimationId(string animId)
{
    // Input: "bot_a_attack_light"
    // Output: "bot_a_attack_light_3" (random variant 1-5)

    if (string.IsNullOrEmpty(animId))
        return "Idle";  // fallback

    // Pick random variant
    int variant = Random.Range(1, attackVariants + 1);
    string stateName = $"{animId}_{variant}";

    // Verify state exists in Animator
    if (leftAnimator != null && leftAnimator.HasState(0, Animator.StringToHash(stateName)))
        return stateName;

    // Fallback to base name
    return animId;
}
```

### Supported Animation Names

```
Format: {character}_{action}_{variant}

Attacker animations:
  bot_a_attack_light_1   (light attack variant 1)
  bot_a_attack_light_2
  bot_a_attack_light_3
  bot_a_attack_light_4
  bot_a_attack_light_5
  bot_a_attack_heavy_1   (heavy attack variant 1)
  bot_a_attack_heavy_2
  ...

Defender animations (hit reactions):
  bot_a_hit_light_1      (light hit reaction variant 1)
  bot_a_hit_light_2
  ...
  bot_a_hit_heavy_1      (heavy hit reaction variant 1)
  ...

Special animations:
  bot_a_idle             (idle stance)
  bot_a_victory          (victory pose)
  bot_a_defeat           (defeat pose)
```

---

## UI Updates Reference

### Update Health Bar

```csharp
// Method: PlayerUI.UpdateHealthDisplay()
playerUI?.UpdateHealthDisplay(
    side: PlayerUI.Side.Left,
    currentHp: 850,
    maxHp: 1000
);

// Updates:
// - Health bar fill (85%)
// - Health text ("850 / 1000")
// - Bar color (Green if >50%, Yellow if <50%, Red if <25%)
```

### Update Character Name

```csharp
playerUI?.UpdateName(PlayerUI.Side.Left, "bot_a");
playerUI?.UpdateName(PlayerUI.Side.Right, "bot_b");

// Updates:
// - Character name text above health bar
// - Used in MATCH_CREATED event
```

### Show Dialogue/Meme Text

```csharp
// When ARGUMENT_SELECTED arrives with meme text:
playerUI?.SetDialogue(PlayerUI.Side.Left, "Skill issue detected.");

// Displays on-screen:
// "Skill issue detected." above character

// Clear at start of next turn:
playerUI?.ClearDialogue(PlayerUI.Side.Left);
playerUI?.ClearDialogue(PlayerUI.Side.Right);
```

### Show Damage Popup

```csharp
// When DAMAGE_APPLIED event arrives:
playerUI?.ShowDamage(PlayerUI.Side.Right, 150);

// Displays:
// +150 (red text, floats up, fades out over 1-2 seconds)
```

### Start Turn Timer

```csharp
// When TURN_STARTED arrives:
roundManager?.StartTurnTimer(
    turnNumber: 1,
    opensAtIsoUtc: "2026-09-06T10:00:00Z",
    closesAtIsoUtc: "2026-09-06T10:00:05Z"
);

// Updates:
// - Round number text: "ROUND 1"
// - Timer: "00:05" then counts down to "00:00"
```

### Show Multiplier

```csharp
// When MULTIPLIER_SELECTED arrives:
// multiplierScaled = 15000, multiplierScale = 10000
float mult = (float)15000 / 10000;  // 1.5f
roundManager?.ShowMultiplier(mult);

// Displays: "1.5x"
```

### Show Voting Totals

```csharp
// When ARGUMENT_SELECTED arrives:
var totals = new Dictionary<string, long>
{
    { "turn_1_A", 0 },
    { "turn_1_B", 101 },
    { "turn_1_C", 102 }
};
roundManager?.ShowFinalTotals(totals);

// Could display:
// A: 0 votes
// B: 101 votes ✓
// C: 102 votes ← Winner
```

---

## State Management Reference

### Track Character Max HP

```csharp
// In GameManager:
private Dictionary<string, long> _characterMaxHp = 
    new Dictionary<string, long>();

// Set during MATCH_CREATED:
_characterMaxHp["bot_a"] = 1000;
_characterMaxHp["bot_b"] = 1000;

// Use during DAMAGE_APPLIED:
_characterMaxHp.TryGetValue("bot_b", out long maxHp);
playerUI?.UpdateHP(PlayerUI.Side.Right, currentHp, maxHp);
```

### Track Match Snapshot

```csharp
// In GameManager:
private MemeBattleMatchSnapshot _latestSnapshot;

// Update on start_result or snapshot event:
_latestSnapshot = snapResp.snapshot;

// Access current state:
if (_latestSnapshot != null)
{
    var currentTurn = _latestSnapshot.currentTurn;
    var matchId = _latestSnapshot.matchId;
    var winner = _latestSnapshot.winnerCharacterId;
}
```

### Track Character Names

```csharp
// In GameManager:
private Dictionary<string, string> _characterNames = 
    new Dictionary<string, string>();

// Store during MATCH_CREATED:
_characterNames["bot_a"] = "bot_a";
_characterNames["bot_b"] = "bot_b";

// Retrieve for display:
_characterNames.TryGetValue("bot_a", out var name);
Debug.Log($"Character: {name}");
```

---

## Error Handling

### Handle Connection Errors

```csharp
// In WebSocketManager:
_ws.OnError += (sender, err) =>
{
    var code = MapErrorToCode(err);  // SOCKET_AUTH_REQUIRED, etc.
    EnqueueError(code, err);
    Debug.LogError($"WebSocketManager: OnError: {err}");
};

// Map error message to code:
private string MapErrorToCode(string msg)
{
    if (string.IsNullOrEmpty(msg)) return "UNKNOWN_ERROR";
    msg = msg.ToLowerInvariant();

    if (msg.Contains("valid socket.io credentials"))
        return "SOCKET_AUTH_REQUIRED";

    if (msg.Contains("not authenticated"))
        return "UNITY_AUTH_REQUIRED";

    if (msg.Contains("match not found"))
        return "MATCH_NOT_FOUND";

    return "UNKNOWN_ERROR";
}
```

### Handle Match Start Errors

```csharp
// In GameManager.HandleRawMessage():
case "meme_battle_start_result":
{
    var res = j["result"];
    if (res != null)
    {
        var err = res["error"];
        if (err != null)
        {
            var code = err.Value<string>("code");
            var msg = err.Value<string>("message");

            Debug.LogError($"GameManager: start_result error {code}: {msg}");

            // Show error to user
            var lm = FindObjectOfType<LoaddingManager>();
            if (lm != null)
                lm.HandleExternalError(code, msg);

            break;
        }
    }

    // Process success...
}
```

### Reconnection Handling

```csharp
// In WebSocketManager:
_ws.OnDisconnected += async (sender, e) =>
{
    Debug.Log($"WebSocketManager: Disconnected: {e}");
    _ws = null;

    if (_autoReconnect && !_isClosing)
    {
        // Wait before attempting reconnect
        await Task.Delay(TimeSpan.FromSeconds(_reconnectDelay));

        // Attempt reconnect
        _ = ConnectAsync();
    }
};

// On reconnect success:
_ws.OnConnected += async (sender, e) =>
{
    Debug.Log("WebSocketManager: Connected.");

    // Re-subscribe to match if we were in one
    if (!string.IsNullOrEmpty(_currentMatchId))
    {
        await SubscribeToMatch(_currentMatchId, _lastAppliedSequence);
    }
};
```

---

## Networking Details

### WebSocket Connection Parameters

```csharp
var options = new SocketIOOptions
{
    Auth = new
    {
        clientType = "UNITY_RENDERER",
        serviceToken = _serviceToken
    },
    Transport = SocketIOClient.Transport.TransportProtocol.WebSocket,
    EIO = SocketIOClient.EngineIO.V4,
    Path = "/socket.io"
};

_ws = new SocketIO(_serverUrl, options);
await _ws.ConnectAsync();
```

### Emit Event to Server

```csharp
// Simple emit (no args):
await _ws.EmitAsync("event_name");

// Emit with JSON string:
string payload = JsonConvert.SerializeObject(new { data = "value" });
await _ws.EmitAsync("meme_battle_start", payload);

// Emit with object:
await _ws.EmitAsync("event_name", new { key = "value" });
```

### Listen for Events

```csharp
// Specific event handler:
_ws.On("meme_battle_event", response =>
{
    var ev = response.GetValue<MemeBattleEvent>();
    // Process event...
});

// Catch-all handler (optional):
_ws.On("*", response =>
{
    Debug.Log($"Any event: {response.ToString()}");
});
```

### Subscribe to Match

```csharp
public async Task SubscribeToMatch(string matchId, long afterSequence = 0)
{
    if (_ws == null || !_ws.Connected)
    {
        Debug.LogWarning("Socket not connected");
        return;
    }

    var req = new SubscribeRequest 
    { 
        matchId = matchId, 
        afterSequence = afterSequence 
    };

    string payload = JsonConvert.SerializeObject(req);
    await EmitLoggedAsync("meme_battle_subscribe", payload);
}
```

---

## Data Models

### Event Envelope

```csharp
[Serializable]
public class MemeBattleEvent
{
    public string eventId;           // "evt_abc123"
    public string eventType;         // "ARGUMENT_SELECTED"
    public string matchId;           // "match_abc"
    public string turnId;            // "turn_001" (nullable)
    public long sequence;            // 5, 6, 7...
    public int schemaVersion;        // 1
    public string occurredAt;        // "2026-09-06T10:00:00Z"
    public JObject payload;          // Event-specific data
}
```

### Match Snapshot

```csharp
[Serializable]
public class MemeBattleMatchSnapshot
{
    public string matchId;
    public string state;             // "ACTIVE", "FINISHED"
    public Dictionary<string, long> characterHpAtomic;        // {"bot_a": 1000, "bot_b": 850}
    public Dictionary<string, long> characterMaxHpAtomic;     // {"bot_a": 1000, "bot_b": 1000}
    public string currentTurnId;
    public MemeBattleTurnSnapshot currentTurn;
    public string winnerCharacterId; // "bot_a", "bot_b", "DRAW", null
    public bool contributionWindowOpen;
    public long latestSequence;      // Latest processed event sequence
    public int rulesetVersion;
    public string rngCommitment;
}
```

### Turn Snapshot

```csharp
[Serializable]
public class MemeBattleTurnSnapshot
{
    public string turnId;
    public int turnNumber;
    public string state;             // "VOTING", "RESOLVED"
    public string voteWindowId;
    public List<Argument> arguments; // Available actions
    public Dictionary<string, long> finalTotalsAtomic;
    public string selectedArgumentId;
    public int multiplierScaled;
    public string opensAt;           // ISO-8601
    public string closesAt;          // ISO-8601
    public string resolvedAt;        // ISO-8601
}
```

### Argument Definition

```csharp
[Serializable]
public class Argument
{
    public string argumentId;        // "turn_1_A"
    public string targetCharacterId; // "bot_b"
    public long baseDamageAtomic;    // 100
    public string animationId;       // "bot_a_attack_light"
}
```

---

## Testing & Debugging

### Enable Verbose Logging

```csharp
// In Inspector, set:
WebSocketManager.verboseLogging = true;

// Output:
// WebSocketManager INCOMING [2026-09-06T10:00:00.123Z] event=meme_battle_event
// WebSocketManager OUTGOING [2026-09-06T10:00:00.456Z]: Emit → event=meme_battle_start
```

### Debug Local Input

```csharp
// In GameManager Inspector, set:
enableLocalInputTesting = true;

// Then press:
// Q: Left attacks with random preset
// E: Right attacks with random preset
```

### Test Animation Playback

```csharp
// Direct call:
animationController?.PlayAnimation(PlayerUI.Side.Left, "bot_a_attack_light");

// Check Animator state:
Debug.Log(leftAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash);
```

### Simulate Event

```csharp
// In GameManager:
string simulatedJson = JsonConvert.SerializeObject(new 
{
    type = "meme_battle_event",
    @event = new 
    {
        eventType = "ARGUMENT_SELECTED",
        payload = new
        {
            actorCharacterId = "bot_a",
            targetCharacterId = "bot_b",
            memeText = "Test",
            animationId = "bot_a_attack_light"
        }
    }
});

GameManager.Instance.ApplyRawMessage(simulatedJson);
```

---

## Common Integration Patterns

### Auto-Start Match on Scene Load

```csharp
// In LoadingManager:
public async void LoadBattleScene()
{
    await SceneManager.LoadSceneAsync("BattleScene", LoadSceneMode.Single);

    // Wait for GameManager to initialize
    await Task.Delay(100);

    // Trigger match start
    if (WebSocketManager.Instance != null)
    {
        if (!WebSocketManager.Instance.IsConnected())
        {
            WebSocketManager.Instance.OnConnectionProgress += OnConnected;
        }
        else
        {
            StartMatchRequest();
        }
    }
}

void OnConnected(float progress, string status)
{
    if (progress >= 1f)
    {
        StartMatchRequest();
        WebSocketManager.Instance.OnConnectionProgress -= OnConnected;
    }
}

async void StartMatchRequest()
{
    var requestId = $"unity-start-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
    string payload = JsonConvert.SerializeObject(new { requestId });

    // Emit via WebSocketManager
    var ws = WebSocketManager.Instance;
    // (Ideally add a public method to emit arbitrary events)
}
```

### Display Error Message

```csharp
// In LoadingManager:
public void HandleExternalError(string code, string message)
{
    Debug.LogError($"Game Error [{code}]: {message}");

    // Map code to user-friendly message
    string userMessage = code switch
    {
        "SOCKET_AUTH_REQUIRED" => "Authentication failed. Check server token.",
        "MATCH_NOT_FOUND" => "Match not found. Try starting a new match.",
        "MATCH_SUBSCRIBE_INVALID" => "Invalid match parameters.",
        _ => $"Error: {message}"
    };

    // Show on UI
    errorPanel?.SetActive(true);
    errorText?.SetText(userMessage);
}
```

### Handle Match Victory

```csharp
// In GameManager.HandleMemeBattleEvent():
case "WINNER_DECLARED":
{
    var winner = payload.Value<string>("winnerCharacterId");
    _matchEnded = true;

    if (winner == "DRAW")
    {
        Debug.Log("Match ended in a DRAW!");
        playerUI?.ShowDraw();
    }
    else
    {
        var winnerSide = SideFromCharacterId(winner);
        Debug.Log($"Player {winnerSide} wins!");

        playerUI?.ShowVictory(winnerSide.Value);
        animationController?.PlayAnimation(winnerSide.Value, "Victory");
    }

    // Stop timer
    roundManager?.StopTimer();
}
break;
```

### Monitor Connection Status

```csharp
// Periodically check:
public void CheckConnectionStatus()
{
    if (WebSocketManager.Instance == null)
    {
        Debug.LogError("WebSocketManager not found!");
        return;
    }

    if (WebSocketManager.Instance.IsConnected())
    {
        connectionStatusText.text = "🟢 Connected";
        connectionStatusText.color = Color.green;
    }
    else
    {
        connectionStatusText.text = "🔴 Disconnected";
        connectionStatusText.color = Color.red;
    }
}
```

---

## Performance Considerations

### Memory Management

```csharp
// Reuse ConcurrentQueues instead of creating new ones
private readonly ConcurrentQueue<string> _incomingMessages = 
    new ConcurrentQueue<string>();

// Limit queue size if needed
while (_incomingMessages.Count > MAX_QUEUE_SIZE)
{
    _incomingMessages.TryDequeue(out _);  // Discard oldest
}
```

### Network Bandwidth

```csharp
// Only send necessary data
var req = new SubscribeRequest 
{ 
    matchId = matchId,
    afterSequence = _lastAppliedSequence  // Request only new events
};

// Server only sends events > afterSequence, saving bandwidth
```

### Animation Performance

```csharp
// Use crossfade instead of direct state change
animator.CrossFade(stateName, 0.12f);  // Smooth transition

// Not: animator.SetTrigger(...) with OnEnter transitions
// (Harder to sync between two animators)
```

---

## Troubleshooting

### Issue: Events not received

**Checklist:**
1. ✓ WebSocketManager.IsConnected() returns true?
2. ✓ Service token is correct?
3. ✓ Server URL is correct?
4. ✓ Firewall allows WebSocket?
5. ✓ Event handlers registered before connecting?

**Test:**
```csharp
Debug.Log($"Connected: {WebSocketManager.Instance.IsConnected()}");
```

### Issue: Animation not playing

**Checklist:**
1. ✓ Animator assigned in Inspector?
2. ✓ State exists in Animator Controller?
3. ✓ State name matches expected format?
4. ✓ No other code resetting animation?
5. ✓ Root Motion disabled?

**Test:**
```csharp
Debug.Log(animator.GetCurrentAnimatorStateInfo(0).fullPathHash);
```

### Issue: Health bar not updating

**Checklist:**
1. ✓ HP_CHANGED or DAMAGE_APPLIED events received?
2. ✓ PlayerUI component assigned?
3. ✓ Health bar Image component assigned?
4. ✓ MAX HP value set correctly?
5. ✓ Update method called?

**Test:**
```csharp
playerUI?.UpdateHealthDisplay(PlayerUI.Side.Left, 500, 1000);
```

### Issue: Reconnection loops

**Checklist:**
1. ✓ _autoReconnect = true?
2. ✓ _reconnectDelay > 0?
3. ✓ Check server logs for auth errors?
4. ✓ Service token not expired?

**Solution:**
```csharp
// Temporary: disable auto-reconnect to debug
WebSocketManager.Instance._autoReconnect = false;
```

