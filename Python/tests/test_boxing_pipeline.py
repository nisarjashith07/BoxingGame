"""
test_boxing_pipeline.py
=======================
Automated unit tests for the Python tracking pipeline, binary weight exporter,
schema contract hashing, and Gymnasium combat simulator.
"""

import json
import math
import struct
import unittest
from pathlib import Path

# Add Python directory to sys.path
import sys
_THIS_DIR = Path(__file__).resolve().parent
_PYTHON_DIR = _THIS_DIR.parent
_REPO_DIR = _PYTHON_DIR.parent
if str(_PYTHON_DIR) not in sys.path:
    sys.path.insert(0, str(_PYTHON_DIR))

from data.pose_data import PoseData, Landmark
from tracking.one_euro_filter import OneEuroFilter, SkeletonOneEuroFilter
from rl.export_weights_binary import compute_fnv1a_32, CANONICAL_CONTRACT

try:
    import gymnasium as gym
    from rl.boxing_env import BoxingEnv, FighterState, PUNCH_STATS, DODGE_COST, BLOCK_REDUCTION, STEP_DT
    HAS_GYMNASIUM = True
except ImportError:
    HAS_GYMNASIUM = False


class TestPoseData(unittest.TestCase):
    def test_json_serialization_with_timestamp(self):
        landmarks = [Landmark(x=0.1, y=0.2, z=-0.3, visibility=0.9) for _ in range(33)]
        pose = PoseData(
            frame=42,
            timestamp=12.3456,
            landmarks=landmarks,
            left_punch_speed=2.5,
            right_punch_speed=1.1,
            left_punch_state="punching",
            right_punch_state="guard",
            punch_type="LEFT JAB / STRAIGHT"
        )
        json_str = pose.to_json()
        data = json.loads(json_str)

        self.assertEqual(data["frame"], 42)
        self.assertAlmostEqual(data["timestamp"], 12.3456, places=4)
        self.assertEqual(len(data["landmarks"]), 33)
        self.assertAlmostEqual(data["landmarks"][0]["x"], 0.1, places=3)
        self.assertAlmostEqual(data["landmarks"][0]["y"], 0.2, places=3)
        self.assertAlmostEqual(data["landmarks"][0]["z"], -0.3, places=3)
        self.assertAlmostEqual(data["left_punch_speed"], 2.5, places=2)
        self.assertEqual(data["punch_type"], "LEFT JAB / STRAIGHT")


class TestOneEuroFilter(unittest.TestCase):
    def test_filter_smooths_jitter(self):
        filt = OneEuroFilter(min_cutoff=1.5, beta=0.35)
        # Apply initial and slight noisy step
        val0 = filt.filter(1.0, 0.0)
        val1 = filt.filter(1.05, 0.016)
        val2 = filt.filter(0.98, 0.033)
        self.assertFalse(math.isnan(float(val1)))
        self.assertFalse(math.isnan(float(val2)))
        self.assertTrue(0.95 <= float(val2) <= 1.05)

    def test_skeleton_filter(self):
        skel_filt = SkeletonOneEuroFilter(min_cutoff=1.5, beta=0.35, landmark_count=33)
        raw_lms = [Landmark(x=float(i)*0.01, y=0.5, z=-0.2, visibility=0.8) for i in range(33)]
        smoothed = skel_filt.filter_skeleton(raw_lms, 0.016)
        self.assertEqual(len(smoothed), 33)
        self.assertFalse(math.isnan(smoothed[0].x))
        self.assertAlmostEqual(smoothed[0].y, 0.5, places=3)


class TestModelContractAndWeights(unittest.TestCase):
    def test_fnv1a_schema_hash_matches_csharp(self):
        expected_csharp_hash = 0xD6A3BCD8
        computed_hash = compute_fnv1a_32(CANONICAL_CONTRACT)
        self.assertEqual(computed_hash, expected_csharp_hash,
                         f"Python FNV-1a hash 0x{computed_hash:08X} != C# EXPECTED_SCHEMA_HASH 0x{expected_csharp_hash:08X}")

    def test_shipped_binary_weights_validity(self):
        weights_path = _REPO_DIR / "Assets" / "Resources" / "boxing_ai_policy.bytes"
        self.assertTrue(weights_path.exists(), f"Weights asset missing at {weights_path}")

        file_bytes = weights_path.read_bytes()
        self.assertGreater(len(file_bytes), 8)

        magic, version = struct.unpack_from("<II", file_bytes, 0)
        self.assertEqual(magic, 0x424F5849, f"Invalid magic header: 0x{magic:08X}")
        self.assertIn(version, (1, 2), f"Unsupported version: {version}")

        expected_floats = (256 * 18) + 256 + 256 + 256 + \
                          (256 * 256) + 256 + 256 + 256 + \
                          (128 * 256) + 128 + 128 + 128 + \
                          (12 * 128) + 12
        header_size = 8 if version == 1 else 32
        expected_total_bytes = header_size + expected_floats * 4
        self.assertEqual(len(file_bytes), expected_total_bytes,
                         f"File size mismatch: {len(file_bytes)} != expected {expected_total_bytes}")


@unittest.skipUnless(HAS_GYMNASIUM, "gymnasium not installed (requires Python/rl/requirements.txt)")
class TestCombatSimulatorParity(unittest.TestCase):
    def test_stamina_cost_and_velocity_tracking(self):
        env = BoxingEnv()
        obs, info = env.reset(seed=123)

        self.assertEqual(len(obs), 18)
        self.assertFalse(any(math.isnan(x) for x in obs))

        # Perform Jab action (5)
        initial_stamina = env._agent.stamina
        obs, reward, terminated, truncated, info = env.step(5)

        # Check stamina deduction matches PUNCH_STATS[5]['cost'] (12.0)
        expected_cost = PUNCH_STATS[5]['cost']
        self.assertAlmostEqual(env._agent.stamina, initial_stamina - expected_cost, places=1)

        # Perform Dodge action (10)
        env._agent.stamina = 100.0
        env._agent.dodge_cd = 0
        obs, reward, terminated, truncated, info = env.step(10)
        self.assertAlmostEqual(env._agent.stamina, 100.0 - DODGE_COST, places=1)

        # Step movements and verify physical velocity computation (not clipped position)
        env._agent.x = 0.0
        env._agent.z = 0.0
        env.step(1) # Move forward
        # Agent displaced by MOVE_SPEED along z; velocity = MOVE_SPEED / STEP_DT
        expected_vz = 0.25 / STEP_DT
        self.assertAlmostEqual(env._agent.vz, expected_vz, places=2)

    def test_block_damage_reduction(self):
        # Unity block absorbs 75% -> incoming damage multiplier is 0.25
        self.assertAlmostEqual(BLOCK_REDUCTION, 0.25, places=3)


if __name__ == "__main__":
    unittest.main()
