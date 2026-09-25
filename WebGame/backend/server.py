"""Local API server and static host for the browser game."""
from __future__ import annotations

import os
from pathlib import Path
from typing import Literal

from fastapi import FastAPI, Query
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

from boxing_env import ACTIONS, BoxingEnv

ROOT = Path(__file__).resolve().parents[1]
app = FastAPI(title="Corner Boxing RL API", version="1.0.0")
env = BoxingEnv()
policy = None
try:
    from stable_baselines3 import PPO
    model_path = os.environ.get("BOXING_MODEL", "models/boxing_ppo.zip")
    if Path(model_path).exists():
        policy = PPO.load(model_path)
except ImportError:
    pass


class Observation(BaseModel):
    distance: float = Field(2.0, ge=0, le=8)
    player_health: float = Field(100, ge=0, le=100)
    opponent_health: float = Field(100, ge=0, le=100)
    player_stamina: float = Field(100, ge=0, le=100)
    opponent_stamina: float = Field(100, ge=0, le=100)
    player_guarding: bool = False
    player_attacking: bool = False
    opponent_recovering: bool = False
    difficulty: Literal["easy", "medium", "hard"] = "easy"


def choose_action(o: Observation) -> str:
    """Fallback utility policy; a trained PPO model takes priority when loaded."""
    if policy is not None:
        obs = [min(o.distance / 4, 1), o.opponent_health / 100,
               o.player_health / 100, o.opponent_stamina / 100,
               o.player_stamina / 100, float(o.player_attacking),
               float(o.player_guarding), float(o.player_attacking),
               float(o.opponent_recovering)]
        idx, _ = policy.predict(obs, deterministic=True)
        return ACTIONS[int(idx)]
    if o.distance > 1.8:
        return "move_forward"
    if o.player_attacking and o.opponent_stamina > 20:
        return "block"
    if o.opponent_stamina < 18:
        return "block"
    pressure = {"easy": .38, "medium": .63, "hard": .84}[o.difficulty]
    import random
    if random.random() > pressure:
        return "block" if o.player_attacking else "idle"
    if o.opponent_recovering and o.player_health < 30:
        return "cross"
    return random.choices(["jab", "cross", "leftHook", "rightHook"], [.42, .30, .14, .14])[0]


@app.get("/api/health")
def health():
    return {"status": "ok", "model_loaded": policy is not None, "actions": list(ACTIONS)}


@app.post("/api/opponent/act")
def opponent_action(observation: Observation):
    action = choose_action(observation)
    return {"action": action, "policy": "ppo" if policy else "utility"}


@app.post("/api/episode/reset")
def reset_episode(seed: int | None = None):
    obs, info = env.reset(seed=seed)
    return {"observation": obs.tolist(), "info": info}


@app.post("/api/episode/step")
def step_episode(action: int = Query(ge=0, le=len(ACTIONS) - 1)):
    obs, reward, terminated, truncated, info = env.step(action)
    return {"observation": obs.tolist(), "reward": reward, "terminated": terminated,
            "truncated": truncated, "info": info}


app.mount("/", StaticFiles(directory=ROOT, html=True), name="webgame")
