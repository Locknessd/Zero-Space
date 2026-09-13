# Meme Battle — Event Flow Diagrams

## Complete Match Lifecycle

```
┌─────────────────────────────────────────────────────────────────────┐
│ 1. CONNECTION PHASE                                                  │
└─────────────────────────────────────────────────────────────────────┘

Unity Client                                Backend Server
      │                                            │
      │─── ConnectAsync() ──────────────────────→  │
      │                                            │
      │  Auth: {                                   │
      │    clientType: "UNITY_RENDERER"           │
      │    serviceToken: "test-unity-secret"      │
      │  }                                         │
      │                                            │
      │  ←───── OnConnected ─────────────────────  │
      │  (Socket.IO handshake complete)           │
      │                                            │
      └──────────────────────────────────────────  │


┌─────────────────────────────────────────────────────────────────────┐
│ 2. MATCH START PHASE                                                 │
└─────────────────────────────────────────────────────────────────────┘

Unity Client                                Backend Server
      │                                            │
      │─── Emit "meme_battle_start" ────────────→  │
      │  {                                         │
      │    requestId: "unity-start-..."          │
      │  }                                         │
      │                                            │  (Simulator runs)
      │                                            │  - Create match
      │                                            │  - Select characters
      │                                            │  - Initialize HP=1000
      │                                            │  - Generate events
      │                                            │
      │  ←──── "meme_battle_start_result" ──────  │
      │  {                                         │
      │    requestId: "...",                       │
      │    matchId: "match_abc",                  │
      │    snapshot: {                             │
      │      characterHpAtomic: {                  │
      │        bot_a: 1000,                       │
      │        bot_b: 1000                        │
      │      },                                   │
      │      latestSequence: 3                    │
      │    }                                       │
      │  }                                         │
      │                                            │
      │ GameManager processes:                    │
      │ - Update character names                  │
      │ - Set max HP                              │
      │ - Display health bars                     │
      │                                            │
      └──────────────────────────────────────────  │


┌─────────────────────────────────────────────────────────────────────┐
│ 3. TURN LOOP PHASE (repeated for each turn)                         │
└─────────────────────────────────────────────────────────────────────┘

             ┌─────────────────────────────────┐
             │ T U R N   L O O P               │
             └─────────────────────────────────┘
                          │
                          ↓
        ┌──────────────────────────────────┐
        │ TURN_STARTED (sequence: 4)       │
        │  - turnNumber: 1                  │
        │  - opensAt: "2026-09-06T10:00Z"   │
        │  - closesAt: "2026-09-06T10:05Z"  │
        └──────────────────────────────────┘
                          │
                ┌─────────┴─────────┐
                │                   │
                ↓                   ↓
    [GameManager]        [RoundManager]
    Clear dialogue        Start timer
    from previous turn    (countdown 5s)
                │                   │
                └─────────┬─────────┘
                          ↓
        ┌──────────────────────────────────┐
        │ ARGUMENT_SELECTED (sequence: 5)  │
        │  - actorCharacterId: "bot_a"     │
        │  - targetCharacterId: "bot_b"    │
        │  - animationId: "bot_a_attack_  │
        │               light"             │
        │  - memeText: "Skill issue..."    │
        │  - finalTotalsAtomic: {...}      │
        └──────────────────────────────────┘
                          │
        ┌─────────────────┼─────────────────┐
        │                 │                 │
        ↓                 ↓                 ↓
   [AnimationController]  [PlayerUI]    [RoundManager]
   Play attack + hit      Show meme     Show voting
   animations             text on       totals
                         attacker
        │                 │                 │
        └─────────────────┼─────────────────┘
                          ↓
        ┌──────────────────────────────────┐
        │ MULTIPLIER_SELECTED (seq: 6)     │
        │  - multiplierScaled: 15000        │
        │  - multiplierScale: 10000         │
        │  → Display: 1.5x                  │
        └──────────────────────────────────┘
                          │
                          ↓
        ┌──────────────────────────────────┐
        │ DAMAGE_APPLIED (sequence: 7)     │
        │  - targetCharacterId: "bot_b"    │
        │  - damageAtomic: 150              │
        │  - hpAfterAtomic: 850             │
        └──────────────────────────────────┘
                          │
        ┌─────────────────┼─────────────────┐
        │                 │                 │
        ↓                 ↓                 ↓
   [GameManager]    [PlayerUI]      [PlayerUI]
   Update HP        Show damage     Update health
   in state         popup           bar display
        │                 │                 │
        └─────────────────┼─────────────────┘
                          ↓
        ┌──────────────────────────────────┐
        │ HP_CHANGED (sequence: 8)         │
        │  - characterId: "bot_b"          │
        │  - hpAfterAtomic: 850             │
        │  (authoritative HP from backend) │
        └──────────────────────────────────┘
                          │
                          ↓
                    [HP verified]
                          │
                    No winner yet?
                    /            \
                   Yes            No
                  /                \
                 ↓                  ↓
              Loop                 Break
              back to             (go to
            TURN_STARTED          WINNER)


┌─────────────────────────────────────────────────────────────────────┐
│ 4. MATCH END PHASE                                                   │
└─────────────────────────────────────────────────────────────────────┘

        ┌──────────────────────────────────┐
        │ WINNER_DECLARED (sequence: 100)  │
        │  - winnerCharacterId: "bot_a"    │
        │  - contributionWindowOpen: false │
        └──────────────────────────────────┘
                          │
        ┌─────────────────┼─────────────────┐
        │                 │                 │
        ↓                 ↓                 ↓
   [GameManager]      [PlayerUI]      [AnimationController]
   Mark match         Show victory    Play victory
   as ended           screen          animation
        │                 │                 │
        └─────────────────┼─────────────────┘
                          ↓
                   [Match Complete]
```

---

## Message Queue & Processing

```
┌──────────────────────────────────────────────────────────────────┐
│                    NETWORK THREAD                                │
│                                                                  │
│  Socket.IO callback (OnMessage)                                 │
│      ↓                                                           │
│  Parse JSON                                                     │
│      ↓                                                           │
│  Extract sequence number                                        │
│      ↓                                                           │
│  ┌─────────────────────────────┐                               │
│  │ sequence > _lastApplied?    │                               │
│  └────────┬────────────────────┘                               │
│           │                                                    │
│      Yes  │  No                                                │
│          │  │                                                 │
│          │  → Skip (duplicate)                                │
│          │                                                    │
│          ↓                                                     │
│  ┌─────────────────────────────┐                              │
│  │ Add to _incomingMessages    │                              │
│  │ (ConcurrentQueue - safe)    │                              │
│  └─────────────────────────────┘                              │
│          │                                                    │
│          ↓                                                     │
│  Update _lastAppliedSequence                                  │
│                                                               │
└──────────────────────────────────────────────────────────────────┘
                          │
                          │
        ┌─────────────────↓──────────────────┐
        │     MAIN THREAD (Update)           │
        │                                    │
        │  while (_incomingMessages          │
        │         .TryDequeue(out msg))      │
        │      ↓                             │
        │  Parse JSON object                 │
        │      ↓                             │
        │  Extract "type" field              │
        │      ↓                             │
        │  ┌────────────────────────┐        │
        │  │ Switch on type:        │        │
        │  │ - meme_battle_event    │        │
        │  │ - meme_battle_snapshot │        │
        │  │ - meme_battle_start... │        │
        │  │ - error                │        │
        │  └────────────────────────┘        │
        │      ↓                             │
        │  Call appropriate handler         │
        │      ↓                             │
        │  Handler updates UI/state         │
        │                                   │
        └───────────────────────────────────┘
```

---

## Reconnection Flow

```
Normal Connection
      │
      ↓
Connected
      │
      └─ Network error / Disconnect
            ↓
      ┌─────────────────────┐
      │ OnDisconnected      │
      │ fires               │
      └─────────────────────┘
            ↓
      _ws = null
            ↓
      ┌─────────────────────┐
      │ _autoReconnect?     │
      │ _isClosing?         │
      └────────┬────────────┘
               │
        Yes    │  No
       /       │   \
      /        │    \ (exit)
     ↓         │
  ┌──────────────────┐
  │ Wait             │
  │ _reconnectDelay  │
  │ (3 seconds)      │
  └────────┬─────────┘
           │
           ↓
      ┌──────────────────────────┐
      │ Call ConnectAsync()      │
      │ again                    │
      └────────┬─────────────────┘
               │
               ↓
      ┌──────────────────────────┐
      │ OnConnected fires        │
      │ (Socket.IO reconnect)    │
      └────────┬─────────────────┘
               │
               ↓
      ┌──────────────────────────┐
      │ _currentMatchId is set?  │
      │ (from before disconnect) │
      └────────┬─────────────────┘
               │
        Yes    │  No
       /       │   \
      /        │    \ (idle)
     ↓         │
  ┌──────────────────────────┐
  │ Emit "meme_battle_       │
  │ subscribe":              │
  │ {                        │
  │   matchId: "match_abc",  │
  │   afterSequence: 6       │
  │ }                        │
  └────────┬─────────────────┘
           │
           ↓
  ←─────────────────────────────
  "meme_battle_snapshot":
  {
    snapshot: { /* current */ },
    events: [ /* seq > 6 */ ]
  }
           │
           ↓
  ┌──────────────────────┐
  │ Process snapshot     │
  │ Process missing events
  │ (replay catchup)     │
  └──────────────────────┘
           │
           ↓
      Back in sync!
```

---

## Animation State Machine

```
┌──────────────────────────────────┐
│         I D L E                  │
│  (default, both fighters)        │
│  - No attack happening           │
│  - Waiting for next turn         │
│  - Position recovery active      │
└──────────┬───────────────────────┘
           │
    ARGUMENT_SELECTED
    event received
           │
           ↓
┌──────────────────────────────────────┐
│         CROSSFADE START             │
│  (duration: 0.12s)                  │
│                                     │
│  Attacker:                          │
│  bot_a_attack_light_3   ← Animate   │
│                                     │
│  Target:                            │
│  bot_a_hit_light_3      ← Animate   │
│  (same variant #)                   │
└──────────┬──────────────────────────┘
           │
      Animations loop
      (attack frame, hit reaction,
       etc.)
           │
           ↓
┌──────────────────────────────────┐
│    ANIMATION COMPLETE            │
│  (auto-transition)               │
└──────────┬───────────────────────┘
           │
           ↓
┌──────────────────────────────────┐
│    BACK TO IDLE                  │
│  Both fighters return to idle    │
│  Position recovery kicks in      │
│  (move back to combat distance)  │
└──────────────────────────────────┘
           │
      Wait for next
      ARGUMENT_SELECTED
           │
           ↓
      [Loop back to Attack]
```

---

## Sequence Number Tracking

```
Initial State:
  _lastAppliedSequence = 0

Event 1: sequence = 1
  1 > 0? YES
  ↓
  Process
  _lastAppliedSequence = 1

Event 2: sequence = 3 (skip 2 for demo)
  3 > 1? YES
  ↓
  Process
  _lastAppliedSequence = 3

Duplicate Event: sequence = 2
  2 > 3? NO
  ↓
  Skip (already processed)

Event 3: sequence = 4
  4 > 3? YES
  ↓
  Process
  _lastAppliedSequence = 4

[Connection Lost]
[Reconnect]
[Request: afterSequence = 4]

Events from Backend:
  sequence = 5 (new event after reconnect)
  5 > 4? YES
  ↓
  Process
  _lastAppliedSequence = 5

[All caught up!]
```

---

## Character-to-Side Mapping

```
Backend sends characterId

        ↓

GameManager.SideFromCharacterId()

        ↓

  ┌─────────────────┐
  │ Which char?     │
  ├─────────────────┤
  │ "bot_a"         │
  │   → Left        │
  │                 │
  │ "bot_b"         │
  │   → Right       │
  │                 │
  │ (else)          │
  │   → null        │
  └─────────────────┘

        ↓

Use mapping for:
- Animation selection
- UI health bar update
- Damage popup display
- Dialogue positioning
```

---

## Health Bar Update Flow

```
Event: HP_CHANGED or DAMAGE_APPLIED
      │
      ↓
Extract: characterId ("bot_b")
         hpAfterAtomic (850)
         maxHpAtomic (1000)
      │
      ↓
Call: PlayerUI.UpdateHealthDisplay(
    side: Right,
    currentHp: 850,
    maxHp: 1000
)
      │
      ↓
PlayerUI updates:
┌─────────────────────────────────┐
│ Health Bar (fill %)             │
│ ████████░░ (85% filled)         │
│                                 │
│ HP Text                         │
│ "850 / 1000"                    │
│                                 │
│ Health Color                    │
│ ├─ 100% = Green                │
│ ├─ 50% = Yellow                │
│ └─ 0% = Red                     │
└─────────────────────────────────┘
      │
      ↓
Next frame: Display updated
```

---

## Error Code Mapping

```
Backend error message → Error code

"Service credentials..."
    → SOCKET_AUTH_REQUIRED

"Not authenticated"
    → UNITY_AUTH_REQUIRED

"Principal is invalid"
    → UNITY_AUTH_INVALID

"Event is not allowed"
    → UNITY_EVENT_FORBIDDEN

"Match not found"
    → MATCH_NOT_FOUND

"Invalid afterSequence"
    → MATCH_SUBSCRIBE_INVALID

(else)
    → UNKNOWN_ERROR
```

---

## Timer Countdown Display

```
TURN_STARTED:
  closesAt = "2026-09-06T10:00:05Z" (UTC)

RoundManager.Update() every frame:
  now = DateTime.UtcNow
  remaining = closesAt - now

  If remaining > 1 hour:
    Display: "HH:MM:SS"
  Else:
    Display: "MM:SS"

Timeline:
  T+0s: "00:05"
  T+1s: "00:04"
  T+2s: "00:03"
  T+3s: "00:02"
  T+4s: "00:01"
  T+5s: "00:00"
  T+6s: Timer stops
```

---

## Multiplier Calculation

```
Event: MULTIPLIER_SELECTED
{
  "multiplierScaled": 15000,
  "multiplierScale": 10000
}

GameManager calculates:
  float multiplier = (float)multiplierScaled
                    / (float)multiplierScale
                  = 15000 / 10000
                  = 1.5f

RoundManager.ShowMultiplier(1.5f):
  Display as: "1.5x"

Other examples:
  20000 / 10000 = 2.0x
   5000 / 10000 = 0.5x
  10000 / 10000 = 1.0x
```

---

## Voting Totals Display

```
Event: ARGUMENT_SELECTED
{
  "finalTotalsAtomic": {
    "turn_1_A": 0,
    "turn_1_B": 101,
    "turn_1_C": 102
  }
}

GameManager extracts dictionary

RoundManager.ShowFinalTotals():
  Parse and format for display

Display could show:
  Option A: 0 votes ❌
  Option B: 101 votes ✓
  Option C: 102 votes

  → Winner: C
```

---

## Damage Popup Lifecycle

```
Event: DAMAGE_APPLIED
  damageAtomic: 150
  targetCharacterId: "bot_b"

GameManager calls:
  PlayerUI.ShowDamage(Right, 150)

PlayerUI behavior:
  ┌──────────────┐
  │   +150       │  (Red, animated)
  │    ↑         │  Float up
  │    ↑         │  over target
  │    ↑         │
  │    ↑         │  Fade out
  │   (fade)     │
  └──────────────┘

After 1-2 seconds:
  Disappear
  HP bar already updated
```

---

## Debug Input System

```
Q Key Pressed:
  │
  ├─ GameManager.IsKeyDown(KeyCode.Q)
  │
  ├─ Try legacy Input.GetKeyDown()
  │  Fallback: Reflection to new Input System
  │
  └─ PlayBothAnimations(left, right)
     ├─ Attacker: Left plays attack
     └─ Target: Right plays hit

E Key Pressed:
  │
  └─ PlayBothAnimations(right, left)
     ├─ Attacker: Right plays attack
     └─ Target: Left plays hit
```

---

## Match Victory Conditions

```
Backend sends: WINNER_DECLARED
{
  "winnerCharacterId": "bot_a" or "bot_b" or "DRAW"
}

GameManager handles:
  ┌─────────────────────────┐
  │ winnerCharacterId?      │
  ├─────────────────────────┤
  │ "bot_a"                 │
  │   → Left side wins      │
  │   → Play victory anim   │
  │   → Show victory UI     │
  │                         │
  │ "bot_b"                 │
  │   → Right side wins     │
  │   → Play victory anim   │
  │   → Show victory UI     │
  │                         │
  │ "DRAW"                  │
  │   → Both lose           │
  │   → Show draw UI        │
  │   → No victory anim     │
  └─────────────────────────┘
      │
      ↓
  _matchEnded = true

  No more events processed
  Game waits for user
  to start new match
```

