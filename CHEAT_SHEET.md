# 🚀 Meme Battle — Developer Cheat Sheet

## One-Page Reference (Print This!)

### Architecture Layers

```
┌─────────────────────────────────────────┐
│ UI: PlayerUI, RoundManager              │  Show health, damage, timer, votes
├─────────────────────────────────────────┤
│ Animation: AnimationController          │  Play animations
├─────────────────────────────────────────┤
│ Controller: GameManager                 │  Route events, manage state
├─────────────────────────────────────────┤
│ Network: WebSocketManager               │  Socket.IO, buffer, sequences
├─────────────────────────────────────────┤
│ Backend: Simulator                      │  Damage, winner, events
└─────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Does | Key Method |
|-----------|------|-----------|
| **WebSocketManager** | Network I/O, buffering, sequences | `ConnectAsync()` |
| **GameManager** | Event routing, state | `HandleMemeBattleEvent()` |
| **AnimationController** | Play animations | `PlayBothAnimationsWithMatchedVariant()` |
| **PlayerUI** | Health, damage, dialogue | `UpdateHealthDisplay()` |
| **RoundManager** | Timer, multiplier, votes | `StartTurnTimer()` |

### Event Sequence

```
1. MATCH_CREATED (characters, HP)
2. MATCH_STARTED (game begins)
3. TURN_STARTED (countdown timer)
4. ARGUMENT_SELECTED (attack/hit anim, meme text)
5. MULTIPLIER_SELECTED (bonus display)
6. DAMAGE_APPLIED (damage popup, HP update)
7. HP_CHANGED (authoritative HP)
8. [repeat 3-7 per turn]
9. WINNER_DECLARED (victory screen)
```

### Message Queue Flow

```
Backend Event
    ↓
WebSocketManager.On() [network thread]
    ↓
Check sequence > _lastAppliedSequence
    ↓
Enqueue to _incomingMessages (ConcurrentQueue)
    ↓
GameManager.Update() [main thread]
    ↓
Dequeue & HandleRawMessage()
    ↓
HandleMemeBattleEvent() → Event handler
    ↓
Update UI + Play animations
```

### Animation Playing

```
Backend sends: animationId = "bot_a_attack_light"
    ↓
Attacker (Left):   "bot_a_attack_light_3" (random variant 1-5)
Target (Right):    "bot_a_hit_light_3"    (variant matches)
    ↓
Both play simultaneously
    ↓
Both return to Idle automatically
```

### State Storage

| State | Stored In | Lifetime |
|-------|-----------|----------|
| Connection status | WebSocketManager._ws | Until disconnect |
| Last sequence | WebSocketManager._lastAppliedSequence | Until disconnect |
| Current match | GameManager._currentMatchId | While in match |
| Character max HP | GameManager._characterMaxHp | Match duration |
| Character names | GameManager._characterNames | Match duration |
| Current HP | PlayerUI (display) | Match duration |
| Timer deadline | RoundManager._turnClosesAtUtc | Per turn |

### Key Principles

1. **Backend is Authority** - Never calculate HP/damage locally
2. **Thread-Safe Queuing** - Use ConcurrentQueue for thread boundaries
3. **Exactly-Once Semantics** - Sequence numbers prevent duplicates
4. **Graceful Reconnection** - Auto-reconnect, re-subscribe, catch up
5. **Separation of Concerns** - Each layer has one responsibility

### Character Mapping

```
Backend → GameManager → UI

"bot_a" → PlayerUI.Side.Left  → Left animator + left health bar
"bot_b" → PlayerUI.Side.Right → Right animator + right health bar
```

### Reconnection Flow

```
Connection Lost
    ↓
Wait 3 seconds (_reconnectDelay)
    ↓
ConnectAsync() again
    ↓
OnConnected → SubscribeToMatch(matchId, lastSequence)
    ↓
Receive snapshot + new events
    ↓
Back in sync!
```

### Health Update Flow

```
DAMAGE_APPLIED event
    ↓
Extract: damageAtomic, hpAfterAtomic, targetCharacterId
    ↓
GameManager maps "bot_b" → Side.Right
    ↓
PlayerUI.UpdateHealthDisplay(Right, hpAfter, maxHp)
    ↓
Health bar updated:
  ├─ Fill % = hpAfter / maxHp
  ├─ Text = "hpAfter / maxHp"
  └─ Color = Green/Yellow/Red based on %
    ↓
PlayerUI.ShowDamage(Right, damageAmount)
    ↓
"+150" popup floats up and fades
```

### Error Codes

```
"Service credentials..." → SOCKET_AUTH_REQUIRED
"Not authenticated" → UNITY_AUTH_REQUIRED
"Invalid principal" → UNITY_AUTH_INVALID
"Not allowed" → UNITY_EVENT_FORBIDDEN
"Match not found" → MATCH_NOT_FOUND
"Invalid parameters" → MATCH_SUBSCRIBE_INVALID
(else) → UNKNOWN_ERROR
```

### Timer Display

```
Event: TURN_STARTED { closesAt: "2026-09-06T10:00:05Z" }
    ↓
RoundManager.Update() every frame:
    remaining = closesAt - DateTime.UtcNow
    Display: "MM:SS"
    ↓
00:05 → 00:04 → 00:03 → ... → 00:00
```

### Configuration

**Development:**
```
Server: http://localhost:9092
Token: test-unity-secret
Logging: true
```

**Production:**
```
Server: https://api.lockness.xyz
Token: (actual secret)
Logging: false
```

### Quick Debugging

```
// Check connection
WebSocketManager.Instance.IsConnected()

// Check last event
WebSocketManager.Instance._lastAppliedSequence

// Check current match
GameManager.Instance._currentMatchId

// Enable verbose logging
WebSocketManager.Instance.verboseLogging = true

// Simulate event
string json = JsonConvert.SerializeObject({...});
GameManager.Instance.ApplyRawMessage(json);

// Debug key input (if enabled)
Press Q → Left attacks (random preset)
Press E → Right attacks (random preset)
```

### Common Issues

| Issue | Check |
|-------|-------|
| Events not arriving | Connection status, token, URL |
| Animation not playing | Animator assigned, state exists, OnAnimation called |
| Health not updating | Event received, UpdateHealthDisplay called |
| Timer not counting | TURN_STARTED received, time parsing works |
| Reconnection loops | Auto-reconnect setting, token validity |

### JSON Event Examples

```json
{
  "eventType": "ARGUMENT_SELECTED",
  "payload": {
    "actorCharacterId": "bot_a",
    "targetCharacterId": "bot_b",
    "memeText": "Skill issue.",
    "animationId": "bot_a_attack_light",
    "finalTotalsAtomic": {"turn_1_A": 0, "turn_1_B": 101}
  }
}

{
  "eventType": "DAMAGE_APPLIED",
  "payload": {
    "targetCharacterId": "bot_b",
    "damageAtomic": 150,
    "hpAfterAtomic": 850
  }
}

{
  "eventType": "WINNER_DECLARED",
  "payload": {
    "winnerCharacterId": "bot_a"
  }
}
```

### File Locations

```
WebSocketManager.cs
  ├─ ConnectAsync() @ line 70
  └─ Event handlers @ line 200+

GameManager.cs
  ├─ HandleRawMessage() @ line 120
  └─ HandleMemeBattleEvent() @ line 170

AnimationController.cs
  ├─ PlayAnimation() @ line 120
  └─ PlayBothAnimationsWithMatchedVariant() @ line 150

PlayerUI.cs
  ├─ UpdateHealthDisplay()
  └─ ShowDamage()

RoundManager.cs
  ├─ StartTurnTimer()
  └─ Update() [timer display]
```

### Useful Commands

```csharp
// Check if connected
if (!WebSocketManager.Instance.IsConnected())
    Debug.Log("Not connected!");

// Subscribe to match
await WebSocketManager.Instance.SubscribeToMatch("match_id", 0);

// Update health
playerUI?.UpdateHealthDisplay(PlayerUI.Side.Left, 850, 1000);

// Play animation
animationController?.PlayAnimation(PlayerUI.Side.Left, "bot_a_attack_light");

// Show damage
playerUI?.ShowDamage(PlayerUI.Side.Right, 150);

// Start timer
roundManager?.StartTurnTimer(1, "2026-09-06T10:00:00Z", "2026-09-06T10:00:05Z");

// Show multiplier
roundManager?.ShowMultiplier(1.5f);
```

### Key Methods to Know

| Method | Class | Purpose |
|--------|-------|---------|
| `ConnectAsync()` | WebSocketManager | Connect to backend |
| `SubscribeToMatch()` | WebSocketManager | Join match after reconnect |
| `HandleMemeBattleEvent()` | GameManager | Process game events |
| `PlayBothAnimationsWithMatchedVariant()` | AnimationController | Play attack + hit |
| `UpdateHealthDisplay()` | PlayerUI | Update health bar |
| `StartTurnTimer()` | RoundManager | Begin countdown |

### Event Handling Template

```csharp
case "CUSTOM_EVENT":
{
    // Extract payload
    var field1 = payload.Value<string>("field1");
    var field2 = payload.Value<long?>("field2") ?? 0;

    // Validate
    if (string.IsNullOrEmpty(field1)) break;

    // Log
    Debug.Log($"CUSTOM_EVENT field1={field1} field2={field2}");

    // Update state
    // ...

    // Update UI
    playerUI?.UpdateName(...);
    roundManager?.StartTurnTimer(...);
    animationController?.PlayAnimation(...);
}
break;
```

### Bookmark These URLs/Sections

When using the docs:
1. **README.md** - Your navigation hub
2. **QUICK_REFERENCE.md** - For quick lookups
3. **CODE_REFERENCE_GUIDE.md** - For code examples
4. **UNDERSTANDING_THE_CODE.md** - When reading source
5. **GAME_FLOW_DIAGRAMS.md** - When you need visuals

### Testing Locally

```csharp
// In Inspector, enable:
WebSocketManager.enableLocalInputTesting = true
GameManager.verboseLogging = true

// Press:
Q → Left attacks
E → Right attacks

// Watch console for:
"INCOMING" messages
"OUTGOING" messages
Animation state names
```

---

## Quick Start (5 Minutes)

1. Read this cheat sheet ✓
2. Open README.md
3. Choose your learning path
4. Read relevant docs
5. Start coding!

---

**Print this page and keep it on your desk! 📌**

