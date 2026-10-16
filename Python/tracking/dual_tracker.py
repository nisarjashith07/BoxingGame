import cv2
import time
import math
import numpy as np
import mediapipe as mp

from mediapipe.tasks import python
from mediapipe.tasks.python import vision

from config import (
    MODEL_PATH,
    FRONT_CAMERA_INDEX,
    SIDE_CAMERA_INDEX,
    CAMERA_WIDTH,
    CAMERA_HEIGHT,
    SIDE_CAMERA_ON_RIGHT,
    SIDE_CAMERA_FLIP_HORIZONTAL,
    SIDE_CAMERA_ROTATION,
    FUSION_DEPTH_WEIGHT,
    PUNCH_REACH_SCALE,
    ENABLE_BONE_CONSTRAINTS,
    ONE_EURO_MIN_CUTOFF,
    ONE_EURO_BETA,
    MIN_DETECTION_CONFIDENCE,
    MIN_TRACKING_CONFIDENCE,
    LANDMARK_COUNT,
)
from data.pose_data import PoseData, Landmark
from tracking.threaded_camera import ThreadedCamera
from tracking.one_euro_filter import SkeletonOneEuroFilter


class DualPoseTracker:
    """
    Precision Dual-Camera 3D Pose Tracker for VR Boxing.
    Features:
    - Multi-view 3D stereoscopic sensor fusion (Front X/Y + Side Z Depth).
    - 1€ Filter (One Euro Filter) for zero-latency explosive punches and rock-solid guard stance.
    - Anatomical Bone Length Constraints (eliminates rubber-arm distortion).
    - Interactive 1-click calibration for user-specific arm proportions and camera scale matching.
    """

    def __init__(self):
        print("=" * 60)
        print("INITIALIZING HIGH-PRECISION DUAL-CAMERA VR BOXING TRACKER")
        print(f"Loading MediaPipe Task: {MODEL_PATH}")
        print("=" * 60)

        base_options = python.BaseOptions(model_asset_path=str(MODEL_PATH))
        options = vision.PoseLandmarkerOptions(
            base_options=base_options,
            num_poses=1,
            min_pose_detection_confidence=MIN_DETECTION_CONFIDENCE,
            min_pose_presence_confidence=MIN_DETECTION_CONFIDENCE,
            min_tracking_confidence=MIN_TRACKING_CONFIDENCE,
        )

        # Create separate landmarker instances for Front and Side views
        self.landmarker_front = vision.PoseLandmarker.create_from_options(options)
        self.landmarker_side = vision.PoseLandmarker.create_from_options(options)

        # Start non-blocking threaded video capture for zero latency
        self.cap_front = ThreadedCamera(
            FRONT_CAMERA_INDEX,
            width=CAMERA_WIDTH,
            height=CAMERA_HEIGHT,
            name="Front-Cam"
        )
        self.cap_side = ThreadedCamera(
            SIDE_CAMERA_INDEX,
            width=CAMERA_WIDTH,
            height=CAMERA_HEIGHT,
            name="Side-Cam"
        )

        self.frame_count = 0
        self.side_flip_h = SIDE_CAMERA_FLIP_HORIZONTAL
        self.side_rotation = SIDE_CAMERA_ROTATION
        self.side_on_right = SIDE_CAMERA_ON_RIGHT

        # Auto-orientation forward sign (+1 or -1) for side camera
        self.side_forward_dir = 1.0

        # One Euro Filter for state-of-the-art smoothing
        self.one_euro = SkeletonOneEuroFilter(
            min_cutoff=ONE_EURO_MIN_CUTOFF,
            beta=ONE_EURO_BETA,
            landmark_count=LANDMARK_COUNT
        )
        self.last_timestamp = time.time()

        # Anatomical bone lengths (calibrated or defaults in meters)
        self.upper_arm_length = 0.30
        self.forearm_length = 0.28
        self.shoulder_width = 0.42

        # Interactive calibration state
        self.is_calibrating = False
        self.calibration_start_time = 0.0
        self.calibration_duration = 2.5
        self.calibration_samples = []

        # Punch dynamics tracking
        self.prev_left_wrist = None
        self.prev_right_wrist = None
        self.left_punch_speed = 0.0
        self.right_punch_speed = 0.0
        self.left_punch_state = "guard"
        self.right_punch_state = "guard"
        self.punch_type = "none"

        # Cached raw 2D landmarks for visualizer overlays
        self.latest_2d_front = None
        self.latest_2d_side = None

    def start_calibration(self, duration=2.5):
        """Initiates a 2.5s calibration while the user holds their guard stance."""
        self.is_calibrating = True
        self.calibration_start_time = time.time()
        self.calibration_duration = duration
        self.calibration_samples = []
        print("\n[Calibration] CALIBRATION STARTED: Hold your boxing guard stance facing front camera!")

    def get_calibration_progress(self):
        """Returns 0.0 to 1.0 if calibrating, or None if idle."""
        if not self.is_calibrating:
            return None
        elapsed = time.time() - self.calibration_start_time
        return min(elapsed / self.calibration_duration, 1.0)

    def toggle_side_flip(self):
        self.side_flip_h = not self.side_flip_h
        print(f"[Tracker] Side Camera Horizontal Flip: {self.side_flip_h}")

    def rotate_side_90(self):
        self.side_rotation = (self.side_rotation + 90) % 360
        print(f"[Tracker] Side Camera Rotation: {self.side_rotation} deg")

    def _preprocess_side_frame(self, frame):
        if frame is None:
            return None

        # Apply rotation if mounted in portrait mode
        if self.side_rotation == 90:
            frame = cv2.rotate(frame, cv2.ROTATE_90_CLOCKWISE)
        elif self.side_rotation == 180:
            frame = cv2.rotate(frame, cv2.ROTATE_180)
        elif self.side_rotation == 270:
            frame = cv2.rotate(frame, cv2.ROTATE_90_COUNTERCLOCKWISE)

        # Apply horizontal flip if mirrored
        if self.side_flip_h:
            frame = cv2.flip(frame, 1)

        return frame

    def get_dual_pose(self):
        """
        Reads frames from both cameras, runs pose detection,
        performs 3D stereoscopic fusion, and returns (front_frame, side_frame, pose_data).
        """
        ret_front, front_frame = self.cap_front.read()
        ret_side, raw_side = self.cap_side.read()

        side_frame = self._preprocess_side_frame(raw_side)

        if front_frame is None and side_frame is None:
            return None, None, None

        if front_frame is None:
            front_frame = np.zeros((480, 640, 3), dtype=np.uint8)
        if side_frame is None:
            side_frame = np.zeros((480, 640, 3), dtype=np.uint8)

        # Convert to RGB for MediaPipe
        rgb_front = cv2.cvtColor(front_frame, cv2.COLOR_BGR2RGB)
        rgb_side = cv2.cvtColor(side_frame, cv2.COLOR_BGR2RGB)

        mp_front = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_front)
        mp_side = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb_side)

        res_front = self.landmarker_front.detect(mp_front)
        res_side = self.landmarker_side.detect(mp_side)

        # Cache 2D landmarks for visualizer overlays
        self.latest_2d_front = res_front.pose_landmarks[0] if len(res_front.pose_landmarks) > 0 else None
        self.latest_2d_side = res_side.pose_landmarks[0] if len(res_side.pose_landmarks) > 0 else None

        has_front = len(res_front.pose_world_landmarks) > 0
        has_side = len(res_side.pose_world_landmarks) > 0

        if not has_front and not has_side:
            return front_frame, side_frame, None

        self.frame_count += 1
        now = time.time()
        dt = max(now - self.last_timestamp, 0.001)
        self.last_timestamp = now

        # Perform 3D Multi-View Fusion
        fused_landmarks, side_aligned_landmarks, front_landmarks = self._fuse_poses(
            res_front.pose_world_landmarks[0] if has_front else None,
            res_side.pose_world_landmarks[0] if has_side else None
        )

        # Handle active calibration samples
        if self.is_calibrating and has_front:
            self._collect_calibration_sample(res_front.pose_world_landmarks[0])

        # Apply Anatomical Bone Length Constraints (prevents rubber arms)
        if ENABLE_BONE_CONSTRAINTS:
            fused_landmarks = self._enforce_bone_constraints(fused_landmarks)

        # Apply 1€ Filter for jitter-free stability + instant punch velocity
        smoothed = self.one_euro.filter_skeleton(fused_landmarks, now)

        # Calculate punch velocity and detect boxing punches
        self._analyze_punches(smoothed, dt)

        pose_data = PoseData(
            frame=self.frame_count,
            landmarks=smoothed,
            landmarks_right=side_aligned_landmarks,
            landmarks_front=front_landmarks,
            left_punch_speed=self.left_punch_speed,
            right_punch_speed=self.right_punch_speed,
            left_punch_state=self.left_punch_state,
            right_punch_state=self.right_punch_state,
            punch_type=self.punch_type
        )

        return front_frame, side_frame, pose_data

    def _fuse_poses(self, pose_front, pose_side):
        """
        Fuses Front Camera (lateral X + height Y) with Side Camera (optical punch reach Z + height Y).
        """
        front_list = []
        side_aligned_list = []
        fused_list = []

        # Determine forward direction sign in the side camera view
        if pose_side is not None:
            nose = pose_side[0]
            left_ear = pose_side[7]
            right_ear = pose_side[8]
            ear_x = (left_ear.x + right_ear.x) * 0.5
            
            delta_forward = nose.x - ear_x
            if abs(delta_forward) > 0.02:
                detected_dir = 1.0 if delta_forward > 0 else -1.0
                if not self.side_on_right:
                    detected_dir = -detected_dir
                self.side_forward_dir = 0.90 * self.side_forward_dir + 0.10 * detected_dir

        dir_sign = 1.0 if self.side_forward_dir >= 0 else -1.0

        if pose_side is not None:
            side_hip_x = (pose_side[23].x + pose_side[24].x) * 0.5
        else:
            side_hip_x = 0.0

        left_arm_indices = {11, 13, 15, 17, 19, 21}
        right_arm_indices = {12, 14, 16, 18, 20, 22}

        for i in range(LANDMARK_COUNT):
            lm_f = pose_front[i] if pose_front is not None else None
            lm_s = pose_side[i] if pose_side is not None else None

            vf = float(lm_f.visibility) if (lm_f is not None and hasattr(lm_f, 'visibility')) else (1.0 if lm_f is not None else 0.0)
            vs = float(lm_s.visibility) if (lm_s is not None and hasattr(lm_s, 'visibility')) else (1.0 if lm_s is not None else 0.0)

            # Store raw front landmark
            if lm_f is not None:
                front_list.append(Landmark(x=float(lm_f.x), y=float(lm_f.y), z=float(lm_f.z), visibility=vf))
            else:
                front_list.append(Landmark(x=0.0, y=0.0, z=0.0, visibility=0.0))

            # Store side landmark aligned to front/character coordinates
            if lm_s is not None:
                aligned_z = -(lm_s.x - side_hip_x) * dir_sign * PUNCH_REACH_SCALE
                aligned_y = lm_s.y
                aligned_x = lm_s.z
                side_aligned_list.append(Landmark(x=aligned_x, y=aligned_y, z=aligned_z, visibility=vs))
            else:
                side_aligned_list.append(Landmark(x=0.0, y=0.0, z=0.0, visibility=0.0))

            # --- 3D FUSION COMPUTATION ---
            if lm_f is not None and lm_s is not None:
                # 1. LATERAL (X): Front camera has pure direct line of sight
                fused_x = float(lm_f.x)

                # 2. VERTICAL (Y): Weighted average of both cameras
                total_v = max(vf + vs, 0.001)
                fused_y = float((vf * lm_f.y + vs * lm_s.y) / total_v)

                # 3. DEPTH / PUNCH EXTENSION (Z):
                if i in right_arm_indices or i in left_arm_indices:
                    shoulder_idx = 12 if i in right_arm_indices else 11
                    shoulder_s = pose_side[shoulder_idx]
                    shoulder_f = pose_front[shoulder_idx]

                    # Measure optical reach from shoulder along the side camera's horizontal axis
                    reach_from_shoulder = (lm_s.x - shoulder_s.x) * dir_sign
                    
                    # MediaPipe convention: negative Z is forward towards the opponent
                    side_z = float(shoulder_f.z) - (reach_from_shoulder * PUNCH_REACH_SCALE)

                    # Arm-specific weighting based on line of sight
                    arm_weight = FUSION_DEPTH_WEIGHT * vs
                    fused_z = float(arm_weight * side_z + (1.0 - arm_weight) * lm_f.z)
                else:
                    body_reach = (lm_s.x - side_hip_x) * dir_sign
                    side_z = -(body_reach * PUNCH_REACH_SCALE)
                    weight = (FUSION_DEPTH_WEIGHT * 0.8) * vs
                    fused_z = float(weight * side_z + (1.0 - weight) * lm_f.z)

                fused_visibility = max(vf, vs)
                fused_list.append(Landmark(x=fused_x, y=fused_y, z=fused_z, visibility=fused_visibility))

            elif lm_f is not None:
                fused_list.append(Landmark(x=float(lm_f.x), y=float(lm_f.y), z=float(lm_f.z), visibility=vf))
            elif lm_s is not None:
                aligned_z = -(lm_s.x - side_hip_x) * dir_sign * PUNCH_REACH_SCALE
                fused_list.append(Landmark(x=float(lm_s.z), y=float(lm_s.y), z=aligned_z, visibility=vs))
            else:
                fused_list.append(Landmark(x=0.0, y=0.0, z=0.0, visibility=0.0))

        return fused_list, side_aligned_list, front_list

    def _enforce_bone_constraints(self, landmarks):
        """
        Projects arm landmarks onto rigid anatomical bone lengths (Upper Arm & Forearm).
        Eliminates rubber-arm distortion and preserves natural punch reach.
        """
        if len(landmarks) < 17:
            return landmarks

        constrained = [Landmark(x=lm.x, y=lm.y, z=lm.z, visibility=lm.visibility) for lm in landmarks]

        # Enforce for Left Arm (11: Shoulder, 13: Elbow, 15: Wrist)
        self._constrain_arm(constrained, 11, 13, 15)

        # Enforce for Right Arm (12: Shoulder, 14: Elbow, 16: Wrist)
        self._constrain_arm(constrained, 12, 14, 16)

        return constrained

    def _constrain_arm(self, landmarks, idx_s, idx_e, idx_w):
        s = np.array([landmarks[idx_s].x, landmarks[idx_s].y, landmarks[idx_s].z])
        e = np.array([landmarks[idx_e].x, landmarks[idx_e].y, landmarks[idx_e].z])
        w = np.array([landmarks[idx_w].x, landmarks[idx_w].y, landmarks[idx_w].z])

        # Upper arm constraint: ||E - S|| = L1
        v_se = e - s
        dist_se = np.linalg.norm(v_se)
        if dist_se > 0.01:
            u_se = v_se / dist_se
            # Clamp elbow to true physical upper arm length
            e_constrained = s + u_se * self.upper_arm_length
            landmarks[idx_e].x = float(e_constrained[0])
            landmarks[idx_e].y = float(e_constrained[1])
            landmarks[idx_e].z = float(e_constrained[2])
        else:
            e_constrained = e

        # Forearm constraint: ||W - E|| = L2
        v_ew = w - e_constrained
        dist_ew = np.linalg.norm(v_ew)
        if dist_ew > 0.01:
            u_ew = v_ew / dist_ew
            w_constrained = e_constrained + u_ew * self.forearm_length
            landmarks[idx_w].x = float(w_constrained[0])
            landmarks[idx_w].y = float(w_constrained[1])
            landmarks[idx_w].z = float(w_constrained[2])

    def _collect_calibration_sample(self, pose):
        # Collect shoulder width, upper arm, and forearm samples
        ls = np.array([pose[11].x, pose[11].y, pose[11].z])
        rs = np.array([pose[12].x, pose[12].y, pose[12].z])
        le = np.array([pose[13].x, pose[13].y, pose[13].z])
        re = np.array([pose[14].x, pose[14].y, pose[14].z])
        lw = np.array([pose[15].x, pose[15].y, pose[15].z])
        rw = np.array([pose[16].x, pose[16].y, pose[16].z])

        sw = float(np.linalg.norm(ls - rs))
        l1 = (float(np.linalg.norm(le - ls)) + float(np.linalg.norm(re - rs))) * 0.5
        l2 = (float(np.linalg.norm(lw - le)) + float(np.linalg.norm(rw - re))) * 0.5

        if sw > 0.2 and l1 > 0.15 and l2 > 0.15:
            self.calibration_samples.append((sw, l1, l2))

        elapsed = time.time() - self.calibration_start_time
        if elapsed >= self.calibration_duration:
            self.is_calibrating = False
            if len(self.calibration_samples) > 10:
                avg_sw = float(np.mean([s[0] for s in self.calibration_samples]))
                avg_l1 = float(np.mean([s[1] for s in self.calibration_samples]))
                avg_l2 = float(np.mean([s[2] for s in self.calibration_samples]))

                self.shoulder_width = avg_sw
                self.upper_arm_length = avg_l1
                self.forearm_length = avg_l2
                print(f"[Calibration] COMPLETED SUCCESSFULLY!")
                print(f"[Calibration] Shoulder Width: {avg_sw:.2f}m | Upper Arm: {avg_l1:.2f}m | Forearm: {avg_l2:.2f}m | Max Reach: {avg_l1 + avg_l2:.2f}m")
            else:
                print("[Calibration] Insufficient samples, maintaining default anatomical proportions.")

    def _analyze_punches(self, landmarks, dt):
        """
        Calculates wrist speed in m/s and detects punch type (Jab, Cross, Hook, Guard).
        """
        if len(landmarks) < 17:
            return

        left_wrist = np.array([landmarks[15].x, landmarks[15].y, landmarks[15].z])
        right_wrist = np.array([landmarks[16].x, landmarks[16].y, landmarks[16].z])
        left_shoulder = np.array([landmarks[11].x, landmarks[11].y, landmarks[11].z])
        right_shoulder = np.array([landmarks[12].x, landmarks[12].y, landmarks[12].z])

        if self.prev_left_wrist is not None and dt > 0.001:
            v_l = np.linalg.norm(left_wrist - self.prev_left_wrist) / dt
            v_r = np.linalg.norm(right_wrist - self.prev_right_wrist) / dt
            self.left_punch_speed = 0.65 * self.left_punch_speed + 0.35 * v_l
            self.right_punch_speed = 0.65 * self.right_punch_speed + 0.35 * v_r
        else:
            self.left_punch_speed = 0.0
            self.right_punch_speed = 0.0

        self.prev_left_wrist = left_wrist
        self.prev_right_wrist = right_wrist

        left_reach = left_shoulder[2] - left_wrist[2]
        right_reach = right_shoulder[2] - right_wrist[2]

        PUNCH_THRESHOLD_SPEED = 1.8
        EXTEND_THRESHOLD = 0.32

        if self.left_punch_speed > PUNCH_THRESHOLD_SPEED and left_reach > EXTEND_THRESHOLD:
            self.left_punch_state = "punching"
            self.punch_type = "LEFT JAB / STRAIGHT"
        elif left_reach < 0.22:
            self.left_punch_state = "guard"

        if self.right_punch_speed > PUNCH_THRESHOLD_SPEED and right_reach > EXTEND_THRESHOLD:
            self.right_punch_state = "punching"
            self.punch_type = "RIGHT CROSS / POWER PUNCH"
        elif right_reach < 0.22:
            self.right_punch_state = "guard"

        if self.left_punch_state == "guard" and self.right_punch_state == "guard":
            self.punch_type = "GUARD STANCE"

    def release(self):
        self.cap_front.release()
        self.cap_side.release()
        print("[Tracker] Both cameras and trackers successfully shut down.")
