"""Train PPO against the live Unity match. Start Unity Play Mode first."""
import argparse
from pathlib import Path

from stable_baselines3 import PPO
from stable_baselines3.common.callbacks import CheckpointCallback

from unity_env import UnityBoxingEnv


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--timesteps", type=int, default=500_000)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5056)
    parser.add_argument("--output", default="models/unity_boxing_ppo")
    args = parser.parse_args()

    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    env = UnityBoxingEnv(host=args.host, port=args.port)
    try:
        model = PPO("MlpPolicy", env, verbose=1, learning_rate=3e-4,
                    n_steps=1024, batch_size=256, gamma=.99,
                    tensorboard_log="runs/unity_boxing/")
        checkpoint = CheckpointCallback(save_freq=25_000, save_path=str(output.parent / "checkpoints"),
                                        name_prefix=output.name)
        model.learn(total_timesteps=args.timesteps, callback=checkpoint)
        model.save(str(output))
        print(f"Saved Unity-trained policy to {output}.zip")
    finally:
        env.close()


if __name__ == "__main__":
    main()
