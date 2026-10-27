"""
policy.py
=========
Actor-Critic neural network for the BoxingGame PPO agent.

Architecture
------------
  Input  : 18 float32 features
  Hidden : Linear(18->256) -> LayerNorm -> ReLU
           Linear(256->256) -> LayerNorm -> ReLU
           Linear(256->128) -> LayerNorm -> ReLU
  Actor  : Linear(128->12)  -> logits (no softmax, used with Categorical)
  Critic : Linear(128->1)   -> scalar value estimate

All weights are orthogonally initialised (gain=sqrt2 for hidden, gain=0.01 for
actor, gain=1.0 for critic) - a common PPO best practice.
"""

from __future__ import annotations

import numpy as np
import torch
import torch.nn as nn
from torch.distributions import Categorical
from typing import Tuple


def _layer_init(layer: nn.Linear, gain: float = np.sqrt(2), bias: float = 0.0) -> nn.Linear:
    """Orthogonal weight init with optional bias constant."""
    nn.init.orthogonal_(layer.weight, gain=gain)
    nn.init.constant_(layer.bias, bias)
    return layer


class BoxingPolicy(nn.Module):
    """
    Shared-trunk Actor-Critic network.

    Usage
    -----
    policy = BoxingPolicy()

    # Training:
    action, log_prob, entropy, value = policy.get_action_and_value(obs)

    # Value only:
    value = policy.get_value(obs)

    # Inference (no grad):
    action = policy.get_action(obs)
    """

    OBS_DIM    = 18
    ACTION_DIM = 12

    def __init__(self):
        super().__init__()

        # -- Shared trunk -------------------------------------------------
        self.trunk = nn.Sequential(
            _layer_init(nn.Linear(self.OBS_DIM, 256)),
            nn.LayerNorm(256),
            nn.ReLU(),

            _layer_init(nn.Linear(256, 256)),
            nn.LayerNorm(256),
            nn.ReLU(),

            _layer_init(nn.Linear(256, 128)),
            nn.LayerNorm(128),
            nn.ReLU(),
        )

        # -- Actor head ---------------------------------------------------
        # Small gain -> near-uniform initial policy
        self.actor_head = _layer_init(
            nn.Linear(128, self.ACTION_DIM), gain=0.01
        )

        # -- Critic head --------------------------------------------------
        self.critic_head = _layer_init(
            nn.Linear(128, 1), gain=1.0
        )

    # -- Forward helpers -------------------------------------------------------

    def _features(self, obs: torch.Tensor) -> torch.Tensor:
        return self.trunk(obs)

    def _dist(self, features: torch.Tensor) -> Categorical:
        logits = self.actor_head(features)
        return Categorical(logits=logits)

    # -- Public API ------------------------------------------------------------

    def get_value(self, obs: torch.Tensor) -> torch.Tensor:
        """
        Compute value estimate.

        Parameters
        ----------
        obs : Tensor[..., 18]

        Returns
        -------
        value : Tensor[...]
        """
        features = self._features(obs)
        return self.critic_head(features).squeeze(-1)

    def get_action_and_value(
        self,
        obs: torch.Tensor,
        action: torch.Tensor | None = None,
    ) -> Tuple[torch.Tensor, torch.Tensor, torch.Tensor, torch.Tensor]:
        """
        Sample (or evaluate) an action and return all quantities needed by PPO.

        Parameters
        ----------
        obs    : Tensor[B, 18]
        action : Tensor[B] | None
            If provided, evaluate log-prob for those actions (PPO update phase).
            If None, sample new actions (rollout phase).

        Returns
        -------
        action    : Tensor[B]   int64 action indices
        log_prob  : Tensor[B]   log p(a|s)
        entropy   : Tensor[B]   H[p(?|s)]
        value     : Tensor[B]   V(s)
        """
        features = self._features(obs)
        dist     = self._dist(features)

        if action is None:
            action = dist.sample()

        log_prob = dist.log_prob(action)
        entropy  = dist.entropy()
        value    = self.critic_head(features).squeeze(-1)

        return action, log_prob, entropy, value

    @torch.no_grad()
    def get_action(self, obs: torch.Tensor) -> torch.Tensor:
        """
        Inference-only action sampling (no gradient tape).

        Parameters
        ----------
        obs : Tensor[B, 18] or Tensor[18]

        Returns
        -------
        action : Tensor[B] int64
        """
        if obs.dim() == 1:
            obs = obs.unsqueeze(0)
        features = self._features(obs)
        dist     = self._dist(features)
        return dist.sample().squeeze(0)

    @torch.no_grad()
    def get_logits(self, obs: torch.Tensor) -> torch.Tensor:
        """Return raw actor logits (used for ONNX export)."""
        if obs.dim() == 1:
            obs = obs.unsqueeze(0)
        features = self._features(obs)
        return self.actor_head(features)


# --- Actor-only wrapper for ONNX export ---------------------------------------

class ActorOnly(nn.Module):
    """
    Thin wrapper that exposes only the actor (logits) for ONNX export.
    Outputs both the argmax action and the raw logits.
    """

    def __init__(self, policy: BoxingPolicy):
        super().__init__()
        self.trunk       = policy.trunk
        self.actor_head  = policy.actor_head

    def forward(self, obs: torch.Tensor) -> Tuple[torch.Tensor, torch.Tensor]:
        """
        Parameters
        ----------
        obs    : float32 Tensor [1, 18]

        Returns
        -------
        action : int64 Tensor [1]     argmax of logits
        logits : float32 Tensor [1, 12]
        """
        features = self.trunk(obs)
        logits   = self.actor_head(features)          # [1, 12]
        action   = torch.argmax(logits, dim=-1)       # [1]
        return action, logits


# --- Quick sanity check -------------------------------------------------------

if __name__ == '__main__':
    policy = BoxingPolicy()
    total_params = sum(p.numel() for p in policy.parameters())
    print(f"BoxingPolicy parameters: {total_params:,}")

    obs = torch.randn(4, BoxingPolicy.OBS_DIM)
    action, log_prob, entropy, value = policy.get_action_and_value(obs)
    print(f"action:   {action.shape}  {action.dtype}")
    print(f"log_prob: {log_prob.shape}")
    print(f"entropy:  {entropy.mean().item():.4f}")
    print(f"value:    {value.shape}")

    single_obs = torch.randn(BoxingPolicy.OBS_DIM)
    a = policy.get_action(single_obs)
    print(f"Single inference action: {a.item()}")

