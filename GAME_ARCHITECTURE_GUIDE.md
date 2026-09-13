# Meme Battle — Unity Game Architecture & Flow Guide

## 📋 Overview

This document explains how the **Meme Battle** game client works in Unity, including the network flow, event processing, and UI/animation coordination.

**Key Principle:** Backend (BE) is the source of truth. Unity acts as a **state renderer**, receiving events and displaying them. Unity **never calculates** damage, HP, or winners—BE does all that.

---

## 🏗️ Architecture Layers

```
┌─────────────────────────────────────────────────────────────────┐
│                         UI Layer                                 │
│  PlayerUI (Health bars, Dialogues)  RoundManager (Timer, Totals) │
└─────────────────────────────────────────────────────────────────┘
                                 ↑
┌─────────────────────────────────────────────────────────────────┐
│                    Controller Layer (Brain)                       │
│          GameManager.cs (Event Router & State Manager)           │
└─────────────────────────────────────────────────────────────────┘
                                 ↑
┌─────────────────────────────────────────────────────────────────┐
│                   Animation Layer                                 │
│        AnimationController.cs (Play Animator Transitions)        │
└─────────────────────────────────────────────────────────────────┘
                                 ↑
┌─────────────────────────────────────────────────────────────────┐
│                  Network Layer (WebSocket)                        │
│   WebSocketManager.cs (Socket.IO Client, Message Queue)         │
└─────────────────────────────────────────────────────────────────┘
                                 ↑
┌─────────────────────────────────────────────────────────────────┐
│                      Backend Server                              │
│  Match Simulator, Damage Calculator, Event Producer             │
└─────────────────────────────────────────────────────────────────┘
```

### Layer Responsibilities

| Layer | Class | Role |
|-------|-------|------|
| **Network** | `WebSocketManager` | Socket.IO connection, message buffering, sequence tracking |
| **Controller** | `GameManager` | Parses events, routes to sub-systems, maintains match state |
| **Animation** | `AnimationController` | Plays Animator state transitions (Idle, Attack, Hit, etc.) |
| **UI** | `PlayerUI`, `RoundManager` | Displays health, timer, multiplier, dialogue, voting totals |

---

## 🔌 Network Flow

### Phase 1: Connection & Authentication

```
Unity Client
    ↓
Connect to: ws://api.lockness.xyz/socket.io/
    ↓
Send Auth:
{
  "clientType": "UNITY_RENDERER",
  "serviceToken": "test-unity-secret"
}
    ↓
← OnConnected event fires
```

**Code Location:** `WebSocketManager.ConnectAsync()`

**Key Points:**
- Socket.IO protocol (EIO v4)
- WebSocket transport
- Service token is a shared secret (do not commit to source control)

---

### Phase 2: Start Match

```
Unity emits "meme_battle_start":
{
  "requestId": "unity-start-20260906-0001"
}
    ↓ (Backend processes, creates match, selects characters)
    ↓
← Backend emits "meme_battle_start_result":
{
  "requestId": "unity-start-20260906-0001",
  "matchId": "match_abc123",
  "state": "ACTIVE",
  "characterIds": ["bot_a", "bot_b"],
  "snapshot": {
    "characterHpAtomic": {"bot_a": 1000, "bot_b": 1000},
    "characterMaxHpAtomic": {"bot_a": 1000, "bot_b": 1000},
    "currentTurnId": "turn_001",
    "latestSequence": 3,
    "winnerCharacterId": null
  }
}
```

**Code Location:** `WebSocketManager._ws.On("meme_battle_start_result")`

**Handler:** `GameManager.HandleRawMessage()` case `"meme_battle_start_result"`

**Actions:**
1. Update character names: `PlayerUI.UpdateName()`
2. Store max HP for health calculations
3. Start turn timer: `RoundManager.StartTurnTimer()`
4. Display initial health bars: `PlayerUI.UpdateHealthDisplay()`

---

### Phase 3: Realtime Event Stream

Backend sends gameplay events via `meme_battle_event`. Each event is **sequenced** to ensure in-order processing.

```
← Backend emits "meme_battle_event":
{
  "eventId": "evt_...",
  "eventType": "ARGUMENT_SELECTED",
  "matchId": "match_abc123",
  "turnId": "turn_001",
  "sequence": 4,
  "schemaVersion": 1,
  "occurredAt": "2026-09-06T10:00:00Z",
  "payload": {
    "argumentId": "turn_1_A",
    "actorCharacterId": "bot_a",
    "targetCharacterId": "bot_b",
    "memeText": "Skill issue detected.",
    "animationId": "bot_a_attack_light",
    "finalTotalsAtomic": {
      "turn_1_A": 0,
      "turn_1_B": 101,
      "turn_1_C": 102
    }
  }
}
```

**Code Location:** `WebSocketManager._ws.On("meme_battle_event")`

**Sequence Logic:**
- WebSocketManager tracks `_lastAppliedSequence`
- Duplicate events (sequence ≤ last applied) are skipped
- Events are enqueued only if `sequence > _lastAppliedSequence`
- This ensures exactly-once processing even on reconnect

---

## 📡 Event Types & Handlers

### Event Lifecycle Overview

```
MATCH_CREATED
    ↓
MATCH_STARTED
    ↓
TURN_STARTED (repeated per turn)
├─ ARGUMENT_SELECTED (voting results → action chosen)
├─ MULTIPLIER_SELECTED (bonus damage multiplier)
├─ DAMAGE_APPLIED (actual damage dealt)
├─ HP_CHANGED (health update)
└─ [loop back to next turn]
    ↓
WINNER_DECLARED (match ends)
```

### Detailed Event Handlers

#### 1. **MATCH_CREATED**
```json
{
  "eventType": "MATCH_CREATED",
  "payload": {
    "characterIds": ["bot_a", "bot_b"],
    "initialHpAtomic": 1000,
    "rulesetVersion": 1,
    "rngCommitment": "..."
  }
}
```

**Handler:** `GameManager.HandleMemeBattleEvent()` case `"MATCH_CREATED"`

**Actions:**
```csharp
// Update character names
playerUI?.UpdateName(PlayerUI.Side.Left, "bot_a");
playerUI?.UpdateName(PlayerUI.Side.Right, "bot_b");

// Store max HP
_characterMaxHp["bot_a"] = 1000;
_characterMaxHp["bot_b"] = 1000;

// Initialize health display
playerUI?.UpdateHealthDisplay(PlayerUI.Side.Left, 1000, 1000);
playerUI?.UpdateHealthDisplay(PlayerUI.Side.Right, 1000, 1000);
```

---

#### 2. **TURN_STARTED**
```json
{
  "eventType": "TURN_STARTED",
  "payload": {
    "turnNumber": 1,
    "voteWindowId": "auto:match_abc:1",
    "opensAt": "2026-09-06T10:00:00Z",
    "closesAt": "2026-09-06T10:00:05Z",
    "arguments": [
      {
        "argumentId": "turn_1_A",
        "actorCharacterId": "bot_a",
        "targetCharacterId": "bot_b",
        "memeText": "Skill issue detected.",
        "baseDamageAtomic": 100,
        "animationId": "bot_a_attack_light"
      }
    ]
  }
}
```

**Handler:** `GameManager.HandleMemeBattleEvent()` case `"TURN_STARTED"`

**Actions:**
```csharp
// Start countdown timer for the turn
roundManager?.StartTurnTimer(
  turnNumber: 1,
  opensAtIsoUtc: "2026-09-06T10:00:00Z",
  closesAtIsoUtc: "2026-09-06T10:00:05Z"
);

// Clear old dialogue from previous turn
playerUI?.ClearDialogue(PlayerUI.Side.Left);
playerUI?.ClearDialogue(PlayerUI.Side.Right);
```

**Timer Implementation:**
- `RoundManager` parses ISO-8601 UTC strings
- Calculates remaining time: `closesAt - DateTime.UtcNow`
- Updates UI text every frame: `00:05`, `00:04`, ...

---

#### 3. **ARGUMENT_SELECTED** (Voting Results)
```json
{
  "eventType": "ARGUMENT_SELECTED",
  "payload": {
    "argumentId": "turn_1_A",
    "actorCharacterId": "bot_a",
    "targetCharacterId": "bot_b",
    "memeText": "Skill issue detected.",
    "animationId": "bot_a_attack_light",
    "lowestCandidates": ["turn_1_A"],
    "finalTotalsAtomic": {
      "turn_1_A": 0,
      "turn_1_B": 101,
      "turn_1_C": 102
    }
  }
}
```

**Handler:** `GameManager.HandleMemeBattleEvent()` case `"ARGUMENT_SELECTED"`

**Actions:**
```csharp
// 1. Determine which side is attacking
var attackerSide = SideFromCharacterId("bot_a");    // Left
var targetSide = SideFromCharacterId("bot_b");      // Right

// 2. Play matched attack & hit animations
// Convert "bot_a_attack_light" → "bot_a_hit_light" (variant numbers match)
string hitAnimationId = animationId.Replace("attack", "hit");
animationController?.PlayBothAnimationsWithMatchedVariant(
  Left, "bot_a_attack_light",
  Right, "bot_a_hit_light"  // Same variant (attack_light_5, hit_light_5)
);

// 3. Show meme text on attacker
playerUI?.SetDialogue(PlayerUI.Side.Left, "Skill issue detected.");

// 4. Display voting totals
roundManager?.ShowFinalTotals({
  "turn_1_A": 0,
  "turn_1_B": 101,
  "turn_1_C": 102
});
```

**Animation Details:**
- Backend specifies `animationId` (e.g., `"bot_a_attack_light"`)
- AnimationController resolves to state name: `"bot_a_attack_light_1"` (variant 1-5)
- Attacker plays attack, target plays matching hit (same variant number)
- Crossfade duration: 0.12 seconds (configurable)

---

#### 4. **MULTIPLIER_SELECTED** (Bonus Damage)
```json
{
  "eventType": "MULTIPLIER_SELECTED",
  "payload": {
    "multiplierScaled": 15000,
    "multiplierScale": 10000
  }
}
```

**Handler:** `GameManager.HandleMemeBattleEvent()` case `"MULTIPLIER_SELECTED"`

**Calculation:**
```csharp
float mult = (float)multiplierScaled / multiplierScale;  // 1.5x
roundManager?.ShowMultiplier(mult);  // Display as "1.5x"
```

---

#### 5. **DAMAGE_APPLIED** (Damage Dealt)
```json
{
  "eventType": "DAMAGE_APPLIED",
  "payload": {
    "actorCharacterId": "bot_a",
    "targetCharacterId": "bot_b",
    "damageAtomic": 150,
    "hpBeforeAtomic": 1000,
    "hpAfterAtomic": 850,
    "animationId": "bot_a_attack_light"
  }
}
```

**Handler:** `GameManager.HandleMemeBattleEvent()` case `"DAMAGE_APPLIED"`

**Actions:**
```csharp
// 1. Update HP bar
playerUI?.UpdateHP(PlayerUI.Side.Right, 850, 1000);

// 2. Show damage popup on target
playerUI?.ShowDamage(PlayerUI.Side.Right, 150);

// 3. Update health display text
playerUI?.UpdateHealthDisplay(PlayerUI.Side.Right, 850, 1000);

// NOTE: Animation already played in ARGUMENT_SELECTED
// Do NOT replay animation here (would override the hit animation)
```

**Important:** Animation is played once in `ARGUMENT_SELECTED`, not here. This event only updates the UI.

---

#### 6. **HP_CHANGED** (Authoritative HP Update)
```json
{
  "eventType": "HP_CHANGED",
  "payload": {
    "characterId": "bot_b",
    "hpAfterAtomic": 850
  }
}
```

**Handler:** `GameManager.HandleMemeBattleEvent()` case `"HP_CHANGED"`

**Key Principle:**
- This is the **authoritative** HP value from the backend
- Always trust this value; do not calculate HP locally
- Can be used to sync if UI and backend differ

---

#### 7. **WINNER_DECLARED** (Match End)
```json
{
  "eventType": "WINNER_DECLARED",
  "payload": {
    "winnerCharacterId": "bot_a",
    "contributionWindowOpen": false
  }
}
```

**Handler:** `GameManager.HandleMemeBattleEvent()` case `"WINNER_DECLARED"`

**Actions:**
```csharp
// 1. Mark match as ended
_matchEnded = true;

// 2. Show victory screen
var winnerSide = SideFromCharacterId("bot_a");  // Left
playerUI?.ShowVictory(winnerSide);

// 3. Play victory animation
animationController?.PlayAnimation(winnerSide, "Victory");

// Special case: Draw
if (winnerCharacterId == "DRAW") {
    // Show draw screen
}
```

---

## 🔄 Message Processing Pipeline

### WebSocketManager → Queue → GameManager

```
Backend sends JSON over Socket.IO
         ↓
WebSocketManager._ws.On("meme_battle_event") fired
         ↓
Parse JSON, check sequence number
         ↓
If sequence > _lastAppliedSequence:
  Enqueue to _incomingMessages (thread-safe)
  Update _lastAppliedSequence
         ↓
GameManager.Update() dequeues messages
         ↓
HandleRawMessage() deserializes JSON
         ↓
HandleMemeBattleEvent() routes to event handler
         ↓
Update UI, play animations
```

### Why This Design?

1. **Thread-Safe:** Network callbacks happen on background threads; queues prevent race conditions
2. **Deduplication:** Sequence tracking prevents duplicate events after reconnect
3. **In-Order:** Events are processed in the order received
4. **Resilient:** If a handler throws, it doesn't crash the network thread

---

## 🎬 Animation Coordination

### Animation State Naming Convention

```
Format: {character}_{action}_{variant}

Examples:
  bot_a_attack_light_1     (bot_a attacks with light variant 1)
  bot_a_attack_light_5     (bot_a attacks with light variant 5)
  bot_a_hit_light_1        (bot_a gets hit with light variant 1)
  bot_a_idle               (bot_a idle state)
  bot_a_victory            (bot_a victory state)
```

### Attack & Hit Pairing

When `ARGUMENT_SELECTED` event arrives:

```
Backend sends: animationId = "bot_a_attack_light"
         ↓
Attacker (bot_a, Left) plays:  "bot_a_attack_light_X"
Target   (bot_b, Right) plays: "bot_a_hit_light_X"  (variant X matches attacker)
         ↓
Both animations crossfade in parallel
         ↓
Both finish, return to Idle
```

### Code Flow

```csharp
// AnimationController.PlayBothAnimationsWithMatchedVariant()
public void PlayBothAnimationsWithMatchedVariant(
    PlayerUI.Side attackerSide, string attackAnimId,
    PlayerUI.Side targetSide, string hitAnimId)
{
    // Resolve animation IDs to state names with variant numbers
    string attackStateName = ResolveAnimationId(attackAnimId);  // "bot_a_attack_light_3"
    string hitStateName = ResolveAnimationId(hitAnimId);       // "bot_a_hit_light_3"

    // Play on both animators simultaneously
    var animator1 = attackerSide == PlayerUI.Side.Left ? leftAnimator : rightAnimator;
    var animator2 = targetSide == PlayerUI.Side.Left ? leftAnimator : rightAnimator;

    animator1.CrossFade(attackStateName, defaultCrossFade);
    animator2.CrossFade(hitStateName, defaultCrossFade);
}

private string ResolveAnimationId(string animId)
{
    // Input: "bot_a_attack_light" → Output: "bot_a_attack_light_3" (random 1-5)
    // Checks Animator for matching states
}
```

---

## 🔌 Reconnection & Resync

### Scenario: Connection Lost → Reconnect

```
Connection drops
    ↓
WebSocketManager.OnDisconnected fires
    ↓
If _autoReconnect = true, wait _reconnectDelay (default 3s)
    ↓
Call ConnectAsync() again
    ↓
← Connection restored, OnConnected fires
    ↓
If _currentMatchId is set, call SubscribeToMatch()
    ↓
Emit "meme_battle_subscribe":
{
  "matchId": "match_abc123",
  "afterSequence": 6  // Last sequence we processed
}
    ↓
← Backend sends "meme_battle_snapshot":
{
  "snapshot": { /* current state */ },
  "events": [ /* events with sequence > 6 */ ]
}
```

**Code Location:** `WebSocketManager._ws.OnConnected` handler

**Key Points:**
- `afterSequence` tells backend which events we've already seen
- Backend only sends newer events
- Snapshot contains current state (HP, turn info, etc.)

---

## 💾 State Management

### GameManager maintains:

```csharp
private Dictionary<string, long> _characterMaxHp;        // bot_a → 1000
private Dictionary<string, string> _characterNames;      // bot_a → "bot_a"
private MemeBattleMatchSnapshot _latestSnapshot;         // Current match state
private bool _matchEnded;                                // Match concluded?
private long _lastAppliedSequence;                       // (in WebSocketManager)
```

### PlayerUI maintains:

```csharp
public float leftHealth;     // Current HP (Left)
public float leftMaxHealth;  // Max HP (Left)
public float rightHealth;    // Current HP (Right)
public float rightMaxHealth; // Max HP (Right)
```

### RoundManager maintains:

```csharp
private DateTime _turnClosesAtUtc;  // When turn ends
private bool _timerRunning;         // Timer active?
```

---

## 🚨 Error Handling

### Connection Errors

```
← Backend sends error event:
{
  "type": "error",
  "error": "Service credentials are invalid"
}
```

**Handler:** `WebSocketManager.OnError`

**Mapped Error Codes:**
- `SOCKET_AUTH_REQUIRED` - Missing/invalid token
- `UNITY_AUTH_INVALID` - Bad principal
- `MATCH_NOT_FOUND` - Match ID doesn't exist
- `MATCH_SUBSCRIBE_INVALID` - Wrong parameters

---

## 📊 Data Flow Diagram

```
Backend Simulator
  ├─ Selects characters (bot_a, bot_b)
  ├─ Initializes HP
  ├─ Selects actions (arguments)
  ├─ Calculates damage
  └─ Decides winner
         ↓
Socket.IO Events (meme_battle_event)
         ↓
WebSocketManager
  ├─ Parses JSON
  ├─ Checks sequence
  └─ Enqueues to _incomingMessages
         ↓
GameManager.Update()
  ├─ Dequeues messages
  ├─ Deserializes event
  └─ Calls HandleMemeBattleEvent()
         ↓
Event-Specific Handlers
  ├─ PlayerUI.UpdateName()
  ├─ PlayerUI.UpdateHealthDisplay()
  ├─ RoundManager.StartTurnTimer()
  ├─ AnimationController.PlayAnimation()
  └─ PlayerUI.ShowDamage()
         ↓
User sees animations and UI updates
```

---

## 🎮 Local Testing & Debug Features

### GameManager Debug Input

```csharp
// Press Q → Left attacks (random preset)
if (Input.GetKeyDown(KeyCode.Q)) {
    PlayBothAnimations(leftState, rightState);
}

// Press E → Right attacks
if (Input.GetKeyDown(KeyCode.E)) {
    PlayBothAnimations(rightState, leftState);
}
```

### WebSocketManager Logging

```csharp
public bool verboseLogging = true;  // Log all events
```

**Output Example:**
```
WebSocketManager INCOMING [2026-09-06T10:00:00.123Z] event=meme_battle_event raw={...}
WebSocketManager OUTGOING [2026-09-06T10:00:00.456Z]: Emit → event=meme_battle_subscribe
```

---

## 📝 Configuration Checklist

### Before Production

- [ ] Set `_serverUrl` to production API endpoint (not localhost)
- [ ] Update `_serviceToken` to production shared secret
- [ ] Set `verboseLogging = false` to reduce log spam
- [ ] Disable `enableLocalInputTesting` (Q/E keys)
- [ ] Set `_autoReconnect = true` for resilience
- [ ] Configure `_reconnectDelay` (3s recommended)

### Before Each Match

- [ ] Call `GameManager.ApplyRawMessage()` or subscribe via `initialMatchId`
- [ ] Verify `PlayerUI`, `RoundManager`, `AnimationController` are assigned
- [ ] Check that Animator has all required states

---

## 🔍 Debugging Checklist

### Problem: Events not arriving

1. Check `WebSocketManager.IsConnected()` returns true
2. Verify `_serverUrl` is correct
3. Confirm `_serviceToken` matches backend
4. Check browser console for Socket.IO errors
5. Verify firewall/proxy allows WebSocket

### Problem: Animation not playing

1. Check Animator has the state (debug output shows state name)
2. Verify crossfade duration isn't too long
3. Check that `leftAnimator` and `rightAnimator` are assigned
4. Look for errors in `ResolveAnimationId()`

### Problem: Health not updating

1. Verify `HP_CHANGED` or `DAMAGE_APPLIED` events are received
2. Check `_characterMaxHp` has correct initial values
3. Confirm `PlayerUI.UpdateHealthDisplay()` is called
4. Verify health bar UI components are not null

### Problem: Timer not counting down

1. Check `TURN_STARTED` event is received
2. Verify ISO-8601 date parsing works (check Debug.Log output)
3. Confirm `RoundManager.StartTurnTimer()` is called
4. Check `_timerRunning = true` in RoundManager

---

## 📚 Related Files

| File | Purpose |
|------|---------|
| `Assets/Scripts/Network/WebSocketManager.cs` | Socket.IO client, message buffering |
| `Assets/Scripts/Core/GameManager.cs` | Event router, state manager |
| `Assets/Scripts/UI/PlayerUI.cs` | Health bars, dialogue, damage popups |
| `Assets/Scripts/UI/RoundManager.cs` | Timer, round display, multiplier |
| `Assets/Scripts/Animation/AnimationController.cs` | Animation state transitions |
| `Assets/Scripts/Core/LoaddingManager.cs` | Scene loading, error display |

---

## 🎯 Summary

1. **Network:** WebSocketManager connects via Socket.IO, buffers events, tracks sequences
2. **Controller:** GameManager receives events, updates state, dispatches to sub-systems
3. **Animation:** AnimationController plays Animator transitions (attack, hit, idle, etc.)
4. **UI:** PlayerUI and RoundManager display health, timer, damage, dialogue
5. **Backend is Authority:** Never calculate damage/HP locally; always trust backend values
6. **Exactly-Once Semantics:** Sequence numbers prevent duplicate processing on reconnect

**The game is essentially a state machine driven by backend events. Unity's job is to render that state.**

