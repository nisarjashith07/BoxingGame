"""Run a trained PPO policy against the human boxer in Unity Play Mode."""
import argparse

from stable_baselines3 import PPO

from unity_env import UnityBoxingEnv


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("model", help="SB3 model path, for example models/unity_boxing_ppo.zip")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5056)
    args = parser.parse_args()
    env = UnityBoxingEnv(host=args.host, port=args.port)
    model = PPO.load(args.model)
    try:
        obs, _ = env.reset()
        while True:
            action, _ = model.predict(obs, deterministic=True)
            obs, reward, terminated, truncated, info = env.step(int(action))
            if terminated or truncated:
                print(f"Bout over: {info['winner'] or 'decision'} (reward {reward:.2f}); resetting")
                obs, _ = env.reset()
    except KeyboardInterrupt:
        print("Stopped Unity policy runner.")
    finally:
        env.close()


if __name__ == "__main__":
    main()
