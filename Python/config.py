import os
from pathlib import Path

# Silence TensorFlow / MediaPipe low-level C++ logs & ClearCut uploader warnings
os.environ["GLOG_minloglevel"] = "2"
os.environ["TF_CPP_MIN_LOG_LEVEL"] = "2"

BASE_DIR = Path(__file__).resolve().parent
MODEL_PATH = BASE_DIR / "models" / "pose_landmarker_full.task"

# ==========================================
# DUAL CAMERA CONFIGURATION
# ==========================================
# Front Camera (PC Webcam facing player front)
FRONT_CAMERA_INDEX = 0

# Side Camera (Phone camera positioned on the RIGHT side of player)
SIDE_CAMERA_INDEX = 1

# Capture resolution (640x480 provides fast 50-60 FPS capture with zero USB bus latency)
CAMERA_WIDTH = 640
CAMERA_HEIGHT = 480

# Side camera placement: True if on player's right, False if on player's left
SIDE_CAMERA_ON_RIGHT = True

# Flip side camera horizontally (useful for front-facing selfie camera or mirrored phone app)
SIDE_CAMERA_FLIP_HORIZONTAL = False

# Rotate side camera frame in degrees: 0, 90, 180, 270 (if phone is mounted vertically)
SIDE_CAMERA_ROTATION = 0

# ==========================================
# 3D FUSION & ACCURACY ENGINE
# ==========================================
# MediaPipe detection and tracking confidence thresholds
MIN_DETECTION_CONFIDENCE = 0.55
MIN_TRACKING_CONFIDENCE = 0.60

# Weight of side camera for forward punch depth (0.0 to 1.0)
FUSION_DEPTH_WEIGHT = 0.92

# Scaling multiplier for punch reach extension
PUNCH_REACH_SCALE = 1.20

# Enforce anatomical bone-length rigidity
ENABLE_BONE_CONSTRAINTS = True

# High-speed 1€ Filter Parameters
# min_cutoff: eliminates micro-jitter in guard stance
# beta: high beta eliminates punch lag completely (instant response)
ONE_EURO_MIN_CUTOFF = 1.5
ONE_EURO_BETA = 0.35

# ==========================================
# UNITY NETWORKING
# ==========================================
UNITY_IP = "127.0.0.1"
UNITY_PORT = 5055

LANDMARK_COUNT = 33