# AI Boxing Game — MediaPipe & Unity 6

[![Unity](https://img.shields.io/badge/Unity-6000.4.0f1-black.svg?style=flat&logo=unity)](https://unity.com/)
[![Python](https://img.shields.io/badge/Python-3.11-blue.svg?style=flat&logo=python)](https://python.org/)
[![MediaPipe](https://img.shields.io/badge/MediaPipe-Pose%20Tracking-teal.svg?style=flat)](https://developers.google.com/mediapipe)
[![Platform](https://img.shields.io/badge/Platform-Windows-0078D6.svg?style=flat&logo=windows)](https://microsoft.com)

A real-time, AI-driven 3D boxing game powered by **MediaPipe Pose Tracking**, **Python**, and **Unity 6 (Universal Render Pipeline)**. 

Play by throwing real punches and holding up your hands in front of your webcam, or play using standard keyboard and mouse controls with dedicated boxing footwork and combat mechanics.

---

## Table of Contents
- [Key Features](#key-features)
- [How to Play & Controls](#how-to-play--controls)
  - [Keyboard & Mouse Controls](#keyboard--mouse-controls)
  - [Webcam Motion Tracking](#webcam-motion-tracking)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Installation & Getting Started](#installation--getting-started)
  - [1. Unity Setup (Game Client)](#1-unity-setup-game-client)
  - [2. Python Setup (Webcam Pose Server)](#2-python-setup-webcam-pose-server)
- [Combat Mechanics & Game Systems](#combat-mechanics--game-systems)
  - [Player Footwork & Movement](#player-footwork--movement)
  - [Punch & Guard Detection](#punch--guard-detection)
  - [AI Opponent Brain](#ai-opponent-brain)
  - [Combat HUD & Visual Feedback](#combat-hud--visual-feedback)
- [Troubleshooting & FAQ](#troubleshooting--faq)

---

## Key Features

- **Webcam Motion Tracking (No VR Required)**: Uses MediaPipe's 33-point BlazePose model at 30+ FPS to track shoulders, elbows, wrists, and guard position in real time.
- **Dedicated Boxing Footwork**: Lock-on boxer movement system. Move forward to step into the pocket, retreat to slip back, and circle/strafe around the opponent while maintaining stance.
- **Adaptive AI Boxer**: Multi-tier AI opponent (Easy, Medium, Hard) driven by a Utility/FSM decision engine that reads player distance, attack intent, and stamina to trade punches, slip, and counter.
- **Full Punch Arsenal**: Left Jabs, Right Crosses, Left Hooks, Right Hooks, and High Guard Defense with procedural inverse kinematics (IK) bone animation.
- **Production Combat HUD**:
  - Live numeric hitpoint displays (`100 / 100 HP`) on top health bars.
  - Overhead 3D health bar directly pinned above the AI opponent in world/screen space.
  - 3D floating damage popups (`-14 CROSS!`, `-8 JAB!`, `BLOCKED -2`) that pop off impact locations.
  - Dynamic hit banner and match result overlays (Knockout / Victory / Defeat).
- **Procedural Dynamic Audio**: Synthesized impact thuds and glove block audio generated at runtime via `AudioClip.Create` without external audio asset dependencies.
- **Zero-Setup Bootstrapper**: Automated scene initialization (`CombatBootstrapper`) that configures the opponent, player controller, UI canvas, and combat environment on Play Mode.

---

## How to Play & Controls

The game can be played immediately with **Keyboard & Mouse** or with full **Webcam Pose Tracking**.

### Keyboard & Mouse Controls

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

### Webcam Motion Tracking

When running `Python/app.py`, stand in view of your webcam:
- **Jabs & Crosses**: Extend your left or right fist toward the camera with speed.
- **Hooks**: Swing your arm horizontally across your torso.
- **High Guard**: Bring both hands up to cover your chin and face.

---

## Architecture

```text
┌────────────────────────────────────────────────────────────────────────┐
│                        WEBCAM MOTION PIPELINE                          │
│                                                                        │
│  Webcam (30+ FPS)                                                      │
│    └─→ Python MediaPipe Pose Tracker (33 Landmarks)                    │
│          └─→ UDP JSON Stream (localhost:5055)                          │
│                └─→ Unity UDPReceiver (Background Worker Thread)        │
│                      └─→ PoseManager.cs (Dispatches Main Thread Data)  │
└────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌────────────────────────────────────────────────────────────────────────┐
│                         COMBAT & MATCH PIPELINE                        │
│                                                                        │
│  Player Input / Pose                                                   │
│    ├─→ BoxingPlayerController.cs (Lock-On WASD Footwork)               │
│    └─→ PunchDetector.cs (Velocity, Extension & Guard Detection)        │
│          │                                                             │
│          ▼                                                             │
│  BoxingEnvironment.cs (Match Engine & Hit Registration)                │
│    ├─→ AIOpponentBrain.cs & AIFighterController.cs (AI Combat Loop)   │
│    ├─→ FighterState.cs (Health, Stamina & Defense Calculations)        │
│    └─→ CombatHUD.cs (Screen HUD, 3D Overhead Bar, Floating Numbers)    │
└────────────────────────────────────────────────────────────────────────┘
```

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
│   │   │   ├── BoxingAction.cs          # 12-action combat enumeration
│   │   │   ├── BoxingObservation.cs     # 17-field environmental observation struct
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
│   │   │   └── PoseData.cs              # Full 33-landmark pose frame payload
│   │   │
│   │   ├── Managers/                    # Game Loop Managers
│   │   │   ├── PoseManager.cs           # UDP packet parsing & dispatch
│   │   │   └── MatchTimer.cs            # 3-minute round clock
│   │   │
│   │   ├── Networking/                  # Networking
│   │   │   └── UDPReceiver.cs           # Threaded UDP socket receiver (Port 5055)
│   │   │
│   │   ├── Visualization/              # 3D Avatar & Skeleton
│   │   │   ├── AvatarController.cs      # Humanoid IK arm positioning
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
│   ├── app.py                           # Webcam tracking entry point
│   ├── config.py                        # Camera indices, resolution & UDP port
│   ├── requirements.txt                 # Python dependencies
│   ├── data/
│   │   └── pose_data.py                 # Dataclasses matching C# contract
│   ├── models/
│   │   └── pose_landmarker_full.task    # Google MediaPipe Pose Landmarker model
│   ├── networking/
│   │   └── udp_sender.py               # JSON socket streamer over UDP
│   └── tracking/
│       ├── tracker.py                   # MediaPipe pose extraction pipeline
│       └── visualizer.py               # Debug OpenCV frame overlay
│
└── README.md
```

---

## Installation & Getting Started

### 1. Unity Setup (Game Client)

1. Open **Unity Hub**.
2. Click **Add** and select the `BoxingGame/` project folder.
3. Open the project with **Unity 6 (6000.4.0f1 or newer)**.
4. Open `Assets/Scenes/SampleScene.unity` or `Assets/Scenes/BoxingMatch_UI.unity`.
5. Press **Play ▶**.
   - `CombatBootstrapper` automatically configures the opponent, wires player controls, and generates the complete HUD.
   - You can immediately fight using **WASD** and **Mouse / J, K, H, L**.

### 2. Python Setup (Webcam Pose Server)

To play using your webcam and physical motion:

1. Open a terminal in the `Python/` directory:
   ```bash
   cd Python
   ```
2. Create and activate a virtual environment:
   ```bash
   python -m venv venv
   # On Windows:
   venv\Scripts\activate
   # On Linux/macOS:
   source venv/bin/activate
   ```
3. Install required packages:
   ```bash
   pip install -r requirements.txt
   ```
4. Start the webcam pose tracker:
   ```bash
   python app.py
   ```
5. With `app.py` running in the background, enter Play Mode in Unity. Your physical movements will stream directly to your avatar over UDP port `5055`.

---

## Combat Mechanics & Game Systems

### Player Footwork & Movement
Unlike generic third-person controllers that spin 180° away when walking backwards, `BoxingPlayerController`:
- Always faces the opponent (lock-on boxing stance).
- Employs tight footwork speeds (advance: 1.8 m/s, retreat: 1.4 m/s, strafe: 1.5 m/s).
- Uses distance clamping (< 1.15m) to prevent clipping through or walking past the opponent.
- Locks locomotion animation to a walking blend tree, avoiding running animations.

### Punch & Guard Detection
- **Punch Detection**: Evaluates wrist velocity (> 0.8 units/s) and arm extension (> 1.15x resting arm length) to identify straight punches (jabs/crosses) vs. lateral swings (hooks).
- **High Guard**: Evaluates the distance between the wrists and the chin/nose. When hands are raised to face level, guard level transitions to 1.0 (75% damage reduction).
- **Punch Window**: Punches remain active for a 0.20s impact window, then cleanly reset to allow rapid combinations.

### AI Opponent Brain
- **Stance & Pocket Management**: Within attack range (< 1.65m), the AI plants its feet, trades punches, blocks incoming strikes, and counters recovery windows.
- **Difficulty Tiers**:
  - **Easy**: Lower aggression (0.65), generous reaction windows, single punches.
  - **Medium**: Balanced aggression (0.80), 2-3 punch combinations, reactive blocking.
  - **Hard**: Relentless pressure (0.90), rapid combinations, precise anticipation, and active slipping.

### Combat HUD & Visual Feedback
- **Live Numeric HP**: Top player and opponent bars feature bold `100 / 100 HP` numeric readouts.
- **Overhead 3D Bar**: A floating health bar hovers right over the opponent's head in world space.
- **3D Floating Damage Numbers**: Pop off the point of impact on every punch (`-14 CROSS!`, `-8 JAB!`, `BLOCKED -2`).
- **Hit Flash**: The opponent flashes white/red for 0.12s on hit impact.

---

## Troubleshooting & FAQ

- **Punches aren't registering with keyboard**: Click inside the Unity Game view window to ensure the window has focus. Cursor lock will engage. Press `Escape` to toggle cursor lock.
- **Python server fails to connect**: Ensure port `5055` is not blocked by Windows Firewall. Both Unity and Python stream locally on `127.0.0.1:5055`.
- **Webcam not detected**: Check `Python/config.py` and adjust `CAMERA_INDEX` (default is `0` for the primary webcam).
- **Unity compilation**: Run `dotnet build Assembly-CSharp.csproj` from the project root to verify C# build status.

---

## License

This project is licensed under the MIT License. StarterAssets and character models are subject to the Unity Companion License.
