"""Gymnasium client for training against the real Unity BoxingEnvironment."""
from __future__ import annotations

import json
import socket
import time
import uuid

import gymnasium as gym
import numpy as np
from gymnasium import spaces

# Numeric values match Assets/Scripts/AI/BoxingAction.cs exactly.
UNITY_ACTIONS = (
    "Idle", "MoveForward", "MoveBackward", "StrafeLeft", "StrafeRight",
    "Jab", "Cross", "HookLeft", "HookRight", "Block", "DodgeLeft", "DodgeRight",
)


def observation_vector(o: dict) -> np.ndarray:
    """Convert Unity's BoxingObservation to normalized float32 values (18 features)."""
    return np.asarray([
        np.clip(o["distance"] / 4.0, 0, 1),
        o["selfHealth01"], o["selfStamina01"],
        o["opponentHealth01"], o["opponentStamina01"],
        o["selfGuard01"], o["opponentGuard01"],
        np.clip(o["relativeX"] / 4.0, -1, 1),
        np.clip(o["relativeZ"] / 4.0, -1, 1),
        np.clip(o["relativeVelocityX"] / 5.0, -1, 1),
        np.clip(o["relativeVelocityZ"] / 5.0, -1, 1),
        o["opponentAttackIntent01"],
        float(o["incomingAttack"]), float(o["opponentRecovering"]),
        float(o["selfCanAttack"]), float(o["selfCanDodge"]),
        float(o["selfCanBlock"]), o["roundTime01"],
    ], dtype=np.float32)


class UnityBoxingEnv(gym.Env):
    """Synchronous real-game Gymnasium environment over localhost UDP.

    Run the Unity scene in Play Mode first. The user controls the human boxer
    with the normal controls while Python supplies the AI opponent's action.
    Each ``step`` advances one Unity Update frame.
    """
    metadata = {"render_modes": []}

    def __init__(self, host: str = "127.0.0.1", port: int = 5056,
                 timeout: float = 5.0, max_steps: int = 20_000):
        super().__init__()
        self.address = (host, port)
        self.timeout = timeout
        self.max_steps = max_steps
        self.steps = 0
        self.action_space = spaces.Discrete(len(UNITY_ACTIONS))
        self.observation_space = spaces.Box(-1.0, 1.0, shape=(18,), dtype=np.float32)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.sock.bind(("127.0.0.1", 0))
        self.sock.settimeout(.25)
        self._last_observation = None

    def _request(self, command: dict) -> dict:
        request_id = uuid.uuid4().hex
        payload = json.dumps({"id": request_id, **command}).encode("utf-8")
        deadline = time.monotonic() + self.timeout
        while time.monotonic() < deadline:
            self.sock.sendto(payload, self.address)
            try:
                data, _ = self.sock.recvfrom(65507)
            except socket.timeout:
                continue
            reply = json.loads(data.decode("utf-8"))
            if reply.get("id") != request_id:
                continue
            if reply.get("status") != "ok":
                raise RuntimeError(f"Unity training bridge: {reply.get('winner', 'request failed')}")
            return reply
        raise TimeoutError(
            f"No reply from Unity at {self.address}. Is the BoxingMatch scene running in Play Mode?"
        )

    def reset(self, *, seed=None, options=None):
        super().reset(seed=seed)
        reply = self._request({"command": "reset"})
        self.steps = 0
        self._last_observation = observation_vector(reply["observation"])
        return self._last_observation.copy(), {"unity": True}

    def step(self, action):
        if not self.action_space.contains(action):
            raise ValueError(f"action must be from 0 through {len(UNITY_ACTIONS) - 1}")
        reply = self._request({"command": "step", "action": int(action)})
        self.steps += 1
        obs = observation_vector(reply["observation"])
        self._last_observation = obs
        truncated = bool(reply.get("truncated", False)) or self.steps >= self.max_steps
        info = {"action_name": UNITY_ACTIONS[int(action)], "winner": reply.get("winner", "")}
        return obs.copy(), float(reply.get("reward", 0.0)), bool(reply.get("terminated", False)), truncated, info

    def close(self):
        if self.sock:
            self.sock.close()
            self.sock = None
