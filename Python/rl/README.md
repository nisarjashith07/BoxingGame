# BoxingGame RL Training & Policy Export Pipeline

Python-native Proximal Policy Optimization (PPO) pipeline that trains a boxing AI opponent without Unity.  
Trained policies export directly to a compact binary format (`boxing_ai_policy.bytes`) executed by Unity via a pure C# zero-allocation neural network (`RLPolicyNetwork.cs`), or to ONNX for offline validation.

---

## Directory Layout

```
BoxingGame/
└── Python/
    └── rl/
        ├── boxing_env.py            # Gymnasium combat simulator aligned with CombatSettings.cs
        ├── policy.py                # Actor-Critic PyTorch network (18 obs -> 12 actions)
        ├── train.py                 # PPO training loop with curriculum learning
        ├── export_weights_binary.py # Exports binary weights for Unity (boxing_ai_policy.bytes)
        ├── export_onnx.py           # Exports to ONNX for offline inspection
        ├── inference_test.py        # Validates inference against scripted bots
        ├── requirements.txt         # RL dependencies (PyTorch, Gymnasium, ONNX)
        └── checkpoints/
            └── boxing_ai_final.pt   # PyTorch final checkpoint
```

---

## Requirements & Setup

Install RL dependencies:
```bash
pip install -r Python/rl/requirements.txt
```

*(Note: the main real-time motion tracking app uses `Python/requirements.txt` which does not require PyTorch or GPU).*

---

## How to Train

Run from the **BoxingGame root** directory:

```bash
# As a module (recommended)
python -m Python.rl.train

# Or directly
python Python/rl/train.py
```

**Training takes ~20–60 min on CPU, ~5–15 min with a GPU.**

### Curriculum
| Steps | Opponent |
|-------|----------|
| 0 – 500 000 | Easy |
| 500 000 – 1 000 000 | Medium |
| 1 000 000 – 2 000 000 | Hard |

Checkpoints are saved every **100 000 steps** to `Python/rl/checkpoints/`.  
Final model: `Python/rl/checkpoints/boxing_ai_final.pt`

### Key hyperparameters (`train.py`)
| Parameter | Value |
|-----------|-------|
| `total_timesteps` | 2 000 000 |
| `num_envs` | 8 |
| `num_steps` | 128 |
| `learning_rate` | 3e-4 (linear decay) |
| `gamma` | 0.99 |
| `gae_lambda` | 0.95 |
| `clip_coeff` | 0.2 |
| `value_loss_coeff` | 0.5 |
| `entropy_coeff` | 0.01 |
| `num_epochs` | 4 |
| `num_minibatches` | 4 |

---

## How to Export

### Export for Unity (Recommended)
Export binary policy weights directly into Unity's Resources:
```bash
python Python/rl/export_weights_binary.py
```
This produces `Assets/Resources/boxing_ai_policy.bytes` with self-describing v2 contract metadata (schema hash, normalization scales, rules version).

### Export to ONNX (Offline Inspection)
```bash
python -m Python.rl.export_onnx
```
Exports `Python/rl/boxing_ai_policy.onnx` for inspection with [Netron](https://netron.app) or Python inference testing.

---

## How to Test Inference

```bash
python -m Python.rl.inference_test
```

Runs 20 episodes vs each difficulty level (Easy / Medium / Hard) using the ONNX model and prints:
- Win rate, average reward, average episode length
- Action distribution histogram
- Inference latency in ms

---

## Model Specification

### Input Observation Vector

| Name | Shape | dtype | Description |
|------|-------|-------|-------------|
| `observation` | `[1, 18]` | float32 | 18-element normalized observation vector |

#### Observation fields (index order)

| Index | Field | Range | Description |
|-------|-------|-------|-------------|
| 0 | `distance` | [0, 1] | Distance divided by 10.0m |
| 1 | `selfHealth01` | [0, 1] | AI HP / 100.0 |
| 2 | `selfStamina01` | [0, 1] | AI stamina / 100.0 |
| 3 | `opponentHealth01` | [0, 1] | Opponent HP / 100.0 |
| 4 | `opponentStamina01` | [0, 1] | Opponent stamina / 100.0 |
| 5 | `selfGuard01` | [0, 1] | AI guard meter |
| 6 | `opponentGuard01` | [0, 1] | Opponent guard meter |
| 7 | `relativeX` | [−1, 1] | Relative lateral displacement / 5.0m |
| 8 | `relativeZ` | [−1, 1] | Relative forward displacement / 5.0m |
| 9 | `relativeVelocityX` | [−1, 1] | Local relative lateral velocity (m/s) |
| 10 | `relativeVelocityZ` | [−1, 1] | Local relative forward velocity (m/s) |
| 11 | `opponentAttackIntent01` | [0, 1] | Opponent attack intent |
| 12 | `incomingAttack` | {0, 1} | Active punch incoming |
| 13 | `opponentRecovering` | {0, 1} | Opponent in whiff/hit recovery |
| 14 | `selfCanAttack` | {0, 1} | AI has stamina and cooldown ready |
| 15 | `selfCanDodge` | {0, 1} | AI has stamina and cooldown ready |
| 16 | `selfCanBlock` | {0, 1} | AI has stamina ready |
| 17 | `healthAdvantage` | [−1, 1] | (selfHealth01 - oppHealth01) |

### Action mapping

| Index | Action | Cost | Base Damage | Startup / Description |
|---|---|---|---|---|
| 0 | `Idle` | 0 | 0 | Resting stance, regenerates stamina |
| 1 | `MoveForward` | 0 | 0 | Steps into punching pocket |
| 2 | `MoveBackward` | 0 | 0 | Steps back out of range |
| 3 | `StrafeLeft` | 0 | 0 | Circles counter-clockwise |
| 4 | `StrafeRight` | 0 | 0 | Circles clockwise |
| 5 | `Jab` | 12 | 8 | Fast straight lead punch |
| 6 | `Cross` | 12 | 14 | Power straight punch |
| 7 | `HookLeft` | 12 | 18 | Lateral hook punch |
| 8 | `HookRight` | 12 | 18 | Lateral hook punch |
| 9 | `Block` | 5/s | 0 | Reduces incoming damage by 75% |
| 10 | `DodgeLeft` | 20 | 0 | Active invulnerability slip |
| 11 | `DodgeRight` | 20 | 0 | Active invulnerability slip |

---

## Integration in Unity (Zero-Package Runtime)

No Barracuda or external neural network packages are needed. The Unity project includes a native, high-performance C# inference engine (`RLPolicyNetwork.cs`):

1. **Automatic Loading:**
   The AI opponent controller (`RLOpponentController.cs`) automatically loads `Assets/Resources/boxing_ai_policy.bytes` on startup via `Resources.Load<TextAsset>()`.
2. **Contract Hash Verification:**
   The network validates the magic header (`0x424F5849`) and checks the FNV-1a schema hash (`0xD6A3BCD8`) to ensure the observation layout and combat rules match the trained model.
3. **Zero Allocation:**
   All inference buffers are pre-allocated at startup. Running forward inference takes less than `0.02ms` on modern CPUs with 0 B/frame GC allocation.
4. **Resilient Fallback:**
   If no policy asset is provided or the file is corrupted, the controller smoothly falls back to the heuristic `AIOpponentBrain`.

| Value | Action |
|-------|--------|
| 0 | Idle |
| 1 | MoveForward |
| 2 | MoveBackward |
| 3 | StrafeLeft |
| 4 | StrafeRight |
| 5 | Jab |
| 6 | Cross |
| 7 | HookLeft |
| 8 | HookRight |
| 9 | Block |
| 10 | DodgeLeft |
| 11 | DodgeRight |

---

## Unity Integration (Barracuda)

### C# Execution Example (`RLOpponentController.cs`)

```csharp
// Loads boxing_ai_policy.bytes from Assets/Resources/
var textAsset = Resources.Load<TextAsset>("boxing_ai_policy");
var policy = RLPolicyNetwork.LoadFromBytes(textAsset.bytes);

// 18-element buffer (zero GC allocations)
float[] buffer = new float[18];
RLOpponentController.BuildInputBuffer(currentObservation, buffer, policy);

// Predict next boxing action in < 0.02ms
BoxingAction action = policy.Predict(buffer, out float[] logits);
```

### Observation Normalization Rules
The network expects inputs normalized identically to training:
- Distance: `distance / 10.0f`
- Health / Stamina: `[0.0, 1.0]`
- Relative Displacements: `relativeX / 5.0f`, `relativeZ / 5.0f`
- Relative Velocities: `localRelVel.x`, `localRelVel.z` clamped to `[-1.0, 1.0]` m/s

---

## Reward Function Reference

| Event | Reward |
|-------|--------|
| Landing a punch | +5 × (damage / 18) |
| Taking a punch | −3 × (damage / 18) |
| Blocking an incoming hit | +2.0 |
| Dodging an incoming hit | +1.5 |
| Idle while opponent attacking (can attack) | −0.5 |
| Health advantage per step | +0.1 × advantage |
| Win the round | +15 |
| Lose the round | −10 |
| Living penalty per step | −0.01 |

---

## Architecture Summary

```
Input [18]
  │
  ├─ Linear(18→256) → LayerNorm → ReLU
  ├─ Linear(256→256) → LayerNorm → ReLU
  └─ Linear(256→128) → LayerNorm → ReLU
        │
        ├─ Actor head:  Linear(128→12) → logits → argmax → action
        └─ Critic head: Linear(128→1)  → value (training only)
```

Total parameters: **~132 000**

