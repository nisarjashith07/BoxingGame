from pathlib import Path

BASE_DIR = Path(__file__).resolve().parent

MODEL_PATH = BASE_DIR / "models" / "pose_landmarker_full.task"

CAMERA_INDEX = 0

UNITY_IP = "127.0.0.1"
UNITY_PORT = 5055

LANDMARK_COUNT = 33