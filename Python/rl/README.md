# BoxingGame RL Training Pipeline

Python-native Proximal Policy Optimization (PPO) pipeline that trains a boxing AI brain without Unity.  
After training, export the policy to ONNX and load it in Unity via Barracuda.

---

## Directory Layout

```
BoxingGame/
└── Python/
    └── rl/
        ├── boxing_env.py       # Gymnasium environment
        ├── policy.py           # Actor-Critic neural network
        ├── train.py            # PPO training loop
        ├── export_onnx.py      # Export to ONNX
        ├── inference_test.py   # Test ONNX inference
        ├── boxing_ai_policy.onnx  (generated after export)
        └── checkpoints/
            ├── boxing_ai_100000.pt
            ├── boxing_ai_200000.pt
            ├── ...
            └── boxing_ai_final.pt
```

---

## Requirements

```
Python >= 3.10
torch >= 2.0
numpy
gymnasium >= 0.26
onnx              # for export verification
onnxruntime       # for inference_test.py
```

Install:
```bash
pip install torch numpy gymnasium onnx onnxruntime
```

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

```bash
python -m Python.rl.export_onnx
```

Exports `boxing_ai_policy.onnx` to `Python/rl/`.

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

## ONNX Model Specification

### Input

| Name | Shape | dtype | Description |
|------|-------|-------|-------------|
| `observation` | `[1, 18]` | float32 | 18-element observation vector |

#### Observation fields (index order)

| Index | Field | Range |
|-------|-------|-------|
| 0 | `distance` (normalised) | [0, 1] |
| 1 | `selfHealth01` | [0, 1] |
| 2 | `selfStamina01` | [0, 1] |
| 3 | `opponentHealth01` | [0, 1] |
| 4 | `opponentStamina01` | [0, 1] |
| 5 | `selfGuard01` | [0, 1] |
| 6 | `opponentGuard01` | [0, 1] |
| 7 | `relativeX` | [−1, 1] |
| 8 | `relativeZ` | [−1, 1] |
| 9 | `relativeVelocityX` | [−1, 1] |
| 10 | `relativeVelocityZ` | [−1, 1] |
| 11 | `opponentAttackIntent01` | [0, 1] |
| 12 | `incomingAttack` | {0, 1} |
| 13 | `opponentRecovering` | {0, 1} |
| 14 | `selfCanAttack` | {0, 1} |
| 15 | `selfCanDodge` | {0, 1} |
| 16 | `selfCanBlock` | {0, 1} |
| 17 | `healthAdvantage` | [−1, 1] |

### Outputs

| Name | Shape | dtype | Description |
|------|-------|-------|-------------|
| `action` | `[1]` | int64 | Chosen action (argmax of logits) |
| `logits` | `[1, 12]` | float32 | Raw policy logits for all 12 actions |

### Action mapping

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

### 1. Install Barracuda

In **Package Manager** → *Add package by name*:
```
com.unity.barracuda
```
Version `3.0.0` or later.

### 2. Import the ONNX model

Copy `boxing_ai_policy.onnx` into your Unity project's `Assets/` folder.  
Unity will import it automatically as a `NNModel` asset.

### 3. Create `RLOpponentController.cs`

```csharp
using Unity.Barracuda;
using UnityEngine;

public class RLOpponentController : MonoBehaviour
{
    [Header("Model")]
    public NNModel modelAsset;          // drag the .onnx asset here

    private IWorker _worker;
    private const int OBS_DIM = 18;

    void Start()
    {
        var model = ModelLoader.Load(modelAsset);
        _worker   = WorkerFactory.CreateWorker(WorkerFactory.Type.CSharpBurst, model);
    }

    /// <summary>
    /// Call each AI decision tick. Returns BoxingAction integer.
    /// </summary>
    public int Decide(BoxingObservation obs)
    {
        float[] raw = BuildObservationArray(obs);

        using var tensor = new Tensor(1, OBS_DIM, raw);
        _worker.Execute(tensor);

        // "action" output is the argmax (int64 stored as float in Barracuda)
        using var actionOut = _worker.PeekOutput("action");
        return Mathf.RoundToInt(actionOut[0]);
    }

    private float[] BuildObservationArray(BoxingObservation o)
    {
        float healthAdv = (o.selfHealth01 - o.opponentHealth01);
        return new float[]
        {
            o.distance / 10f,          // normalise distance
            o.selfHealth01,
            o.selfStamina01,
            o.opponentHealth01,
            o.opponentStamina01,
            o.selfGuard01,
            o.opponentGuard01,
            Mathf.Clamp(o.relativeX / 5f, -1f, 1f),
            Mathf.Clamp(o.relativeZ / 5f, -1f, 1f),
            Mathf.Clamp(o.relativeVelocityX, -1f, 1f),
            Mathf.Clamp(o.relativeVelocityZ, -1f, 1f),
            o.opponentAttackIntent01,
            o.incomingAttack ? 1f : 0f,
            o.opponentRecovering ? 1f : 0f,
            o.selfCanAttack ? 1f : 0f,
            o.selfCanDodge ? 1f : 0f,
            o.selfCanBlock ? 1f : 0f,
            healthAdv,
        };
    }

    void OnDestroy()
    {
        _worker?.Dispose();
    }
}
```

### 4. Normalisation contract

The Python environment normalises `distance` by dividing by `ARENA_HALF * 2 = 10.0`.  
Match this in `BuildObservationArray`: `o.distance / 10f`.  
All other fields are already in `[0,1]` or `[-1,1]`.

### 5. Performance tips

- Call `Decide()` every **3–5 fixed-update frames** (≈ 10–17 Hz) rather than every frame.
- Prefer `WorkerFactory.Type.CSharpBurst` on standalone builds.
- Pre-allocate the `float[]` array and reuse it to avoid GC pressure.

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

