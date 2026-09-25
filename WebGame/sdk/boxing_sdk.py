"""Convenience SDK for creating and training Corner Boxing RL opponents."""
from pathlib import Path
import sys

BACKEND = Path(__file__).resolve().parents[1] / "backend"
if str(BACKEND) not in sys.path:
    sys.path.insert(0, str(BACKEND))

from boxing_env import ACTIONS, BoxingEnv, train_ppo  # noqa: E402,F401

__all__ = ["ACTIONS", "BoxingEnv", "train_ppo"]
