# AI Boxing Game using Dual-Camera MediaPipe & Unity 6

## Project Overview

This project implements a real-time, AI-powered boxing environment utilizing **Dual-Camera 3D Pose Tracking** (MediaPipe Tasks API), Python, and Unity 6. The system captures the player's physical boxing movements via a dual-camera optical setup:
1. **Front Camera (PC Webcam):** Captures lateral ($X$) and vertical ($Y$) motion with zero depth ambiguity.
2. **Side Camera (Phone Camera on Right):** Directly measures forward punch extension and reach depth ($Z$) along the sensor's horizontal optical axis.

By combining both viewpoints through multi-view 3D sensor fusion, the system eliminates monocular depth ambiguity and delivers ultra-accurate forward punch extension (jabs, crosses), guard stances, and torso slipping/ducking to the 3D Unity avatar in real-time.

---

## Dual-Camera Architecture & Sensor Fusion

```
  [Front Camera: PC Webcam]                    [Side Camera: Phone on Right]
       (Facing Player)                              (Facing Player's Right)
              │                                                │
       Captures: X (Lateral)                            Captures: Z (Forward Punch Depth)
                 Y (Vertical)                                     Y (Vertical Height)
              └─────────────────────┬──────────────────────────┘
                                    ▼
                     [DualPoseTracker (Python)]
                     - Multi-threaded zero-latency capture
                     - MediaPipe PoseLandmarker (Dual)
                     - Optical 3D Sensor Fusion
                     - Real-time Punch Speed & State Analytics
                                    │
                            UDP Socket (5055)
                                    ▼
                      [Unity 6 AvatarController]
                     - Full 3D Humanoid IK Rig
                     - Forward Punch Reach Multiplier
                     - Velocity-Adaptive Motion Smoothing
                     - Torso Slipping & Ducking (Dodging)
```

### Why Dual Camera?
- **Single Camera Limitation:** When punching directly towards a front webcam, the fist foreshortens. MediaPipe's monocular depth heuristic compresses or jitters, causing punches to under-extend or snap back.
- **Dual Camera Advantage:** From the side camera (positioned on the player's right), a forward punch moves horizontally across the camera's sensor. This transforms depth estimation into a direct 2D optical measurement, providing millimeter-accurate punch extension and full arm lockout!

---

## Technology Stack

- **Python:** 3.11 / 3.14 (MediaPipe Tasks, OpenCV, NumPy)
- **Unity:** Unity 6 (URP, Humanoid Animation Rigging & IK)
- **Networking:** High-throughput JSON UDP streaming on port `5055`

---

## Quickstart Guide

### 1. Hardware Placement
1. **Front Camera (Index 0):** Place your PC webcam directly in front of you at approximately chest/eye height (1.5m - 2.5m away).
2. **Side Camera (Index 1):** Place your smartphone on your **right side** at a 90-degree angle, facing your right profile.
   - *Connection Options for Phone:* DroidCam, Iriun Webcam, Camo (USB / WiFi), or IP Webcam RTSP/HTTP URL.

### 2. Launch the Python Tracking Pipeline
Open a terminal in the project directory:
```bash
.\Python\venv\Scripts\python.exe Python\app.py
```
Or if using your global Python environment:
```bash
cd Python
python app.py
```

### 3. Interactive Hotkeys
- **`Q` / `ESC`:** Quit pipeline cleanly.
- **`M`:** Toggle horizontal mirror for the side camera (useful if using phone's front-facing selfie camera).
- **`R`:** Rotate side camera feed by 90° (useful if phone is mounted in portrait mode).

### 4. Run Unity
1. Open the project in **Unity 6**.
2. Open `Assets/Scenes/BoxingMatch_UI.unity` (or `SampleScene.unity`).
3. Select the player character in the Hierarchy (`PlayerArmature`).
4. In the Inspector on `AvatarController`:
   - **Tracking Source:** `FusedDualCamera` (Default, uses 3D fused Front+Side coordinates).
   - **Punch Reach Multiplier:** `1.15` (Adjust to match your arm reach).
   - **Enable Torso Dodge:** `Checked` (Allows avatar to slip and duck under incoming punches).
5. Click **Play**! Throw jabs, crosses, hooks, and slip to test the tracking!

---

## Project Structure

```
humanoid/
├── Assets/
│   └── Scripts/
│       ├── Data/
│       │   ├── Landmark.cs        # 3D spatial coordinate with visibility
│       │   └── PoseData.cs        # Fused landmarks + boxing punch metrics
│       ├── Networking/
│       │   └── UDPReciever.cs     # Background thread UDP listener (Port 5055)
│       ├── Visualization/
│       │   ├── AvatarController.cs # Humanoid IK, punch extension, torso dodge
│       │   └── SkeletonVisualizer.cs # 3D sphere-and-bone debug skeleton
│       └── Managers/
│           └── PoseManager.cs     # Coordinates UDP data to Avatar and Visualizer
├── Python/
│   ├── app.py                     # Main tracking dashboard runner
│   ├── config.py                  # Dual-camera and fusion configuration
│   ├── tracking/
│   │   ├── dual_tracker.py        # 3D multi-view sensor fusion & punch analytics
│   │   ├── threaded_camera.py     # Non-blocking threaded camera grabber
│   │   └── visualizer.py          # Dual camera dashboard with skeleton overlay & HUD
│   ├── networking/
│   │   └── udp_sender.py          # High-rate UDP streamer
│   └── data/
│       └── pose_data.py           # Python dataclasses & JSON serializer
└── README.md
```
