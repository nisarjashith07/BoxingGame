# AI Boxing Game — Dual-Camera 3D MoCap & Combat System (Unity 6)

[![Unity](https://img.shields.io/badge/Unity-6000.4.0f1-black.svg?style=flat&logo=unity)](https://unity.com/)
[![Python](https://img.shields.io/badge/Python-3.11%20%7C%203.14-blue.svg?style=flat&logo=python)](https://python.org/)
[![MediaPipe](https://img.shields.io/badge/MediaPipe-Dual%20Camera%203D-teal.svg?style=flat)](https://developers.google.com/mediapipe)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6.svg?style=flat&logo=windows)](https://microsoft.com)

A real-time, AI-driven 3D boxing game powered by **Dual-Camera 3D Pose Tracking** (MediaPipe Tasks API), **Python**, and **Unity 6 (Universal Render Pipeline)**. 

Play by throwing real punches and slipping in front of your dual-camera setup (PC webcam + phone on your right), or play using keyboard and mouse controls with dedicated boxing footwork and combat mechanics.

---

## Dual-Camera Architecture & 3D Sensor Fusion

```text
  [Front Camera: PC Webcam]                    [Side Camera: Phone on Right]
       (Facing Player)                              (Facing Player's Right)
              │                                                │
       Captures: X (Lateral)                            Captures: Z (Forward Punch Depth)
                 Y (Vertical)                                     Y (Vertical Height)
              └─────────────────────┬──────────────────────────┘
                                    ▼
                     [DualPoseTracker (Python)]
                     - Multi-threaded zero-latency capture
                     - 1€ Filter (One Euro Filter: 0-latency, 0-jitter)
                     - Rigid Anatomical Bone Length Constraints
                     - Real-time Punch Speed & State Analytics
                                    │
                            UDP Socket (5055)
                                    ▼
                      [Unity 6 AvatarController]
                     - Full 3D Humanoid IK Rig
                     - Forward Punch Reach Multiplier & Reach Clamping
                     - Velocity-Adaptive Dynamic Smoothing
                     - Torso Dodging (Bob, Weave, and Slip)
                                    │
                                    ▼
                      [Combat & Match System]
                     - BoxingPlayerController.cs (Lock-On WASD Footwork)
                     - PunchDetector.cs & BoxingEnvironment.cs
                     - AIOpponentBrain.cs & CombatHUD.cs
```

### Why Dual Camera?
- **Single Camera Limitation:** When punching directly towards a front webcam, the fist foreshortens. MediaPipe's monocular depth heuristic compresses or jitters, causing punches to under-extend or snap back.
- **Dual Camera Advantage:** From the side camera (positioned on the player's right), a forward punch moves horizontally across the camera's sensor. This transforms depth estimation into a direct 2D optical measurement, providing high-accuracy punch extension and full arm lockout!

---

## Key Features

- **Dual-Camera 3D Motion Tracking (No VR Required)**:
  - Fuses PC front webcam (lateral/vertical) and phone side camera (punch reach/depth).
  - 1€ Filter (One Euro Filter) eliminates micro-jitter in guard stance while providing fast, responsive punch dynamics.
  - Inter-camera capture timestamping with automatic synchronization skew detection and graceful single-camera fallback.
  - Rigid anatomical bone constraints prevent rubber-arm distortion.
  - Interactive 1-click calibration (`Press [C]`) to match your exact arm reach.
  - Event booth optimization: `--lite` flag for lower-spec fallback laptops.
- **Dedicated Boxing Footwork**: Lock-on boxer movement system. Move forward to step into the pocket, retreat to slip back, and circle/strafe around the opponent while maintaining stance.
- **Adaptive AI Boxer**: Multi-tier AI opponent (Easy, Medium, Hard) driven by a Utility/FSM decision engine that reads player distance, attack intent, and stamina to trade punches, slip, and counter.
- **Full Punch Arsenal**: Left Jabs, Right Crosses, Left Hooks, Right Hooks, and High Guard Defense with procedural inverse kinematics (IK) bone animation.
- **Production Combat HUD**:
  - Live numeric hitpoint displays (`100 / 100 HP`) on top health bars.
  - Overhead 3D health bar directly pinned above the AI opponent in world/screen space.
  - 3D floating damage popups (`-14 CROSS!`, `-8 JAB!`, `BLOCKED -2`) that pop off impact locations.
  - Dynamic hit banner and match result overlays (Knockout / Victory / Defeat).
- **Match Timer**: 3-minute round clock with round control and match management.
- **Procedural Dynamic Audio**: Synthesized impact thuds and glove block audio generated at runtime via `AudioClip.Create` without external audio asset dependencies.
- **Zero-Setup Bootstrapper**: Automated scene initialization (`CombatBootstrapper`) that configures the opponent, player controller, UI canvas, and combat environment on Play Mode.

---

## How to Play & Controls

### Option A: Dual-Camera Motion Tracking (Real Punches)
1. Position your PC webcam in front of you.
2. Position your phone camera to your **right side** at $\approx 90^\circ$, facing your right profile.
3. Launch `app.py`:
   ```bash
   .\Python\venv\Scripts\python.exe Python\app.py
   ```
4. **Hotkeys in Tracking Window:**
   - **`[C]`**: Run 2.5s Calibration while holding your boxing guard stance.
   - **`[M]`**: Toggle horizontal mirror for the side camera (for front selfie cameras).
   - **`[R]`**: Rotate side camera feed by 90° (for portrait mode mounting).
   - **`[Q]` / `[ESC]`**: Quit cleanly.

### Option B: Keyboard & Mouse Controls

| Key / Input | Action | In-Game Behavior |
|---|---|---|
| **`W`** / **`↑`** | **Step In** | Advance forward into punching range |
| **`S`** / **`↓`** | **Step Back** | Retreat back while staying locked onto opponent |
| **`A`** / **`←`** | **Circle Left** | Strafe / circle counter-clockwise around opponent |
| **`D`** / **`→`** | **Circle Right** | Strafe / circle clockwise around opponent |
| **Left Click** / **`J`** | **Left Jab** | Fast straight punch with left glove (-8 HP) |
| **Right Click** / **`K`** | **Right Cross** | Power straight punch with right glove (-14 HP) |
| **`H`** | **Left Hook** | Power lateral left hook (-18 HP) |
| **`L`** | **Right Hook** | Power lateral right hook (-18 HP) |
| **`Space`** / **`B`** *(Hold)* | **High Guard** | Raises gloves to chin; reduces incoming damage by 75% |
| **`Escape`** | **Cursor Lock** | Toggle mouse cursor lock / release |

---

## Project Structure

```text
BoxingGame/
├── Assets/
│   ├── Scenes/
│   │   ├── SampleScene.unity            # Default development & testing scene
│   │   └── BoxingMatch_UI.unity         # Pre-configured boxing match scene
│   │
│   ├── Scripts/
│   │   ├── AI/                          # AI Decision & Behavior System
│   │   │   ├── BoxingAction.cs          # Combat action enumeration
│   │   │   ├── BoxingObservation.cs     # Environmental observation struct
│   │   │   ├── AIOpponentProfile.cs     # ScriptableObject difficulty profiles
│   │   │   ├── OpponentPatternMemory.cs # Adaptive opponent memory & counters
│   │   │   ├── AIOpponentBrain.cs       # Utility/FSM decision engine
│   │   │   └── AIOpponentController.cs  # AI MonoBehaviour interface
│   │   │
│   │   ├── Combat/                      # Core Combat Mechanics
│   │   │   ├── BoxingPlayerController.cs# Lock-on boxing footwork & input
│   │   │   ├── AIFighterController.cs   # AI character movement & humanoid IK
│   │   │   ├── PunchDetector.cs         # Punch speed, extension, and guard logic
│   │   │   ├── FighterState.cs          # Health, stamina, and defense states
│   │   │   ├── CombatSettings.cs        # Tunable damage, speeds, and ranges
│   │   │   ├── ObservationBuilder.cs    # Builds AI observation from live state
│   │   │   ├── BoxingEnvironment.cs     # Match loop, hits, and damage flow
│   │   │   ├── CombatHUD.cs             # Self-generating HUD, HP & floating text
│   │   │   └── CombatBootstrapper.cs    # Zero-setup match initializer
│   │   │
│   │   ├── Data/                        # Data Contracts
│   │   │   ├── Landmark.cs              # Individual landmark coordinates & visibility
│   │   │   └── PoseData.cs              # 33-landmark payload + punch telemetry
│   │   │
│   │   ├── Managers/                    # Game Loop Managers
│   │   │   ├── PoseManager.cs           # UDP packet parsing & dispatch
│   │   │   └── MatchTimer.cs            # 3-minute round clock
│   │   │
│   │   ├── Networking/                  # Networking
│   │   │   └── UDPReceiver.cs           # Threaded UDP socket receiver (Port 5055)
│   │   │
│   │   ├── Visualization/               # 3D Avatar & Skeleton
│   │   │   ├── AvatarController.cs      # Humanoid IK arm positioning & torso dodge
│   │   │   ├── SkeletonVisualizer.cs    # 33-point debug landmark visualizer
│   │   │   └── Bone.cs                  # Procedural bone cylinder connector
│   │   │
│   │   └── Editor/                      # Unity Editor Tools
│   │       └── BoxingMatchSetup.cs      # Menu item: Tools → Boxing Game → Setup
│   │
│   ├── Arena/                           # 3D Boxing Ring Models & Ropes
│   └── StarterAssets/                   # Character rigs and animation blend trees
│
├── Python/
│   ├── app.py                           # Dual camera tracking entry point
│   ├── config.py                        # Dual camera indices, resolution & fusion settings
│   ├── requirements.txt                 # Python dependencies
│   ├── data/
│   │   └── pose_data.py                 # Dataclasses matching C# contract
│   ├── models/
│   │   └── pose_landmarker_full.task    # Google MediaPipe Pose Landmarker model
│   ├── networking/
│   │   └── udp_sender.py                # JSON socket streamer over UDP
│   └── tracking/
│       ├── dual_tracker.py              # 3D multi-view stereoscopic fusion & punch analytics
│       ├── threaded_camera.py           # Zero-latency threaded frame grabber
│       ├── one_euro_filter.py           # One-Euro Filter (0 latency, 0 jitter)
│       ├── tracker.py                   # Backward-compatible tracker export
│       └── visualizer.py                # Real-time telemetry dashboard & calibration UI
│
└── README.md
```

---

## License

This project is licensed under the MIT License. StarterAssets and character models are subject to the Unity Companion License.
