"""
inference_test.py
=================
Test inference using the exported ONNX model.

Usage
-----
    python -m Python.rl.inference_test
    # or:
    python c:/Users/harsh/Desktop/BoxingGame/Python/rl/inference_test.py

What it does
------------
1. Loads boxing_ai_policy.onnx
2. Runs a full simulated episode (max 300 steps) against the Hard opponent
3. Prints action distribution, timing, and episode stats
"""

from __future__ import annotations

import sys
import time
from collections import Counter
from pathlib import Path

import numpy as np

# -- Path setup ----------------------------------------------------------------
_THIS_DIR = Path(__file__).resolve().parent
_ROOT     = _THIS_DIR.parent.parent
sys.path.insert(0, str(_ROOT))

from Python.rl.boxing_env import BoxingEnv, MAX_HP  # noqa: E402

ONNX_PATH = _THIS_DIR / "boxing_ai_policy.onnx"

ACTION_NAMES = [
    'Idle', 'MoveForward', 'MoveBackward', 'StrafeLeft', 'StrafeRight',
    'Jab', 'Cross', 'HookLeft', 'HookRight',
    'Block', 'DodgeLeft', 'DodgeRight',
]


def run_onnx_episode(predict_fn, env: BoxingEnv, render: bool = False):
    """Run one episode with the policy, return stats."""
    obs, _ = env.reset(seed=int(np.random.randint(0, 10000)))
    total_reward = 0.0
    action_counts = Counter()
    step = 0

    while True:
        action = predict_fn(obs)
        action_counts[action] += 1

        obs, reward, terminated, truncated, info = env.step(action)
        total_reward += reward
        step += 1

        if render:
            print(
                f"  [{step:3d}] {ACTION_NAMES[action]:13s} "
                f"AgentHP={info['agent_hp']:5.1f} "
                f"OppHP={info['opp_hp']:5.1f} "
                f"r={reward:+.3f}"
            )

        if terminated or truncated:
            break

    return {
        'total_reward':  total_reward,
        'steps':         step,
        'win':           info.get('win', False),
        'loss':          info.get('loss', False),
        'timeout':       info.get('time_out', False),
        'agent_hp':      info.get('agent_hp', 0.0),
        'opp_hp':        info.get('opp_hp', 0.0),
        'action_counts': action_counts,
    }


def main():
    # -- Check ONNX file ---------------------------------------------------
    if not ONNX_PATH.exists():
        print(f"ERROR: ONNX model not found at:\n  {ONNX_PATH}")
        print("Please run export_onnx.py first.")
        sys.exit(1)

    print(f"Loading ONNX model: {ONNX_PATH}")
    print(f"File size: {ONNX_PATH.stat().st_size / 1024:.1f} KB")

    # -- Load inference backend -------------------------------------------
    sess = None
    py_policy = None

    try:
        import onnxruntime as ort
        sess_opts = ort.SessionOptions()
        sess_opts.inter_op_num_threads = 1
        sess_opts.intra_op_num_threads = 1
        sess = ort.InferenceSession(str(ONNX_PATH), sess_options=sess_opts)

        print("\nModel inputs (ONNX):")
        for inp in sess.get_inputs():
            print(f"  {inp.name}: {inp.type} {inp.shape}")
        print("Model outputs (ONNX):")
        for out in sess.get_outputs():
            print(f"  {out.name}: {out.type} {out.shape}")
    except ImportError:
        print("\n[INFO] onnxruntime not installed - evaluating using PyTorch policy checkpoint.")
        from Python.rl.policy import BoxingPolicy
        import torch
        ckpt_path = _THIS_DIR / "checkpoints" / "boxing_ai_final.pt"
        if not ckpt_path.exists():
            print(f"ERROR: Checkpoint not found at:\n  {ckpt_path}")
            sys.exit(1)
        ckpt = torch.load(ckpt_path, map_location='cpu')
        py_policy = BoxingPolicy()
        py_policy.load_state_dict(ckpt['policy_state_dict'])
        py_policy.eval()

    def predict_action(obs_arr):
        if sess is not None:
            inp = obs_arr.reshape(1, 18).astype(np.float32)
            act_arr, _ = sess.run(None, {'observation': inp})
            return int(act_arr[0])
        else:
            import torch
            with torch.no_grad():
                inp = torch.tensor(obs_arr.reshape(1, 18), dtype=torch.float32)
                act = py_policy.get_action(inp)
                return int(act.item() if act.dim() == 0 else act[0].item())

    # -- Benchmark single inference latency -------------------------------
    dummy = np.zeros((1, 18), dtype=np.float32)
    # Warmup
    for _ in range(10):
        predict_action(dummy)
    t0 = time.perf_counter()
    N_BENCH = 1000
    for _ in range(N_BENCH):
        predict_action(dummy)
    t1 = time.perf_counter()
    avg_ms = (t1 - t0) / N_BENCH * 1000
    print(f"\nInference latency: {avg_ms:.3f} ms / call  ({1000/avg_ms:,.0f} calls/sec)")

    # -- Run episodes ------------------------------------------------------
    NUM_EPISODES = 5
    DIFFICULTIES = ['easy', 'medium', 'hard']
    results_by_diff = {}

    for diff in DIFFICULTIES:
        env      = BoxingEnv(difficulty=diff)
        ep_stats = []
        print(f"\n{'-'*55}")
        print(f"Running {NUM_EPISODES} episodes vs {diff.upper()} opponent")
        print(f"{'-'*55}")

        for ep in range(NUM_EPISODES):
            stat = run_onnx_episode(predict_action, env, render=(ep == 0 and diff == 'easy'))
            ep_stats.append(stat)
            outcome = 'WIN' if stat['win'] else ('LOSS' if stat['loss'] else 'DRAW')
            print(
                f"  Ep {ep+1:2d}: {outcome:4s}  "
                f"Reward={stat['total_reward']:+7.2f}  "
                f"Steps={stat['steps']:3d}  "
                f"AgentHP={stat['agent_hp']:5.1f}  OppHP={stat['opp_hp']:5.1f}"
            )

        env.close()
        results_by_diff[diff] = ep_stats

    # -- Summary -----------------------------------------------------------
    print(f"\n{'='*55}")
    print("SUMMARY")
    print(f"{'='*55}")
    print(f"{'Difficulty':<10} {'Win%':>6} {'Avg Reward':>12} {'Avg Steps':>10}")
    print(f"{'-'*40}")
    for diff, eps in results_by_diff.items():
        win_rate   = sum(e['win'] for e in eps) / len(eps) * 100
        avg_reward = np.mean([e['total_reward'] for e in eps])
        avg_steps  = np.mean([e['steps'] for e in eps])
        print(f"{diff:<10} {win_rate:>5.1f}% {avg_reward:>+11.2f} {avg_steps:>9.1f}")

    # -- Action distribution across all episodes ---------------------------
    all_eps   = [e for eps in results_by_diff.values() for e in eps]
    total_act = Counter()
    for ep in all_eps:
        total_act.update(ep['action_counts'])
    total_steps_all = sum(total_act.values())

    print(f"\nAction distribution (all {len(all_eps)} episodes, {total_steps_all} steps):")
    print(f"  {'Action':<14} {'Count':>7} {'Pct':>7}")
    for idx, name in enumerate(ACTION_NAMES):
        cnt = total_act.get(idx, 0)
        pct = cnt / total_steps_all * 100 if total_steps_all else 0
        bar = '#' * int(pct / 2)
        print(f"  {name:<14} {cnt:>7,} {pct:>6.1f}% {bar}")

    print(f"\nInference test complete.")


if __name__ == '__main__':
    main()

