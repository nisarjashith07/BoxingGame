"""Gymnasium boxing environment used to train and evaluate opponent policies."""
from __future__ import annotations

import numpy as np
import gymnasium as gym
from gymnasium import spaces

PUNCHES = ("jab", "cross", "leftHook", "rightHook")
ACTIONS = ("idle", "move_forward", "move_backward", "block", *PUNCHES, "dodge_left", "dodge_right")
DAMAGE = {"jab": 8.0, "cross": 14.0, "leftHook": 18.0, "rightHook": 18.0}


class BoxingEnv(gym.Env):
    """One 3-minute boxing bout, from the opponent's point of view.

    Observation indices: distance (0..4m), self HP, player HP, self stamina,
    player stamina (all percentages), incoming attack, player guarding,
    player attacking, self recovering (booleans as floats).
    Actions: idle, approach, retreat, block, four punches, dodge left/right.
    """
    metadata = {"render_modes": []}

    def __init__(self, max_steps: int = 1800, difficulty: str = "medium"):
        super().__init__()
        self.max_steps = max_steps
        self.difficulty = difficulty
        self.action_space = spaces.Discrete(len(ACTIONS))
        self.observation_space = spaces.Box(0.0, 1.0, shape=(9,), dtype=np.float32)
        self.reset()

    def _observation(self):
        return np.array([np.clip(self.distance / 4, 0, 1), self.hp / 100,
                         self.player_hp / 100, self.stamina / 100,
                         self.player_stamina / 100, float(self.incoming),
                         float(self.player_guarding), float(self.player_attacking),
                         float(self.recovering)], dtype=np.float32)

    def reset(self, *, seed=None, options=None):
        super().reset(seed=seed)
        self.hp = self.player_hp = self.stamina = self.player_stamina = 100.0
        self.distance = 2.5
        self.incoming = self.player_guarding = self.player_attacking = False
        self.recovering = False
        self.cooldown = self.steps = 0
        self._player_next = 0
        return self._observation(), {"winner": None}

    def step(self, action):
        action = int(action)
        if not self.action_space.contains(action):
            raise ValueError(f"invalid action {action}")
        name = ACTIONS[action]
        self.steps += 1
        self.cooldown = max(0, self.cooldown - 1)
        self.stamina = min(100, self.stamina + 0.45)
        self.player_stamina = min(100, self.player_stamina + 0.45)
        reward = -0.001
        self.recovering = self.cooldown > 0

        # The simulated player pressures, occasionally guards and throws punches.
        if self.steps >= self._player_next:
            self.player_attacking = self.np_random.random() < 0.72
            self.player_guarding = not self.player_attacking and self.np_random.random() < .22
            if self.player_attacking and self.distance < 1.9:
                raw = float(self.np_random.choice([8, 14, 18], p=[.48, .34, .18]))
                taken = raw * (.25 if name == "block" else 1)
                self.hp = max(0, self.hp - taken)
                self.player_stamina = max(0, self.player_stamina - 12)
                reward -= taken / 100
                self.incoming = True
            else:
                self.incoming = False
            self._player_next = self.steps + int(self.np_random.integers(9, 19))

        if name == "move_forward":
            self.distance = max(.7, self.distance - .12)
        elif name == "move_backward":
            self.distance = min(4, self.distance + .12)
        elif name.startswith("dodge_"):
            if self.stamina >= 20:
                self.stamina -= 20
                self.incoming = False
                reward += .035 if self.player_attacking else -.005
        elif name in DAMAGE and self.cooldown == 0 and self.stamina >= 12:
            self.stamina -= 12
            self.cooldown = 7
            if self.distance <= 1.8:
                amount = DAMAGE[name] * (.25 if self.player_guarding else 1)
                self.player_hp = max(0, self.player_hp - amount)
                reward += amount / 100 + (.02 if not self.player_guarding else 0)
            else:
                reward -= .02
        elif name == "block" and self.incoming:
            reward += .025

        self.distance = float(np.clip(self.distance, .7, 4))
        terminated = self.hp <= 0 or self.player_hp <= 0
        truncated = self.steps >= self.max_steps
        winner = "opponent" if self.player_hp <= 0 else "player" if self.hp <= 0 else None
        if winner == "opponent": reward += 1.0
        elif winner == "player": reward -= 1.0
        elif truncated:
            reward += .25 if self.hp >= self.player_hp else -.25
        return self._observation(), float(reward), terminated, truncated, {
            "action": name, "winner": winner, "opponent_hp": self.hp,
            "player_hp": self.player_hp, "distance": self.distance,
        }


def train_ppo(total_timesteps: int = 250_000, output_path: str = "models/boxing_ppo"):
    """Train a PPO policy. Requires the optional `rl` dependencies."""
    from stable_baselines3 import PPO
    from stable_baselines3.common.env_util import make_vec_env
    env = make_vec_env(BoxingEnv, n_envs=8)
    model = PPO("MlpPolicy", env, verbose=1, learning_rate=3e-4,
                n_steps=1024, batch_size=256, gamma=.99)
    model.learn(total_timesteps=total_timesteps)
    model.save(output_path)
    return model
