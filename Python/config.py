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
# Can be integer index (e.g. 0) or RTSP/HTTP URL
FRONT_CAMERA_INDEX = 0

# Side Camera (Phone camera positioned on the RIGHT side of player)
# Can be integer index (e.g. 1) or stream URL (e.g. "http://192.168.1.10:8080/video")
SIDE_CAMERA_INDEX = 1

# Capture resolution
CAMERA_WIDTH = 1280
CAMERA_HEIGHT = 720

# Side camera placement: True if on player's right, False if on player's left
SIDE_CAMERA_ON_RIGHT = True

# Flip side camera horizontally (useful for front-facing selfie camera or mirrored phone app)
SIDE_CAMERA_FLIP_HORIZONTAL = False

# Rotate side camera frame in degrees: 0, 90, 180, 270 (if phone is mounted vertically)
SIDE_CAMERA_ROTATION = 0

# ==========================================
# 3D FUSION & ACCURACY ENGINE
# ==========================================
# MediaPipe detection and tracking confidence thresholds (0.0 to 1.0)
# Higher thresholds reject noisy background clutter and unstable keypoints
MIN_DETECTION_CONFIDENCE = 0.60
MIN_TRACKING_CONFIDENCE = 0.65

# Weight of side camera for forward punch depth (0.0 to 1.0)
# 0.92 gives primary depth perception to the high-resolution optical profile
FUSION_DEPTH_WEIGHT = 0.92

# Scaling multiplier for punch reach extension
PUNCH_REACH_SCALE = 1.20

# Enforce anatomical bone-length rigidity (prevents rubber-arm stretching)
ENABLE_BONE_CONSTRAINTS = True

# 1€ Filter Parameters (Industry-standard VR filter)
# min_cutoff: Lower values eliminate micro-jitter when holding guard (e.g., 1.0 - 1.5 Hz)
# beta: Higher values eliminate lag during explosive punches (e.g., 0.05 - 0.20)
ONE_EURO_MIN_CUTOFF = 1.2
ONE_EURO_BETA = 0.12

# ==========================================
# UNITY NETWORKING
# ==========================================
UNITY_IP = "127.0.0.1"
UNITY_PORT = 5055

LANDMARK_COUNT = 33