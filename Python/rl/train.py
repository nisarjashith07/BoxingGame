"""
train.py
========
PPO training script for the BoxingGame AI.

Usage
-----
    python -m Python.rl.train
    # or directly:
    python c:/Users/harsh/Desktop/BoxingGame/Python/rl/train.py

Curriculum
----------
  0 -  500 000 steps  ->  Easy   opponent
  500k - 1 000 000    ->  Medium opponent
  1M  - 2 000 000     ->  Hard   opponent

Checkpoints are saved every 100 000 steps to:
  Python/rl/checkpoints/boxing_ai_<step>.pt

Final model:
  Python/rl/checkpoints/boxing_ai_final.pt
"""

from __future__ import annotations

import argparse
import os
import sys
import time
import math
import random
from collections import deque
from pathlib import Path
from typing import List

import numpy as np
import torch
import torch.nn as nn
import torch.optim as optim

# -- Path setup so this works as both a module and a script --------------------
_THIS_DIR = Path(__file__).resolve().parent
_ROOT     = _THIS_DIR.parent.parent          # BoxingGame/
sys.path.insert(0, str(_ROOT))

from Python.rl.boxing_env import BoxingEnv   # noqa: E402
from Python.rl.policy     import BoxingPolicy  # noqa: E402

# --- Hyper-parameters ---------------------------------------------------------

TOTAL_TIMESTEPS  = 2_000_000
NUM_ENVS         = 8
NUM_STEPS        = 128          # per-env steps per rollout
LEARNING_RATE    = 3e-4
GAMMA            = 0.99
GAE_LAMBDA       = 0.95
CLIP_COEFF       = 0.2
VALUE_LOSS_COEFF = 0.5
ENTROPY_COEFF    = 0.01
MAX_GRAD_NORM    = 0.5
NUM_EPOCHS       = 4
NUM_MINIBATCHES  = 4
BATCH_SIZE       = NUM_ENVS * NUM_STEPS               # 1024
MINIBATCH_SIZE   = BATCH_SIZE // NUM_MINIBATCHES      # 256

CHECKPOINT_DIR   = _THIS_DIR / "checkpoints"
CHECKPOINT_EVERY = 100_000     # timesteps

# -- Curriculum schedule -------------------------------------------------------
CURRICULUM = [
    (500_000,   'easy'),
    (1_000_000, 'medium'),
    (2_000_000, 'hard'),
]

# --- Vectorised Environments (pure Python, no subprocess) ---------------------

class VecBoxingEnv:
    """
    Lightweight synchronous vectorised wrapper around BoxingEnv.
    Does NOT use multiprocessing - fast enough for CPU PPO.
    """

    def __init__(self, num_envs: int, difficulty: str = 'easy'):
        self.num_envs   = num_envs
        self.difficulty = difficulty
        self.envs: List[BoxingEnv] = [
            BoxingEnv(difficulty=difficulty, seed=i)
            for i in range(num_envs)
        ]
        self.obs_shape  = (18,)
        self.num_actions = 12

    def reset(self) -> np.ndarray:
        obs_list = []
        for env in self.envs:
            obs, _ = env.reset()
            obs_list.append(obs)
        return np.stack(obs_list, axis=0)   # [N, 18]

    def step(self, actions: np.ndarray):
        obs_list, rew_list, done_list, info_list = [], [], [], []
        for i, env in enumerate(self.envs):
            obs, rew, term, trunc, info = env.step(int(actions[i]))
            done = term or trunc
            if done:
                obs, _ = env.reset()
            obs_list.append(obs)
            rew_list.append(rew)
            done_list.append(done)
            info_list.append(info)
        return (
            np.stack(obs_list),
            np.array(rew_list, dtype=np.float32),
            np.array(done_list, dtype=bool),
            info_list,
        )

    def set_difficulty(self, difficulty: str):
        self.difficulty = difficulty
        for env in self.envs:
            env.set_difficulty(difficulty)

    def close(self):
        for env in self.envs:
            env.close()


# --- GAE computation ----------------------------------------------------------

def compute_gae(
    rewards:    torch.Tensor,   # [T, N]
    values:     torch.Tensor,   # [T, N]
    dones:      torch.Tensor,   # [T, N]
    next_value: torch.Tensor,   # [N]
    gamma:      float,
    gae_lambda: float,
) -> tuple[torch.Tensor, torch.Tensor]:
    """Return (advantages, returns) both [T, N]."""
    T, N = rewards.shape
    advantages = torch.zeros_like(rewards)
    last_gae   = torch.zeros(N, device=rewards.device)

    for t in reversed(range(T)):
        if t == T - 1:
            next_non_term = 1.0 - dones[t].float()
            next_val      = next_value
        else:
            next_non_term = 1.0 - dones[t].float()
            next_val      = values[t + 1]

        delta        = rewards[t] + gamma * next_val * next_non_term - values[t]
        last_gae     = delta + gamma * gae_lambda * next_non_term * last_gae
        advantages[t] = last_gae

    returns = advantages + values
    return advantages, returns


# --- Linear LR schedule -------------------------------------------------------

def linear_schedule(optimizer: optim.Optimizer, frac: float, base_lr: float):
    """Linearly anneal learning rate: lr = base_lr * frac."""
    lr = base_lr * frac
    for pg in optimizer.param_groups:
        pg['lr'] = lr


# --- Stats tracker ------------------------------------------------------------

class RollingStats:
    def __init__(self, window: int = 100):
        self.ep_rewards  = deque(maxlen=window)
        self.ep_lengths  = deque(maxlen=window)
        self.wins        = deque(maxlen=window)
        self.losses      = deque(maxlen=window)

    def update(self, infos: list, dones: np.ndarray, ep_rewards_buf: np.ndarray):
        for i, (info, done) in enumerate(zip(infos, dones)):
            if done:
                self.ep_rewards.append(ep_rewards_buf[i])
                self.ep_lengths.append(info['step'])
                self.wins.append(float(info.get('win', False)))
                self.losses.append(float(info.get('loss', False)))
                ep_rewards_buf[i] = 0.0

    def mean_reward(self):
        return float(np.mean(self.ep_rewards)) if self.ep_rewards else 0.0

    def mean_length(self):
        return float(np.mean(self.ep_lengths)) if self.ep_lengths else 0.0

    def win_rate(self):
        return float(np.mean(self.wins)) if self.wins else 0.0


# --- Progress bar -------------------------------------------------------------

def _bar(frac: float, width: int = 30) -> str:
    filled = int(frac * width)
    return '[' + '#' * filled + '.' * (width - filled) + ']'


def print_stats(
    global_step:  int,
    total_steps:  int,
    difficulty:   str,
    stats:        RollingStats,
    policy_loss:  float,
    value_loss:   float,
    entropy:      float,
    fps:          float,
    lr:           float,
):
    pct  = global_step / total_steps
    bar  = _bar(pct)
    print(
        f"\r{bar} {global_step:>8,}/{total_steps:,} "
        f"[{difficulty:6s}] "
        f"FPS={fps:5.0f} "
        f"R={stats.mean_reward():+7.2f} "
        f"L={stats.mean_length():5.1f} "
        f"WR={stats.win_rate()*100:4.1f}% "
        f"pL={policy_loss:.4f} "
        f"vL={value_loss:.4f} "
        f"H={entropy:.4f} "
        f"lr={lr:.2e}",
        end='', flush=True
    )


# --- Curriculum helper --------------------------------------------------------

def get_difficulty(global_step: int, total_steps: int = TOTAL_TIMESTEPS) -> str:
    if global_step < total_steps * 0.25:
        return 'easy'
    elif global_step < total_steps * 0.50:
        return 'medium'
    else:
        return 'hard'


# --- Main training loop -------------------------------------------------------

def train(
    total_timesteps: int = TOTAL_TIMESTEPS,
    num_envs: int = NUM_ENVS,
    checkpoint_every: int = CHECKPOINT_EVERY,
):
    batch_size = num_envs * NUM_STEPS
    minibatch_size = batch_size // NUM_MINIBATCHES

    CHECKPOINT_DIR.mkdir(parents=True, exist_ok=True)
    device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
    print(f"Training on: {device}")
    print(f"Batch size: {batch_size}  |  Minibatch size: {minibatch_size}")
    print(f"Total timesteps: {total_timesteps:,}\n")

    # -- Model & optimiser ------------------------------------------------
    policy    = BoxingPolicy().to(device)
    optimizer = optim.Adam(policy.parameters(), lr=LEARNING_RATE, eps=1e-5)

    total_params = sum(p.numel() for p in policy.parameters())
    print(f"Policy parameters: {total_params:,}")

    # -- Environments -----------------------------------------------------
    current_diff = get_difficulty(0, total_timesteps)
    vec_env      = VecBoxingEnv(num_envs, difficulty=current_diff)
    stats        = RollingStats(window=200)

    # -- Storage tensors (pre-allocated) ----------------------------------
    obs_buf    = torch.zeros((NUM_STEPS, num_envs, 18),  device=device)
    act_buf    = torch.zeros((NUM_STEPS, num_envs),      device=device, dtype=torch.long)
    lp_buf     = torch.zeros((NUM_STEPS, num_envs),      device=device)
    val_buf    = torch.zeros((NUM_STEPS, num_envs),      device=device)
    rew_buf    = torch.zeros((NUM_STEPS, num_envs),      device=device)
    done_buf   = torch.zeros((NUM_STEPS, num_envs),      device=device)

    # Episode reward accumulator (per env)
    ep_rew_acc = np.zeros(num_envs, dtype=np.float32)

    # -- Initial reset -----------------------------------------------------
    current_obs = torch.tensor(vec_env.reset(), dtype=torch.float32, device=device)

    global_step      = 0
    update_idx       = 0
    num_updates      = max(1, total_timesteps // batch_size)
    last_ckpt_step   = 0
    t_start          = time.time()
    last_diff        = current_diff

    for update in range(1, num_updates + 1):

        # -- Curriculum check ----------------------------------------------
        new_diff = get_difficulty(global_step, total_timesteps)
        if new_diff != last_diff:
            vec_env.set_difficulty(new_diff)
            print(f"\n[Curriculum] Switching to {new_diff} at step {global_step:,}")
            last_diff = new_diff

        # -- Linear LR decay -----------------------------------------------
        frac = 1.0 - (update - 1) / num_updates
        linear_schedule(optimizer, frac, LEARNING_RATE)

        # -- Rollout -------------------------------------------------------
        policy.eval()
        t_rollout = time.time()

        for step in range(NUM_STEPS):
            with torch.no_grad():
                action, log_prob, _, value = policy.get_action_and_value(current_obs)

            np_actions = action.cpu().numpy()
            next_obs_np, rewards, dones, infos = vec_env.step(np_actions)

            # Accumulate episode rewards
            ep_rew_acc += rewards
            stats.update(infos, dones, ep_rew_acc)

            obs_buf[step]  = current_obs
            act_buf[step]  = action
            lp_buf[step]   = log_prob
            val_buf[step]  = value
            rew_buf[step]  = torch.tensor(rewards, device=device)
            done_buf[step] = torch.tensor(dones.astype(np.float32), device=device)

            current_obs = torch.tensor(next_obs_np, dtype=torch.float32, device=device)
            global_step += num_envs

        # -- Bootstrap value -----------------------------------------------
        with torch.no_grad():
            next_value = policy.get_value(current_obs)

        # -- GAE -----------------------------------------------------------
        advantages, returns = compute_gae(
            rew_buf, val_buf, done_buf, next_value, GAMMA, GAE_LAMBDA
        )
        advantages = (advantages - advantages.mean()) / (advantages.std() + 1e-8)

        # -- Flatten for minibatch training --------------------------------
        b_obs  = obs_buf.reshape(-1, 18)
        b_act  = act_buf.reshape(-1)
        b_lp   = lp_buf.reshape(-1)
        b_adv  = advantages.reshape(-1)
        b_ret  = returns.reshape(-1)

        # -- PPO update ----------------------------------------------------
        policy.train()
        inds = np.arange(batch_size)

        ep_policy_losses = []
        ep_value_losses  = []
        ep_entropies     = []

        for epoch in range(NUM_EPOCHS):
            np.random.shuffle(inds)
            for start in range(0, batch_size, minibatch_size):
                mb_idx = inds[start:start + minibatch_size]
                mb_idx_t = torch.tensor(mb_idx, device=device, dtype=torch.long)

                mb_obs = b_obs[mb_idx_t]
                mb_act = b_act[mb_idx_t]
                mb_adv = b_adv[mb_idx_t]
                mb_ret = b_ret[mb_idx_t]
                mb_lp  = b_lp[mb_idx_t]

                _, new_lp, entropy, new_val = policy.get_action_and_value(mb_obs, mb_act)

                log_ratio  = new_lp - mb_lp
                ratio      = log_ratio.exp()

                # Clipped policy loss
                pg_loss1   = -mb_adv * ratio
                pg_loss2   = -mb_adv * ratio.clamp(1 - CLIP_COEFF, 1 + CLIP_COEFF)
                pg_loss    = torch.max(pg_loss1, pg_loss2).mean()

                # Value loss (also clipped for stability)
                v_clipped  = (val_buf.reshape(-1)[mb_idx_t]
                              + (new_val - val_buf.reshape(-1)[mb_idx_t]).clamp(-CLIP_COEFF, CLIP_COEFF))
                vf_loss1   = (new_val - mb_ret).pow(2)
                vf_loss2   = (v_clipped - mb_ret).pow(2)
                vf_loss    = 0.5 * torch.max(vf_loss1, vf_loss2).mean()

                ent_loss   = entropy.mean()
                loss       = pg_loss + VALUE_LOSS_COEFF * vf_loss - ENTROPY_COEFF * ent_loss

                optimizer.zero_grad()
                loss.backward()
                nn.utils.clip_grad_norm_(policy.parameters(), MAX_GRAD_NORM)
                optimizer.step()

                ep_policy_losses.append(pg_loss.item())
                ep_value_losses.append(vf_loss.item())
                ep_entropies.append(ent_loss.item())

        # -- Metrics -------------------------------------------------------
        fps = batch_size / (time.time() - t_rollout)
        lr  = optimizer.param_groups[0]['lr']
        print_stats(
            global_step, total_timesteps, last_diff, stats,
            np.mean(ep_policy_losses),
            np.mean(ep_value_losses),
            np.mean(ep_entropies),
            fps, lr,
        )

        # -- Checkpointing -------------------------------------------------
        if global_step - last_ckpt_step >= checkpoint_every:
            ckpt_path = CHECKPOINT_DIR / f"boxing_ai_{global_step}.pt"
            torch.save({
                'global_step': global_step,
                'policy_state_dict':    policy.state_dict(),
                'optimizer_state_dict': optimizer.state_dict(),
                'difficulty':           last_diff,
                'mean_reward':          stats.mean_reward(),
                'win_rate':             stats.win_rate(),
            }, ckpt_path)
            print(f"\n  [Saved] Checkpoint: {ckpt_path.name}")
            last_ckpt_step = global_step

    # -- Final save --------------------------------------------------------
    final_path = CHECKPOINT_DIR / "boxing_ai_final.pt"
    torch.save({
        'global_step': global_step,
        'policy_state_dict':    policy.state_dict(),
        'optimizer_state_dict': optimizer.state_dict(),
        'difficulty':           last_diff,
        'mean_reward':          stats.mean_reward(),
        'win_rate':             stats.win_rate(),
    }, final_path)

    vec_env.close()
    elapsed = time.time() - t_start
    print(f"\n\n{'='*60}")
    print(f"Training complete in {elapsed/60:.1f} min")
    print(f"Final mean reward : {stats.mean_reward():+.2f}")
    print(f"Final win rate    : {stats.win_rate()*100:.1f}%")
    print(f"Model saved to    : {final_path}")
    print(f"{'='*60}")


def parse_args():
    parser = argparse.ArgumentParser(description="PPO Training for Boxing AI")
    parser.add_argument("--timesteps", type=int, default=TOTAL_TIMESTEPS, help="Total timesteps to train")
    parser.add_argument("--num-envs", type=int, default=NUM_ENVS, help="Number of parallel envs")
    parser.add_argument("--checkpoint-every", type=int, default=CHECKPOINT_EVERY, help="Checkpoint interval")
    return parser.parse_args()


if __name__ == '__main__':
    args = parse_args()
    train(
        total_timesteps=args.timesteps,
        num_envs=args.num_envs,
        checkpoint_every=args.checkpoint_every,
    )
