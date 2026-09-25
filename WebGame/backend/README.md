# Corner Boxing backend and RL SDK

The backend hosts the 3D browser game at `http://127.0.0.1:8000`, exposes the opponent action endpoint, and provides a Gymnasium environment for training policies. The browser uses Three.js and MediaPipe Tasks Vision from jsDelivr, plus Google's hosted pose model. Keep an internet connection available on first load. No model is required: the server starts with a utility opponent and loads PPO weights when `BOXING_MODEL` points to a saved model.

## Run the browser game

```bash
cd WebGame/backend
python -m venv .venv
# Windows: .venv\Scripts\activate
# macOS/Linux: source .venv/bin/activate
pip install -r requirements.txt
uvicorn server:app --reload
```

Open `http://127.0.0.1:8000` and choose **ENABLE MOTION** to grant webcam access. Use a modern browser and allow camera permission. To serve just the browser files without the AI API, run `python -m http.server 8000` from `WebGame/` and open the same address. Webcam access requires HTTPS or localhost; serve the page over HTTP instead of opening the HTML file directly. If the API is unavailable, the game uses its built-in local opponent.

For motion controls, frame your head, shoulders, arms, hands, and waist in the camera preview; legs and feet do not need to be visible. The red player is an articulated upper-body pose rig: its head, torso, shoulders, elbows, and hands follow MediaPipe's live landmarks. Throw straight punches or hooks with speed and reach; hold both gloves by your face to guard. Lean/shift your upper body side to side to circle and toward/away from the camera to adjust range. Keyboard controls remain available: WASD/arrow keys for footwork, J/K/H/L for punches, and hold Space/B to guard. The camera stays in your browser; pose landmarks are processed locally and only game observations are sent to the opponent API.

## Train an RL opponent

```bash
pip install -r requirements-rl.txt
python -c "import sys; sys.path.insert(0, '../sdk'); from boxing_sdk import train_ppo; train_ppo(500000, 'models/boxing_ppo')"
```

From the SDK directory, for custom training:

```python
from boxing_sdk import BoxingEnv
env = BoxingEnv(difficulty="hard")
obs, info = env.reset(seed=7)
obs, reward, terminated, truncated, info = env.step(3)  # guard
```

The Gymnasium action map is: `0 idle`, `1 move_forward`, `2 move_backward`, `3 block`, `4 jab`, `5 cross`, `6 leftHook`, `7 rightHook`, `8 dodge_left`, `9 dodge_right`. Observations contain normalized distance, both fighters' health and stamina, incoming attack, opponent guard/attack, and recovery state. The game API accepts JSON observations at `POST /api/opponent/act`; it returns the trained policy's move when `BOXING_MODEL` is configured, otherwise a difficulty-aware utility action.

## Train against the Unity game

`unity_env.py` connects Gymnasium to the **actual Unity combat loop** over localhost UDP port `5056`. Each Python step supplies one `BoxingAction`, advances a Unity frame, and returns Unity's combat observation and health-based reward. The Python policy controls the AI boxer; play as the other boxer with the Unity keyboard/mouse or webcam controls.

1. Open `Assets/Scenes/BoxingMatch_UI.unity` in Unity 6 and press **Play**. The combat environment automatically starts the training bridge on `127.0.0.1:5056`.
2. In a separate terminal, from `WebGame/backend`, install the RL dependencies (Python 3.11 is recommended):
   ```bash
   pip install -r requirements-rl.txt
   ```
3. While Unity remains in Play Mode, start training:
   ```bash
   python train_unity_ppo.py --timesteps 500000 --output models/unity_boxing_ppo
   ```
4. Let training complete; periodic checkpoints are written under `models/checkpoints/`, followed by the final policy. To run the saved policy against you, keep Unity in Play Mode, stop the trainer, and run:
   ```bash
   python play_unity_ppo.py models/unity_boxing_ppo.zip
   ```

Only one Python trainer/client should connect to Unity at a time. The bridge defaults to the current Unity player and opponent positions and resets fighter health, stamina, transforms, timer, and attack state between episodes. The PPO action indices match `BoxingAction.cs`: `0 Idle`, `1 MoveForward`, `2 MoveBackward`, `3 StrafeLeft`, `4 StrafeRight`, `5 Jab`, `6 Cross`, `7 HookLeft`, `8 HookRight`, `9 Block`, `10 DodgeLeft`, `11 DodgeRight`. Its 18-value observation is built from Unity's `BoxingObservation`; rewards favor damage dealt and penalize damage taken, with a terminal win/loss reward. Since the current Unity training setup uses a live human-controlled sparring partner, training performance depends on the player's behavior. Close the Unity scene or stop Play Mode to release the training bridge port.

To serve a trained model:

```bash
# Windows PowerShell
$env:BOXING_MODEL = "models/boxing_ppo.zip"
uvicorn server:app
```

The initial environment is a compact self-play simulator with simplified opponent pressure, damage, cooldown, and stamina. Tune or replace its player model/reward before treating a policy as a realistic sparring coach.
