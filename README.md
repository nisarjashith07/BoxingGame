# AI Boxing Game using MediaPipe & Unity

## Project Overview

This project implements a real-time, AI-powered boxing environment utilizing MediaPipe Pose Tracking, Python, and Unity 6. The system captures the player's physical movements via a standard webcam and translates them into a 3D environment without requiring VR headsets or external controllers.

The long-term objective of this project is to create an adaptive AI opponent that dynamically learns the player's boxing style (such as punch frequency, favorite hand, and guard positioning) and adjusts its strategy during gameplay.

## Current Project Status

**Version 0.6: Real-Time Skeleton Visualization**

The core foundation for real-time tracking has been completed and is fully operational. The system is now ready for the integration of advanced boxing mechanics.

**Implemented Features:**
- MediaPipe Pose Tracking (Webcam integration)
- Python Tracking Engine (Landmark extraction)
- UDP Socket Communication (Low-latency data streaming)
- Unity Integration (Data ingestion and parsing)
- Real-Time Skeleton Visualization (3D rendering of 33 landmarks and connecting bones)

## Technology Stack

### Backend & Tracking
- **Language:** Python 3.11
- **Computer Vision:** OpenCV, MediaPipe Tasks API
- **Networking:** JSON, UDP Socket Programming

### Frontend & Visualization
- **Engine:** Unity 6 (URP 3D)
- **Language:** C#
- **Data Handling:** Newtonsoft JSON

## Project Architecture

1. **Webcam Capture:** Captures the live video feed of the player.
2. **MediaPipe Pose Landmarker:** Detects 33 anatomical landmarks in real-time.
3. **Python Tracking Engine:** Formats the extracted spatial data (X, Y, Z coordinates and visibility).
4. **JSON Serialization & UDP Sender:** Packages the data and transmits it locally over port `5055` at approximately 30 FPS.
5. **Unity UDP Receiver:** Listens for incoming data streams.
6. **Pose Manager:** Parses the JSON data and maps the real-world coordinates to the 3D environment.
7. **Skeleton Visualizer:** Dynamically updates the positions of 33 sphere landmarks and connecting cylinder bones to render a live, 1:1 representation of the player.

## Future Roadmap

- **Phase 7: Pose Processing**
  - Skeleton centering, scaling, and landmark smoothing algorithms to reduce jitter.
- **Phase 8: Boxing Mechanics**
  - Punch/guard detection, velocity, and direction calculations.
- **Phase 9: Humanoid Avatar Integration**
  - Replacing the foundational stickman with a fully rigged, real-time humanoid avatar.
- **Phase 10: Adaptive AI Opponent**
  - Machine learning implementation to analyze player habits (combos, speed) and adjust opponent strategy dynamically.
- **Phase 11: Multiplayer Synchronization**
  - Network-synced Player vs. Player mechanics.
- **Phase 12: Hardware Robotics Integration**
  - Translating Unity-simulated movements to physical robot controllers.

## Setup Instructions

This repository is self-contained and includes both the Python tracking server and the Unity project environment.

1. **Python Tracking Server:**
   - Navigate to the `Python/` directory.
   - Activate the virtual environment or install dependencies from `requirements.txt`.
   - Run `app.py` to launch the webcam tracking server.
2. **Unity Environment:**
   - Open the root directory in Unity Hub (this folder contains the full Unity project, including `Assets`, `ProjectSettings`, and `Packages`).
   - Open the main scene and enter Play Mode to begin receiving UDP packets and rendering the skeleton.
