# Meme Battle — Architecture Quick Reference

## System Overview

```
┌────────────────────────────────────────────────────────────────┐
│                      GAME FLOW SUMMARY                         │
└────────────────────────────────────────────────────────────────┘

1. INIT
   • WebSocketManager connects to backend (Socket.IO)
   • GameManager subscribes to OnRawMessageReceived
   • UI components register

2. START
   • Emit "meme_battle_start" (requestId: "...")
   • ← Receive "meme_battle_start_result" (matchId, HP)
   • GameManager updates characters, HP, timer

3. TURN LOOP (per turn)
   • ← TURN_STARTED (opensAt, closesAt)
   • RoundManager starts countdown timer
   • ← ARGUMENT_SELECTED (action, meme text, animation)
   • AnimationController plays attack + hit animations
   • PlayerUI shows meme text on attacker
   • RoundManager shows voting totals
   • ← MULTIPLIER_SELECTED (bonus multiplier)
   • RoundManager shows "1.5x"
   • ← DAMAGE_APPLIED (damage amount, new HP)
   • PlayerUI shows damage popup
   • PlayerUI updates health bar
   • ← HP_CHANGED (authoritative HP update)

4. REPEAT
   • Loop to step 3 until match ends

5. END
   • ← WINNER_DECLARED (winner character ID)
   • GameManager marks match as ended
   • PlayerUI shows victory/draw screen
   • AnimationController plays victory animation

6. RECONNECT (if connection lost)
   • WebSocketManager auto-reconnects after delay
   • Emit "meme_battle_subscribe" (matchId, afterSequence)
   • ← "meme_battle_snapshot" (current state + new events)
   • GameManager replays new events
   • Back in sync
```

---

## Component Responsibilities

| Component | Responsibility | Key Methods |
|-----------|-----------------|-------------|
| **WebSocketManager** | Network I/O, event buffering, sequence tracking | `ConnectAsync()`, `SubscribeToMatch()`, `SendMessageToServer()` |
| **GameManager** | Event routing, state management | `HandleRawMessage()`, `HandleMemeBattleEvent()`, `ApplyRawMessage()` |
| **PlayerUI** | Health bars, damage display, dialogue, name display | `UpdateHealthDisplay()`, `ShowDamage()`, `SetDialogue()` |
| **RoundManager** | Timer display, round number, multiplier, voting totals | `StartTurnTimer()`, `ShowMultiplier()`, `ShowFinalTotals()` |
| **AnimationController** | Animation state transitions | `PlayAnimation()`, `PlayBothAnimationsWithMatchedVariant()` |

---

## Message Types

### Incoming Messages

| Type | Purpose | Typical Sequence |
|------|---------|------------------|
| `meme_battle_start_result` | Match initialization | ~1 |
| `meme_battle_snapshot` | Current match state (on reconnect) | variable |
| `meme_battle_event` | Game event (TURN_STARTED, ARGUMENT_SELECTED, etc.) | 2+ |
| `error` | Error message | anytime |

### Outgoing Messages

| Event Name | Payload | Purpose |
|------------|---------|---------|
| `meme_battle_start` | `{ requestId }` | Start new match |
| `meme_battle_subscribe` | `{ matchId, afterSequence }` | Subscribe to match (on reconnect) |

---

## Event Types (Sequential Order)

```
1. MATCH_CREATED
   └─ Characters initialized, HP set

2. MATCH_STARTED
   └─ Match begins

3-N. (Repeated per turn):
   ├─ TURN_STARTED
   │  └─ Countdown timer starts
   ├─ ARGUMENT_SELECTED
   │  └─ Play attack/hit animations, show meme text
   ├─ MULTIPLIER_SELECTED
   │  └─ Display bonus multiplier
   ├─ DAMAGE_APPLIED
   │  └─ Show damage popup, update HP
   └─ HP_CHANGED
      └─ Authoritative HP update

N+1. WINNER_DECLARED
    └─ Match ends, show victory/draw
```

---

## State Flow Diagram

```
Idle (No Connection)
  │
  ├─ User initiates match
  │
  ↓
Connecting
  ├─ Socket.IO handshake
  │
  ├─ OnConnected fires
  ↓
Connected (Awaiting Match)
  │
  ├─ Emit "meme_battle_start"
  │
  ├─ Receive "meme_battle_start_result"
  ├─ Initialize characters & HP
  ↓
In Match (Turn Loop)
  │
  ├─ Receive TURN_STARTED
  ├─ Start timer
  │
  ├─ Receive ARGUMENT_SELECTED
  ├─ Play animations
  ├─ Show UI updates
  │
  ├─ Receive DAMAGE_APPLIED
  ├─ Update HP
  │
  ├─ (Loop until match ends)
  ↓
Match Finished
  │
  ├─ Receive WINNER_DECLARED
  ├─ Show victory screen
  ├─ Mark _matchEnded = true
  ↓
Idle (Awaiting Next Match)


(If connection lost during match)
  │
  ├─ OnDisconnected fires
  ├─ Wait _reconnectDelay
  │
  ├─ ConnectAsync() again
  │
  ├─ OnConnected fires
  ├─ Emit "meme_battle_subscribe" (afterSequence = last event)
  │
  ├─ Receive "meme_battle_snapshot"
  ├─ Apply snapshot + replay new events
  ├─ Back in sync
  │
  └─ Continue turn loop
```

---

## Sequence Number Tracking

**Purpose:** Exactly-once event delivery, deduplication on reconnect

**Logic:**
```
Initial: _lastAppliedSequence = 0

Event arrives: sequence = 5
  ✓ 5 > 0 → Process
    _lastAppliedSequence = 5

Duplicate event: sequence = 3
  ✗ 3 > 5 → Skip (already processed)

Next event: sequence = 6
  ✓ 6 > 5 → Process
    _lastAppliedSequence = 6

[Connection Lost]
[Reconnect: emit afterSequence = 6]
[Backend: only send events > 6]

Next event: sequence = 7
  ✓ 7 > 6 → Process
    _lastAppliedSequence = 7
```

---

## Character Mapping

```
Backend → GameManager → UI/Animation

"bot_a"  →  PlayerUI.Side.Left  →  Left animator + left health bar
"bot_b"  →  PlayerUI.Side.Right →  Right animator + right health bar

Method: GameManager.SideFromCharacterId()
```

---

## Animation Naming Convention

```
Format: {character}_{action}_{variant}

Examples:
  bot_a_attack_light_1     ← Attacker plays light attack variant 1
  bot_b_hit_light_1        ← Target plays light hit variant 1 (matched)

Flow:
  Backend sends: animationId = "bot_a_attack_light"
       ↓
  Attacker plays: "bot_a_attack_light_3" (random variant 1-5)
  Target plays: "bot_a_hit_light_3" (variant matches attacker)
       ↓
  Both crossfade in parallel, both return to Idle
```

---

## Timer Countdown Logic

```
Event: TURN_STARTED { opensAt: "...", closesAt: "2026-09-06T10:00:05Z" }

RoundManager.Update() every frame:
  remaining = parsedClosesAtUtc - DateTime.UtcNow

  Display: "MM:SS"
  Countdown: 00:05 → 00:04 → 00:03 → ... → 00:00

  When remaining <= 0:
    _timerRunning = false
    Display: "00:00"
```

---

## Health Update Pipeline

```
Backend calculates damage + new HP

         ↓

Event: DAMAGE_APPLIED
  {
    targetCharacterId: "bot_b",
    damageAtomic: 150,
    hpAfterAtomic: 850,
    hpBeforeAtomic: 1000
  }

         ↓

GameManager extracts data
         ↓
  Map "bot_b" → Side.Right
  currentHp = 850
  maxHp = 1000

         ↓

PlayerUI.UpdateHealthDisplay(Right, 850, 1000)
  ├─ Health bar fill %:    (850 / 1000) * 100 = 85%
  ├─ Health text:          "850 / 1000"
  └─ Bar color:
      Green   (>50%)
      Yellow  (25-50%)
      Red     (<25%)

         ↓

PlayerUI.ShowDamage(Right, 150)
  ├─ "+150" popup
  ├─ Float upward
  └─ Fade out
```

---

## Error Handling Flow

```
Backend error
      ↓
MapErrorToCode():
  "valid socket.io credentials" → SOCKET_AUTH_REQUIRED
  "not authenticated" → UNITY_AUTH_REQUIRED
  "principal is invalid" → UNITY_AUTH_INVALID
  "match not found" → MATCH_NOT_FOUND
  (else) → UNKNOWN_ERROR
      ↓
OnConnectionError?.Invoke(code, message)
      ↓
LoadingManager.HandleExternalError(code, message)
      ↓
Display user-friendly message
```

---

## Reconnection Flow

```
Connection Lost
      ↓
WebSocketManager.OnDisconnected
      ↓
_ws = null
      ↓
if (_autoReconnect && !_isClosing):
  Wait _reconnectDelay (default 3s)
      ↓
  ConnectAsync()
      ↓
  OnConnected fires
      ↓
  if (_currentMatchId is set):
    SubscribeToMatch(_currentMatchId, _lastAppliedSequence)
      ↓
    Emit "meme_battle_subscribe"
      ↓
    ← Receive "meme_battle_snapshot"
    ├─ snapshot: current state
    └─ events: new events (sequence > afterSequence)
      ↓
  GameManager replays events
      ↓
Back in sync!
```

---

## Data Persistence Rules

| State | Where Stored | Lifetime | Updated By |
|-------|--------------|----------|-----------|
| Match HP | GameManager._characterMaxHp | Match duration | MATCH_CREATED |
| Current HP | PlayerUI (display) | Match duration | DAMAGE_APPLIED, HP_CHANGED |
| Character Names | GameManager._characterNames | Match duration | MATCH_CREATED |
| Last Sequence | WebSocketManager._lastAppliedSequence | Until disconnect | Each event |
| Latest Snapshot | GameManager._latestSnapshot | Match duration | start_result, snapshot events |
| Timer Deadline | RoundManager._turnClosesAtUtc | Per turn | TURN_STARTED |

---

## Configuration Checklist

### Development
- [ ] Server URL: `http://localhost:9092`
- [ ] Service Token: `test-unity-secret`
- [ ] Verbose Logging: `true`
- [ ] Auto Reconnect: `true`
- [ ] Local Input Testing: `true` (Q/E debug keys)

### Production
- [ ] Server URL: `https://api.lockness.xyz`
- [ ] Service Token: (actual production secret)
- [ ] Verbose Logging: `false`
- [ ] Auto Reconnect: `true`
- [ ] Local Input Testing: `false`

---

## Common Integration Points

```
LoadingManager
  ├─ Shows loading screen
  ├─ Waits for WebSocketManager connection
  └─ Calls StartMemeMatch() → emits "meme_battle_start"
         ↓
       (Scene loads BattleScene)
         ↓
GameManager
  ├─ Listens for OnRawMessageReceived
  └─ Routes events to PlayerUI, RoundManager, AnimationController
         ↓
PlayerUI + RoundManager + AnimationController
  ├─ Update health bars, timer, animations
  └─ Display to user
         ↓
User sees battle play out
```

---

## Event Handler Pattern

```csharp
void HandleMemeBattleEvent(MemeBattleEvent ev)
{
    switch (ev.eventType)
    {
        case "MATCH_CREATED":
            // Extract payload fields
            // Update GameManager state
            // Update UI (names, HP)
            break;

        case "ARGUMENT_SELECTED":
            // Extract actor, target, animation, meme text
            // Map characters to sides
            // Play animations
            // Show dialogue
            // Show voting totals
            break;

        case "DAMAGE_APPLIED":
            // Extract target, damage, new HP
            // Update health display
            // Show damage popup
            break;

        case "WINNER_DECLARED":
            // Extract winner character ID
            // Mark match ended
            // Show victory screen
            // Play victory animation
            break;
    }
}
```

---

## Key Architectural Principles

1. **Backend is Authority**
   - Never calculate damage locally
   - Always trust HP values from backend
   - Backend drives all game logic

2. **Thread-Safe Queuing**
   - Network callbacks → ConcurrentQueue
   - Main thread dequeues safely
   - Prevents race conditions

3. **Exactly-Once Semantics**
   - Sequence numbers prevent duplicates
   - Resilient to reconnections
   - No repeated event processing

4. **Separation of Concerns**
   - WebSocketManager: networking only
   - GameManager: event routing, state
   - UI/Animation: presentation only

5. **Graceful Reconnection**
   - Auto-reconnect with exponential backoff
   - Re-subscribe to match with afterSequence
   - Replay missed events to catch up

6. **Animation Coordination**
   - Attack and hit play simultaneously
   - Same variant number for both
   - Automatic return to Idle

---

## Debugging Commands

```csharp
// Check connection status
WebSocketManager.Instance.IsConnected();

// Check last processed event
Debug.Log(WebSocketManager.Instance._lastAppliedSequence);

// Check current match ID
Debug.Log(GameManager.Instance._currentMatchId);

// Check current HP
Debug.Log($"Bot A: {current / max} HP");

// Simulate event (testing)
string json = JsonConvert.SerializeObject(...);
GameManager.Instance.ApplyRawMessage(json);
```

---

## Performance Tips

1. **Disable Verbose Logging** in production
2. **Limit Queue Size** if processing slow
3. **Use Crossfade** not direct state changes
4. **Cache Character Info** to avoid repeated lookups
5. **Clean Up** event handlers in OnDestroy

---

## Summary

**Meme Battle is a client-side renderer for a server-driven game.**

- Backend simulates match, calculates damage, decides winner
- Unity receives real-time events via Socket.IO
- GameManager routes events to UI/Animation systems
- UI components display health, timer, damage, dialogue
- AnimationController plays character animations
- On disconnect, auto-reconnect and replay missed events
- Sequence numbers ensure exactly-once processing

**Architecture focuses on:**
- Responsiveness (immediate event processing)
- Reliability (reconnect, deduplication)
- Maintainability (clear separation of concerns)
- Extensibility (easy to add new event types)

