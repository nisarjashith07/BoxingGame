# Boxing AI Opponents — Development Plan and Implementation v1

## 1. Objective

Build the three host-controlled training opponents for the boxing competition:

- Easy
- Medium
- Hard

These opponents are intended to be baseline training opponents for participant RL agents.

The AI is deliberately separated from the boxing mechanics. The AI decides **what action should be taken**; the boxing environment decides **how that action is executed**.

Core interface:

```text
Boxing Observation
        ↓
   AI Opponent Brain
        ↓
   Boxing Action
        ↓
   Boxing Environment / Character Controller
```

This separation is important because the same observation/action interface will later be used by participant RL models.

---

## 2. Design Decision

### Version 1 uses a scripted utility/FSM-style opponent brain

We are not training the host opponents with reinforcement learning yet.

Reason:

1. We need stable opponents before participant RL training exists.
2. Their behavior must be reproducible and controllable.
3. Difficulty needs explicit tuning.
4. Debugging scripted decision logic is easier than debugging RL at this stage.
5. The exact same `BoxingAction` interface can later accept trained RL models.

The host opponents are therefore **baseline AI agents**, not the final competition AI architecture.

Later, the same interface can be used for host-trained RL opponents if required.

---

## 3. Architecture

```text
                 BoxingObservation
                        │
                        ▼
              ┌───────────────────┐
              │ AIOpponentBrain   │
              │                   │
              │ Perception memory │
              │ State selection   │
              │ Utility scoring    │
              │ Action selection   │
              └─────────┬─────────┘
                        │
                        ▼
                  BoxingAction
                        │
                        ▼
              Boxing Environment
                        │
                        ▼
                 Character/Combat
```

---

## 4. Internal AI States

The v1 brain behaves like a lightweight finite-state/utility system.

### Approach

Used when the opponent is outside attack range.

Goal:
- close distance
- maintain useful position
- avoid blindly running forward

### Maintain

Used when distance is near the preferred range.

Goal:
- hold useful distance
- small lateral movement
- wait for an opening

### Attack

Used when an attack is useful.

Goal:
- choose jab/cross/hook
- continue a short combo when skill allows
- exploit low opponent stamina/health

### Defend

Triggered by an incoming attack.

Goal:
- block
- dodge
- reduce damage

### Recover / Reposition

Triggered when stamina is low, health is dangerous, or positioning becomes poor.

Goal:
- create space
- recover stamina
- avoid unnecessary exchanges

### Punish

Triggered when the opponent is recovering from an action.

Goal:
- counter after a successful defensive read or opponent recovery

---

## 5. Observation Input

The AI brain currently consumes these values:

```text
Distance
Self health
Self stamina
Opponent health
Opponent stamina
Self guard
Opponent guard
Relative X position
Relative Z position
Relative X velocity
Relative Z velocity
Opponent attack intent
Incoming attack
Opponent recovering
Can attack
Can dodge
Can block
Round time
```

The values should ultimately come from the official `BoxingEnvironment` rather than directly from the character transform.

---

## 6. Action Output

The action interface is fixed at 12 actions:

```text
0  Idle
1  MoveForward
2  MoveBackward
3  StrafeLeft
4  StrafeRight
5  Jab
6  Cross
7  HookLeft
8  HookRight
9  Block
10 DodgeLeft
11 DodgeRight
```

The AI never directly changes the player's transform, health, or hitboxes.

It only requests an action.

---

## 7. Difficulty Design

Difficulty is controlled by profile parameters rather than separate AI implementations.

Important parameters:

```text
Aggression
Defense
Mobility
Combo skill
Counter skill
Adaptation
Anticipation
Consistency
Randomness
Decision interval
Stamina conservation
Risk tolerance
Preferred distance
Attack distance
Retreat distance
```

### Easy

Behavior goals:

- slower decisions
- more random choices
- weak defense
- limited combos
- poor anticipation
- little adaptation
- moderate aggression
- occasional unnecessary movement

Expected result:

The opponent demonstrates the game mechanics but makes frequent tactical mistakes.

### Medium

Behavior goals:

- faster decisions
- better defense
- basic combinations
- occasional counters
- moderate adaptation
- reasonable positioning
- moderate stamina management

Expected result:

The opponent should require the participant agent to understand distance, attacks, defense, and timing.

### Hard

Behavior goals:

- fast decisions
- strong defense
- good movement
- frequent combinations
- strong counter behavior
- adaptation to opponent tendencies
- resource awareness
- lower decision noise

Expected result:

The opponent should punish obvious mistakes and maintain strong tactical pressure.

---

## 8. Adaptation Layer

The brain contains a lightweight opponent memory.

The memory tracks estimates such as:

```text
Opponent attack tendency
Opponent close-range tendency
Opponent guard tendency
```

The hard profile uses this information more strongly than the medium profile. Easy mostly ignores it.

This provides a progression:

```text
Easy   → mostly reactive
Medium → reactive + basic adaptation
Hard   → reactive + adaptation + anticipation
```

---

## 9. Combo System

Combos are not hard-coded as a complete sequence.

Instead, the brain maintains a short combo state.

Example:

```text
Jab
 ↓
Cross
 ↓
Hook
```

The probability of continuing a combo depends on `comboSkill`.

Therefore:

```text
Easy   → mostly single punches
Medium → short combinations
Hard   → more consistent combinations
```

---

## 10. Defensive Logic

When `incomingAttack` is true, the brain evaluates:

```text
Can dodge?
Can block?
Current stamina?
Current distance?
AI defense ability?
```

It then chooses among:

```text
Dodge Left
Dodge Right
Block
```

The hard opponent has the strongest defensive probability and anticipation.

---

## 11. Resource Management

The brain considers stamina before attacking.

Low stamina causes the AI to:

- reduce attack frequency
- move away
- avoid long combos
- wait for recovery

The hard opponent has the strongest resource management.

This is important because an AI that attacks continuously is easy to exploit.

---

## 12. Reaction Difficulty

Difficulty should not simply mean:

> increase movement speed

Instead, difficulty is produced by several factors:

```text
Decision speed
Decision quality
Reaction reliability
Prediction
Defense
Combo quality
Positioning
Adaptation
Resource management
```

This makes difficulty feel like a difference in intelligence rather than only a stat multiplier.

---

## 13. Current File Structure

```text
Boxing_AI_Opponents_v1/
│
├── Scripts/
│   └── AI/
│       ├── BoxingAction.cs
│       ├── BoxingObservation.cs
│       ├── AIOpponentProfile.cs
│       ├── OpponentPatternMemory.cs
│       ├── AIOpponentBrain.cs
│       └── AIOpponentController.cs
│
└── Docs/
    └── AI_Opponents_Development_Plan.md
```

---

## 14. How It Will Connect to the Boxing Game

The current brain intentionally does not know how Unity animations, hitboxes, movement, or damage are implemented.

The final connection should be:

```text
BoxingEnvironment
      │
      ▼
BuildObservation()
      │
      ▼
AIOpponentController.Evaluate()
      │
      ▼
BoxingAction
      │
      ▼
Action Executor
      │
      ▼
Game/Combat Systems
```

This prevents the AI from bypassing competition rules.

---

## 15. Development Sequence

### Phase A — AI Core

Completed in this package:

- action enum
- observation contract
- difficulty profile
- opponent memory
- decision brain
- Unity controller wrapper

### Phase B — Environment Adapter

Next:

- build real `BoxingObservation` from the boxing environment
- send the AI action to the action executor
- verify all 12 actions actually execute

### Phase C — Easy Bot Calibration

Test:

- attack frequency
- defense frequency
- movement behavior
- stamina behavior
- reaction delay

### Phase D — Medium Bot Calibration

Add:

- better combos
- basic counters
- basic adaptation
- better positioning

### Phase E — Hard Bot Calibration

Add:

- stronger anticipation
- stronger counters
- more reliable defense
- stronger adaptation
- better range management

### Phase F — Stress Testing

Run many matches against:

- stationary player
- aggressive player
- defensive player
- movement-heavy player
- random-action player

Check that the AI does not become trapped in repetitive loops.

---

## 16. What We Are Deliberately NOT Building Yet

The following are outside the current AI-opponent task:

- competition website
- cloud hosting
- participant submission backend
- leaderboard
- player rating
- model evaluation infrastructure
- final RL training pipeline
- MediaPipe changes
- final FPP implementation

The immediate focus is the **AI opponent brain**.

---

## 17. Success Criteria for v1

The AI opponent system is ready for integration when:

```text
Easy / Medium / Hard can be instantiated
                  ↓
Each produces only legal BoxingActions
                  ↓
Each reacts to the same observation contract
                  ↓
Difficulty differences are visible in play
                  ↓
No AI code directly edits game state
                  ↓
The controller can later be replaced by an RL model
```

---

## 18. Next Concrete Implementation Step

Connect `AIOpponentController` to the real boxing environment.

Required adapter:

```text
Environment → BoxingObservation
AI           → BoxingAction
Action layer → Execute BoxingAction
```

Once this adapter exists, we can immediately spawn:

```text
Easy Bot
Medium Bot
Hard Bot
```

inside Unity and begin balancing them.
