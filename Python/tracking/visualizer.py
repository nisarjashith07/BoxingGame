import cv2
import numpy as np
import time


class DualPoseVisualizer:
    """
    Rich OpenCV visualization dashboard for Dual-Camera VR Boxing.
    Renders synchronized side-by-side camera feeds with 2D skeleton overlays,
    depth projection vectors, punch velocity gauges, calibration progress, and UDP streaming status.
    """

    # MediaPipe limb connection pairs (indices 0..32)
    SKELETON_CONNECTIONS = [
        # Upper Body
        (11, 12),  # Left Shoulder to Right Shoulder
        (11, 13),  # Left Shoulder to Left Elbow
        (13, 15),  # Left Elbow to Left Wrist
        (12, 14),  # Right Shoulder to Right Elbow
        (14, 16),  # Right Elbow to Right Wrist
        # Hands
        (15, 17), (15, 19), (15, 21),  # Left Hand knuckles
        (16, 18), (16, 20), (16, 22),  # Right Hand knuckles
        # Torso
        (11, 23),  # Left Shoulder to Left Hip
        (12, 24),  # Right Shoulder to Right Hip
        (23, 24),  # Left Hip to Right Hip
        # Head
        (0, 1), (1, 2), (2, 3), (3, 7),  # Left face
        (0, 4), (4, 5), (5, 6), (6, 8),  # Right face
    ]

    def __init__(self):
        self.fps_timer = time.time()
        self.fps_counter = 0
        self.current_fps = 0.0

    def draw_skeleton_2d(self, frame, landmarks_2d, is_side=False):
        """Draws clean, antialiased skeleton bones and colored joints."""
        if landmarks_2d is None:
            return frame

        h, w = frame.shape[:2]
        pts = {}

        for i, lm in enumerate(landmarks_2d):
            vis = getattr(lm, 'visibility', 1.0)
            if vis is not None and vis > 0.25:
                px = int(lm.x * w)
                py = int(lm.y * h)
                pts[i] = (px, py)

        bone_color = (255, 180, 50) if is_side else (50, 220, 255)
        accent_color = (0, 120, 255) if is_side else (0, 255, 120)

        # Draw bones
        for start_idx, end_idx in self.SKELETON_CONNECTIONS:
            if start_idx in pts and end_idx in pts:
                p1 = pts[start_idx]
                p2 = pts[end_idx]
                if (start_idx in (11, 13, 15) and end_idx in (13, 15, 17, 19)):
                    color = (255, 80, 80) if not is_side else (100, 200, 255)
                    thickness = 3
                elif (start_idx in (12, 14, 16) and end_idx in (14, 16, 18, 20)):
                    color = (80, 100, 255) if not is_side else (80, 240, 255)
                    thickness = 3
                else:
                    color = bone_color
                    thickness = 2
                cv2.line(frame, p1, p2, color, thickness, cv2.LINE_AA)

        # Draw joints
        for idx, pt in pts.items():
            if idx in (15, 16):  # Wrists / Boxing Gloves
                cv2.circle(frame, pt, 8, (0, 0, 255), -1, cv2.LINE_AA)
                cv2.circle(frame, pt, 10, (255, 255, 255), 2, cv2.LINE_AA)
            elif idx in (11, 12, 13, 14):  # Shoulders / Elbows
                cv2.circle(frame, pt, 5, accent_color, -1, cv2.LINE_AA)
            else:
                cv2.circle(frame, pt, 3, (255, 255, 255), -1, cv2.LINE_AA)

        return frame

    def render_dashboard(self, front_frame, side_frame, pose_data, tracker):
        """
        Assembles side-by-side feeds, adds upper HUD and lower telemetry stats.
        """
        # Calculate FPS
        self.fps_counter += 1
        now = time.time()
        if now - self.fps_timer >= 1.0:
            self.current_fps = self.fps_counter / (now - self.fps_timer)
            self.fps_counter = 0
            self.fps_timer = now

        # Standardize heights
        target_h = 540
        f_h, f_w = front_frame.shape[:2]
        s_h, s_w = side_frame.shape[:2]

        new_f_w = int(f_w * (target_h / max(f_h, 1)))
        new_s_w = int(s_w * (target_h / max(s_h, 1)))

        f_resized = cv2.resize(front_frame, (new_f_w, target_h))
        s_resized = cv2.resize(side_frame, (new_s_w, target_h))

        # Overlay 2D skeletons
        if tracker.latest_2d_front is not None:
            f_resized = self.draw_skeleton_2d(f_resized, tracker.latest_2d_front, is_side=False)
        if tracker.latest_2d_side is not None:
            s_resized = self.draw_skeleton_2d(s_resized, tracker.latest_2d_side, is_side=True)

        # Labels on individual camera viewports
        self._draw_cam_badge(f_resized, "FRONT CAMERA (PC Webcam)", "TRACKING LATERAL + VERTICAL", (0, 220, 100))
        side_sub = "OPTICAL DEPTH PUNCH REACH" if tracker.side_on_right else "SIDE CAMERA (LEFT)"
        self._draw_cam_badge(s_resized, "SIDE CAMERA (Phone on Right)", side_sub, (0, 165, 255))

        # Combine side-by-side
        combined = cv2.hconcat([f_resized, s_resized])
        total_w = combined.shape[1]

        # Draw Calibration Progress Modal if active
        cal_prog = tracker.get_calibration_progress()
        if cal_prog is not None:
            self._draw_calibration_modal(combined, cal_prog)

        # Add Header bar (60px) and Footer bar (110px)
        header = np.zeros((60, total_w, 3), dtype=np.uint8)
        footer = np.zeros((110, total_w, 3), dtype=np.uint8)

        # --- HEADER RENDERING ---
        cv2.putText(header, "VR BOXING DUAL-CAM AI TRACKER", (20, 38), cv2.FONT_HERSHEY_DUPLEX, 0.85, (255, 255, 255), 2, cv2.LINE_AA)
        
        has_dual = (tracker.latest_2d_front is not None) and (tracker.latest_2d_side is not None)
        status_text = "● 3D DUAL-CAM FUSION ACTIVE" if has_dual else "○ SINGLE CAM FALLBACK"
        status_col = (0, 255, 120) if has_dual else (0, 180, 255)
        cv2.putText(header, status_text, (total_w - 560, 36), cv2.FONT_HERSHEY_SIMPLEX, 0.65, status_col, 2, cv2.LINE_AA)

        fps_str = f"{self.current_fps:.1f} FPS"
        cv2.putText(header, f"UDP: 5055 | {fps_str}", (total_w - 210, 36), cv2.FONT_HERSHEY_SIMPLEX, 0.60, (200, 200, 200), 1, cv2.LINE_AA)
        cv2.line(header, (0, 59), (total_w, 59), (50, 50, 50), 1)

        # --- FOOTER RENDERING ---
        if pose_data is not None:
            l_spd = pose_data.left_punch_speed
            r_spd = pose_data.right_punch_speed

            self._draw_speed_gauge(footer, 30, 25, "LEFT HAND (JAB)", l_spd, (255, 100, 80))
            self._draw_speed_gauge(footer, 380, 25, "RIGHT HAND (CROSS)", r_spd, (80, 150, 255))

            # Action banner
            p_type = pose_data.punch_type
            banner_col = (0, 255, 255) if "PUNCH" in p_type or "JAB" in p_type or "CROSS" in p_type else (150, 150, 150)
            cv2.putText(footer, f"ACTION: {p_type}", (740, 42), cv2.FONT_HERSHEY_DUPLEX, 0.70, banner_col, 2, cv2.LINE_AA)

            # Anatomical Reach Telemetry
            reach_info = f"Calibrated Arm Reach: {tracker.upper_arm_length + tracker.forearm_length:.2f}m | Shoulder: {tracker.shoulder_width:.2f}m"
            cv2.putText(footer, reach_info, (740, 68), cv2.FONT_HERSHEY_SIMPLEX, 0.46, (170, 170, 170), 1, cv2.LINE_AA)

        # Hotkeys hint bar at bottom of footer
        hint_text = "[C] Calibrate Guard Stance   |   [M] Flip Side Mirror   |   [R] Rotate Side 90 deg   |   [Q] Quit"
        cv2.putText(footer, hint_text, (20, 95), cv2.FONT_HERSHEY_SIMPLEX, 0.50, (0, 255, 200), 1, cv2.LINE_AA)
        cv2.line(footer, (0, 0), (total_w, 0), (50, 50, 50), 1)

        full_dashboard = cv2.vconcat([header, combined, footer])
        return full_dashboard

    def _draw_calibration_modal(self, img, progress):
        h, w = img.shape[:2]
        mw, mh = 500, 120
        x1 = (w - mw) // 2
        y1 = (h - mh) // 2
        x2 = x1 + mw
        y2 = y1 + mh

        overlay = img.copy()
        cv2.rectangle(overlay, (x1, y1), (x2, y2), (10, 10, 10), -1)
        cv2.addWeighted(overlay, 0.85, img, 0.15, 0, img)
        cv2.rectangle(img, (x1, y1), (x2, y2), (0, 220, 255), 2)

        pct = int(progress * 100)
        cv2.putText(img, f"CALIBRATING GUARD STANCE... {pct}%", (x1 + 35, y1 + 45), cv2.FONT_HERSHEY_DUPLEX, 0.70, (255, 255, 255), 2, cv2.LINE_AA)
        cv2.putText(img, "Hold your resting boxing guard facing front camera", (x1 + 35, y1 + 70), cv2.FONT_HERSHEY_SIMPLEX, 0.45, (0, 220, 255), 1, cv2.LINE_AA)

        # Progress bar
        bar_x = x1 + 35
        bar_y = y1 + 85
        bar_w = mw - 70
        bar_h = 12
        cv2.rectangle(img, (bar_x, bar_y), (bar_x + bar_w, bar_y + bar_h), (50, 50, 50), -1)
        fill_w = int(progress * bar_w)
        cv2.rectangle(img, (bar_x, bar_y), (bar_x + fill_w, bar_y + bar_h), (0, 220, 255), -1)

    def _draw_cam_badge(self, img, title, subtitle, color):
        overlay = img.copy()
        cv2.rectangle(overlay, (15, 15), (340, 70), (20, 20, 20), -1)
        cv2.addWeighted(overlay, 0.7, img, 0.3, 0, img)
        cv2.rectangle(img, (15, 15), (340, 70), color, 2)
        cv2.putText(img, title, (25, 40), cv2.FONT_HERSHEY_SIMPLEX, 0.60, (255, 255, 255), 2, cv2.LINE_AA)
        cv2.putText(img, subtitle, (25, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.42, color, 1, cv2.LINE_AA)

    def _draw_speed_gauge(self, img, x, y, label, speed, color):
        cv2.putText(img, f"{label}: {speed:.1f} m/s", (x, y), cv2.FONT_HERSHEY_SIMPLEX, 0.50, (220, 220, 220), 1, cv2.LINE_AA)
        bar_w = 260
        bar_h = 14
        bar_y = y + 8
        cv2.rectangle(img, (x, bar_y), (x + bar_w, bar_y + bar_h), (40, 40, 40), -1)
        fill_w = int(min(speed / 8.0, 1.0) * bar_w)
        if fill_w > 0:
            fill_col = (0, 255, 0) if speed < 3.0 else ((0, 200, 255) if speed < 5.0 else (0, 0, 255))
            cv2.rectangle(img, (x, bar_y), (x + fill_w, bar_y + bar_h), fill_col, -1)
        cv2.rectangle(img, (x, bar_y), (x + bar_w, bar_y + bar_h), (80, 80, 80), 1)


# Legacy visualizer wrapper
class PoseVisualizer:
    def __init__(self):
        self.dashboard = DualPoseVisualizer()

    def draw(self, frame, pose):
        return frame
