# Meme Battle — Understanding Your Codebase (From Docs to Code)

## How to Read This Guide

This document maps the architecture documentation directly to code locations in your project. Use this to navigate the codebase efficiently.

---

## Document Map

| Concept | Main Doc | Diagrams | Code Reference | Located In |
|---------|----------|----------|-----------------|-----------|
| Connection Flow | GAME_ARCHITECTURE_GUIDE.md § Network Flow | GAME_FLOW_DIAGRAMS.md § Phase 1 | CODE_REFERENCE_GUIDE.md § Networking | WebSocketManager.cs |
| Event Processing | GAME_ARCHITECTURE_GUIDE.md § Message Pipeline | GAME_FLOW_DIAGRAMS.md § Queue Processing | CODE_REFERENCE_GUIDE.md § Event Handling | GameManager.cs |
| Animation | GAME_ARCHITECTURE_GUIDE.md § Animation Coordination | GAME_FLOW_DIAGRAMS.md § Animation State Machine | CODE_REFERENCE_GUIDE.md § Animation Integration | AnimationController.cs |
| UI Updates | GAME_ARCHITECTURE_GUIDE.md § Event Handlers | GAME_FLOW_DIAGRAMS.md § Health Bar Update | CODE_REFERENCE_GUIDE.md § UI Updates | PlayerUI.cs, RoundManager.cs |
| Reconnect | GAME_ARCHITECTURE_GUIDE.md § Reconnection | GAME_FLOW_DIAGRAMS.md § Reconnection Flow | CODE_REFERENCE_GUIDE.md § Reconnection | WebSocketManager.cs |

---

## Quick Navigation: Find Code by Concept

### "I need to understand how events arrive"
1. Read: **GAME_ARCHITECTURE_GUIDE.md § Message Processing Pipeline**
2. See diagram: **GAME_FLOW_DIAGRAMS.md § Message Queue & Processing**
3. Code location: `WebSocketManager.ConnectAsync()` lines 70-350
4. Follow: Network thread → `_incomingMessages` queue → `GameManager.Update()` → `HandleRawMessage()`

### "I need to understand how animations play"
1. Read: **GAME_ARCHITECTURE_GUIDE.md § Animation Coordination**
2. See diagram: **GAME_FLOW_DIAGRAMS.md § Animation State Machine**
3. Code example: **CODE_REFERENCE_GUIDE.md § Play Matched Attack & Hit**
4. Code location: `AnimationController.cs` method `PlayBothAnimationsWithMatchedVariant()`

### "I need to understand when HP updates"
1. Read: **GAME_ARCHITECTURE_GUIDE.md § Event Handlers § DAMAGE_APPLIED**
2. See diagram: **GAME_FLOW_DIAGRAMS.md § Health Bar Update Flow**
3. Code example: **CODE_REFERENCE_GUIDE.md § Update Health Bar**
4. Code location: `GameManager.cs` case `"DAMAGE_APPLIED"` → calls `PlayerUI.UpdateHealthDisplay()`

### "I need to understand how reconnection works"
1. Read: **GAME_ARCHITECTURE_GUIDE.md § Reconnection & Resync**
2. See diagram: **GAME_FLOW_DIAGRAMS.md § Reconnection Flow**
3. Code example: **CODE_REFERENCE_GUIDE.md § Reconnection Handling**
4. Code location: `WebSocketManager.cs` handlers `OnDisconnected` and `OnConnected`

---

## Code Walkthrough by Component

### WebSocketManager.cs (Networking Layer)

**File Purpose:** Socket.IO client, message buffering, sequence tracking

**Key Methods:**

| Method | Line Range | Purpose | Calls |
|--------|-----------|---------|-------|
| `ConnectAsync()` | 70 | Establish connection, register event handlers | `_ws.ConnectAsync()` |
| `OnConnected` handler | 150 | Connection established, auto-subscribe if needed | `SubscribeToMatch()` |
| `OnDisconnected` handler | 210 | Connection lost, auto-reconnect if enabled | `ConnectAsync()` |
| `SubscribeToMatch()` | 410 | Re-subscribe after reconnect with sequence | `EmitLoggedAsync()` |
| `Update()` (in message handler) | 380 | Dequeue messages, invoke callbacks | `HandleRawMessage()` |

**Event Handlers Registered:**

```csharp
_ws.On("meme_battle_snapshot", response => {...});     // Line 200
_ws.On("meme_battle_event", response => {...});        // Line 320
_ws.On("meme_battle_start_result", response => {...}); // Line 380
_ws.On("meme_battle_battle_result", response => {...}); // Line 450
```

**Key State:**

```csharp
private long _lastAppliedSequence = 0;  // Track sequence for deduplication
private string _currentMatchId;          // Remember which match we're in
private ConcurrentQueue<string> _incomingMessages;  // Buffer events
```

**How to Read It:**
1. Start at `ConnectAsync()` to understand connection setup
2. Look at `OnConnected` to see what happens after connection
3. See `_ws.On("meme_battle_event", ...)` to understand event reception
4. Check `Update()` to see how messages get queued for GameManager

---

### GameManager.cs (Controller Layer)

**File Purpose:** Parse events, route to UI/animation systems, maintain state

**Key Methods:**

| Method | Line Range | Purpose | Calls |
|--------|-----------|---------|-------|
| `Start()` | 45 | Initialize, subscribe to WebSocket events | `WebSocketManager.OnRawMessageReceived` |
| `HandleRawMessage()` | 120 | Deserialize JSON, route by event type | `HandleMemeBattleEvent()` |
| `HandleMemeBattleEvent()` | 170 | Process game events (MATCH_CREATED, etc.) | Event-specific handlers |
| `UpdatePlayerHealthDisplay()` | 300 | Update both players' health bars | `PlayerUI.UpdateHealthDisplay()` |
| `SideFromCharacterId()` | 450 | Map "bot_a" → Side.Left, "bot_b" → Side.Right | (utility) |

**Event Cases (in `HandleMemeBattleEvent()`):**

```csharp
case "MATCH_CREATED":        // Line 180 - Initialize characters
case "TURN_STARTED":         // Line 200 - Start timer
case "ARGUMENT_SELECTED":    // Line 220 - Play animations, show meme
case "MULTIPLIER_SELECTED":  // Line 270 - Show bonus
case "DAMAGE_APPLIED":       // Line 290 - Update HP, show damage
case "HP_CHANGED":           // Line 320 - Authoritative HP
case "WINNER_DECLARED":      // Line 340 - Match ended
```

**Key State:**

```csharp
private Dictionary<string, long> _characterMaxHp;     // Max HP per char
private Dictionary<string, string> _characterNames;   // Names per char
private MemeBattleMatchSnapshot _latestSnapshot;      // Current state
private bool _matchEnded;                              // Match concluded?
```

**How to Read It:**
1. Start at `HandleRawMessage()` to understand event parsing
2. Look at each case in `HandleMemeBattleEvent()` for event-specific logic
3. See calls to `PlayerUI`, `RoundManager`, `AnimationController` to understand system integration
4. Check `SideFromCharacterId()` to understand character mapping

---

### AnimationController.cs (Animation Layer)

**File Purpose:** Play animator transitions (attack, hit, idle, victory)

**Key Methods:**

| Method | Line Range | Purpose | Calls |
|--------|-----------|---------|-------|
| `PlayAnimation()` | 120 | Play single animation on one side | `ResolveAnimationId()` → `animator.CrossFade()` |
| `PlayBothAnimationsWithMatchedVariant()` | 150 | Play attack + hit simultaneously | `ResolveAnimationId()` on both |
| `ResolveAnimationId()` | 180 | Convert "bot_a_attack_light" → "bot_a_attack_light_3" | (utility) |
| `SnapToCombatPositions()` | 80 | Position fighters at combat distance | (utility) |
| `CalculateAnchors()` | 60 | Calculate Left/Right anchor positions | (utility) |

**Key Properties:**

```csharp
public Animator leftAnimator;      // Left fighter
public Animator rightAnimator;     // Right fighter
public float defaultCrossFade = 0.12f;  // Transition duration
public int attackVariants = 5;     // How many attack variants (1-5)
public int hitVariants = 5;        // How many hit variants (1-5)
```

**How to Read It:**
1. Start at `PlayBothAnimationsWithMatchedVariant()` to understand matched animations
2. Look at `ResolveAnimationId()` to understand variant resolution
3. Check `SnapToCombatPositions()` to understand positioning logic

---

### PlayerUI.cs (UI Layer)

**File Purpose:** Display health bars, damage popups, dialogue, character names

**Key Methods:**

| Method | Purpose | Called By |
|--------|---------|-----------|
| `UpdateName()` | Set character name | GameManager (MATCH_CREATED) |
| `UpdateHealthDisplay()` | Update health bar + text | GameManager (DAMAGE_APPLIED, HP_CHANGED) |
| `UpdateHP()` | Update HP value only | GameManager |
| `ShowDamage()` | Display damage popup | GameManager (DAMAGE_APPLIED) |
| `SetDialogue()` | Show meme text | GameManager (ARGUMENT_SELECTED) |
| `ClearDialogue()` | Clear meme text | GameManager (TURN_STARTED) |
| `ShowVictory()` | Display victory screen | GameManager (WINNER_DECLARED) |

**Key Properties:**

```csharp
public Text leftNameText;
public Text rightNameText;
public Image leftHealthBar;
public Image rightHealthBar;
public Text leftHealthText;
public Text rightHealthText;
```

**How to Read It:**
1. Look at constructor to see UI references
2. Check `UpdateHealthDisplay()` to see health bar logic
3. See `ShowDamage()` for popup implementation

---

### RoundManager.cs (UI Layer)

**File Purpose:** Display timer, round number, multiplier, voting totals

**Key Methods:**

| Method | Purpose | Called By |
|--------|---------|-----------|
| `StartTurnTimer()` | Begin countdown from closesAt | GameManager (TURN_STARTED) |
| `Update()` | Update timer display every frame | Unity |
| `ShowMultiplier()` | Display bonus multiplier | GameManager (MULTIPLIER_SELECTED) |
| `ShowFinalTotals()` | Display voting results | GameManager (ARGUMENT_SELECTED) |
| `StopTimer()` | Stop countdown | GameManager (match end) |

**Key State:**

```csharp
private DateTime _turnClosesAtUtc;  // When turn ends
private bool _timerRunning;         // Timer active?
```

**How to Read It:**
1. Look at `Update()` to understand countdown logic
2. Check `StartTurnTimer()` for ISO-8601 date parsing
3. See `ShowMultiplier()` for multiplier display

---

## Data Flow Walkthrough: Complete Example

### Scenario: ARGUMENT_SELECTED Event Arrives

**Step 1: Network Thread (WebSocketManager.cs)**

```csharp
_ws.On("meme_battle_event", response =>  // Line ~320
{
    var rawText = response.ToString();
    // Parse JSON, check sequence
    if (sequence > _lastAppliedSequence)
    {
        _incomingMessages.Enqueue(rawText);  // Buffer for main thread
        _lastAppliedSequence = sequence;
    }
});
```

**Step 2: Main Thread (GameManager.Update())**

```csharp
void Update()
{
    while (_incomingMessages.TryDequeue(out string msg))  // Dequeue from buffer
    {
        HandleRawMessage(msg);  // Process
    }
}
```

**Step 3: Parse (GameManager.HandleRawMessage())**

```csharp
var j = JObject.Parse(json);  // Deserialize
var type = j.Value<string>("type");  // Get "meme_battle_event"

if (type == "meme_battle_event")
{
    var ev = j["event"].ToObject<MemeBattleEvent>();
    HandleMemeBattleEvent(ev);  // Route to handler
}
```

**Step 4: Route Event (GameManager.HandleMemeBattleEvent())**

```csharp
case "ARGUMENT_SELECTED":  // Line ~220
{
    var actorCharacterId = payload.Value<string>("actorCharacterId");  // "bot_a"
    var targetCharacterId = payload.Value<string>("targetCharacterId");  // "bot_b"
    var memeText = payload.Value<string>("memeText");  // "Skill issue..."
    var animationId = payload.Value<string>("animationId");  // "bot_a_attack_light"

    // Map to UI sides
    var attackerSide = SideFromCharacterId(actorCharacterId);  // Side.Left
    var targetSide = SideFromCharacterId(targetCharacterId);  // Side.Right

    // Trigger animation
    animationController?.PlayBothAnimationsWithMatchedVariant(
        attackerSide, animationId,
        targetSide, animationId.Replace("attack", "hit")
    );

    // Show meme text
    playerUI?.SetDialogue(attackerSide.Value, memeText);

    // Show voting totals
    var totals = payload["finalTotalsAtomic"].ToObject<Dictionary<string, long>>();
    roundManager?.ShowFinalTotals(totals);
}
```

**Step 5: Animate (AnimationController.PlayBothAnimationsWithMatchedVariant())**

```csharp
public void PlayBothAnimationsWithMatchedVariant(
    PlayerUI.Side side1, string animId1,
    PlayerUI.Side side2, string animId2)
{
    // Resolve IDs with random variant
    string state1 = ResolveAnimationId(animId1);  // "bot_a_attack_light_3"
    string state2 = ResolveAnimationId(animId2);  // "bot_a_hit_light_3"

    // Get animators
    var anim1 = side1 == PlayerUI.Side.Left ? leftAnimator : rightAnimator;
    var anim2 = side2 == PlayerUI.Side.Left ? leftAnimator : rightAnimator;

    // Crossfade both
    anim1.CrossFade(state1, 0.12f);
    anim2.CrossFade(state2, 0.12f);
}
```

**Step 6: Display UI (PlayerUI.SetDialogue())**

```csharp
public void SetDialogue(PlayerUI.Side side, string text)
{
    var textComponent = side == PlayerUI.Side.Left ? leftDialogueText : rightDialogueText;
    if (textComponent != null)
    {
        textComponent.text = text;  // Show "Skill issue..."
    }
}
```

**Result:** Attack/hit animations play in parallel, meme text displays, voting totals show!

---

## Understanding Key Patterns

### Pattern 1: ConcurrentQueue for Thread Safety

**Why?** Network callbacks happen on background threads

**Where:**
- `WebSocketManager._incomingMessages` (line ~40)
- `WebSocketManager._progressQueue` (line ~50)
- `WebSocketManager._errorQueue` (line ~50)

**How:**
```csharp
// Network thread (safe)
_incomingMessages.Enqueue(message);

// Main thread (safe)
while (_incomingMessages.TryDequeue(out string msg))
{
    HandleMessage(msg);
}
```

### Pattern 2: Sequence Number Deduplication

**Why?** Prevent duplicate event processing on reconnect

**Where:** `WebSocketManager._lastAppliedSequence` (line ~30)

**How:**
```csharp
if (sequence > _lastAppliedSequence)
{
    ProcessEvent();
    _lastAppliedSequence = sequence;
}
else
{
    SkipEvent();  // Already processed
}
```

### Pattern 3: Event Routing Switch

**Why?** Handle different event types differently

**Where:** `GameManager.HandleMemeBattleEvent()` (line ~170)

**How:**
```csharp
switch (eventType)
{
    case "MATCH_CREATED": HandleMatchCreated(); break;
    case "ARGUMENT_SELECTED": HandleArgumentSelected(); break;
    // ...
}
```

### Pattern 4: Character-to-Side Mapping

**Why?** Convert backend character IDs to UI sides

**Where:** `GameManager.SideFromCharacterId()` (line ~450)

**How:**
```csharp
private PlayerUI.Side? SideFromCharacterId(string id)
{
    return id == "bot_a" ? PlayerUI.Side.Left :
           id == "bot_b" ? PlayerUI.Side.Right : null;
}
```

---

## Finding Things in the Code

### "Where is health updated?"
```
GameManager.HandleMemeBattleEvent()
  → case "DAMAGE_APPLIED":
      → playerUI?.UpdateHP()
      → playerUI?.ShowDamage()
      → playerUI?.UpdateHealthDisplay()
```

### "Where is animation played?"
```
GameManager.HandleMemeBattleEvent()
  → case "ARGUMENT_SELECTED":
      → animationController?.PlayBothAnimationsWithMatchedVariant()
        → ResolveAnimationId() for each animator
        → animator.CrossFade(state, 0.12f)
```

### "Where is timer started?"
```
GameManager.HandleMemeBattleEvent()
  → case "TURN_STARTED":
      → roundManager?.StartTurnTimer()
        → _turnClosesAtUtc = parsed UTC time
        → _timerRunning = true
```

### "Where is connection handled?"
```
WebSocketManager.ConnectAsync()
  → Creates SocketIO instance
  → Registers event handlers
  → await _ws.ConnectAsync()

WebSocketManager._ws.OnConnected
  → SubscribeToMatch() if needed
```

### "Where are events queued?"
```
WebSocketManager._ws.On("meme_battle_event")
  → Check sequence
  → Enqueue to _incomingMessages

GameManager.Update()
  → Dequeue from _incomingMessages
  → HandleRawMessage()
```

---

## Debugging Checklist

### Event Not Arriving?

```
1. Check WebSocketManager.cs:
   - Is _ws.Connected true? (line 45)
   - Are event handlers registered? (line 200+)
   - Check verbose logging (Inspector)

2. Check network logs:
   - Browser DevTools Network tab (WebSocket frame)
   - Search for "meme_battle_event"

3. Check sequence:
   - Print _lastAppliedSequence
   - Verify sequence > last applied
```

### Animation Not Playing?

```
1. Check AnimationController.cs:
   - Are animators assigned? (line 20-22)
   - Does ResolveAnimationId() find state? (line 180)
   - Check animator has state "bot_a_attack_light_3"

2. Check GameManager.cs:
   - Is PlayAnimation() called? (Add debug log)
   - Is attackerSide/targetSide correct?

3. Check Animator Controller:
   - Does it have states for all variants?
   - Are transitions set up?
```

### Health Not Updating?

```
1. Check GameManager.cs:
   - Is DAMAGE_APPLIED event received? (Add debug log)
   - Is UpdateHealthDisplay() called? (line 300+)
   - Check PlayerUI is assigned (Inspector)

2. Check PlayerUI.cs:
   - Are health bar components assigned? (Inspector)
   - Does UpdateHealthDisplay() update all components?
```

---

## Architecture Validation

To verify your understanding, answer these questions:

### Understanding Flow
- [ ] Can you trace how an event goes from backend to UI?
- [ ] Can you explain what `_lastAppliedSequence` does?
- [ ] Can you describe why `ConcurrentQueue` is used?

### Understanding Components
- [ ] What does WebSocketManager do? (just networking?)
- [ ] What does GameManager do? (just routing?)
- [ ] What does AnimationController do? (just playing anims?)

### Understanding Events
- [ ] What events arrive during a match?
- [ ] Which event updates health? (DAMAGE_APPLIED or HP_CHANGED?)
- [ ] Which event plays animations? (ARGUMENT_SELECTED?)

### Understanding State
- [ ] Where is character max HP stored?
- [ ] Where is current HP stored?
- [ ] Where is the last processed sequence stored?

**If you can answer all these, you understand the architecture! 🎉**

---

## Next Steps

1. **Run the game** with `verboseLogging = true` to see event flow
2. **Trace one full turn** from event arrival to animation completion
3. **Add breakpoints** at key methods (HandleMemeBattleEvent, PlayAnimation, etc.)
4. **Modify one handler** (e.g., change damage display color) to gain confidence
5. **Test reconnection** by stopping/starting server to see auto-reconnect
6. **Extend functionality** (e.g., add new event type) using existing patterns

---

## Reference Card (Copy to Bookmark)

```
NAVIGATION SHORTCUTS:

WebSocketManager.cs:
  - Connection: ConnectAsync() @ line 70
  - Events: _ws.On() @ line 200+
  - Queuing: Update() @ line ~380

GameManager.cs:
  - Main router: HandleRawMessage() @ line 120
  - Event handlers: HandleMemeBattleEvent() @ line 170
  - Mapping: SideFromCharacterId() @ line 450

AnimationController.cs:
  - Play anim: PlayBothAnimationsWithMatchedVariant() @ line 150
  - Resolve ID: ResolveAnimationId() @ line 180

PlayerUI.cs:
  - Health: UpdateHealthDisplay()
  - Damage: ShowDamage()
  - Dialogue: SetDialogue()

RoundManager.cs:
  - Timer: StartTurnTimer()
  - Update loop: Update() @ every frame
```

