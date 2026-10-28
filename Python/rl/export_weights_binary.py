import struct
import argparse
from pathlib import Path

_THIS_DIR = Path(__file__).resolve().parent
_REPO_DIR = _THIS_DIR.parent.parent

CKPT_PATH = _THIS_DIR / "checkpoints" / "boxing_ai_final.pt"
RES_PATH  = _REPO_DIR / "Assets" / "Resources" / "boxing_ai_policy.bytes"

CANONICAL_CONTRACT = (
    "obs18:distance,selfHealth01,selfStamina01,opponentHealth01,opponentStamina01,"
    "selfGuard01,opponentGuard01,relativeX,relativeZ,relativeVelocityX,relativeVelocityZ,"
    "opponentAttackIntent01,incomingAttack,opponentRecovering,selfCanAttack,selfCanDodge,"
    "selfCanBlock,healthAdvantage|act12:Idle,MoveForward,MoveBackward,StrafeLeft,"
    "StrafeRight,Jab,Cross,HookLeft,HookRight,Block,DodgeLeft,DodgeRight"
)

def compute_fnv1a_32(text: str) -> int:
    h = 0x811C9DC5
    for b in text.encode("utf-8"):
        h ^= b
        h = (h * 0x01000193) & 0xFFFFFFFF
    return h

def export_binary(version: int = 2, output_path: Path = RES_PATH):
    import torch

    print(f"Loading checkpoint from: {CKPT_PATH}")
    ckpt = torch.load(CKPT_PATH, map_location="cpu")
    sd = ckpt["policy_state_dict"]

    layers = [
        ("trunk.0.weight", (256, 18)),
        ("trunk.0.bias",   (256,)),
        ("trunk.1.weight", (256,)),
        ("trunk.1.bias",   (256,)),
        ("trunk.3.weight", (256, 256)),
        ("trunk.3.bias",   (256,)),
        ("trunk.4.weight", (256,)),
        ("trunk.4.bias",   (256,)),
        ("trunk.6.weight", (128, 256)),
        ("trunk.6.bias",   (128,)),
        ("trunk.7.weight", (128,)),
        ("trunk.7.bias",   (128,)),
        ("actor_head.weight", (12, 128)),
        ("actor_head.bias",   (12,)),
    ]

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with open(output_path, "wb") as f:
        # Magic: 'BOXI' (0x424F5849)
        magic = 0x424F5849
        if version == 1:
            f.write(struct.pack("<II", magic, 1))
        elif version == 2:
            schema_hash = compute_fnv1a_32(CANONICAL_CONTRACT)
            # magic, version, obs_dim, act_dim, rules_ver, schema_hash, dist_norm, pos_norm
            f.write(struct.pack("<IIIIIIff", magic, 2, 18, 12, 2, schema_hash, 10.0, 5.0))
        else:
            raise ValueError(f"Unsupported export version: {version}")

        total_floats = 0
        for name, shape in layers:
            tensor = sd[name].float().cpu().numpy()
            assert tuple(tensor.shape) == shape, f"Shape mismatch for {name}: {tensor.shape} != {shape}"
            f.write(tensor.tobytes())
            total_floats += tensor.size

    file_size_kb = output_path.stat().st_size / 1024
    print(f"Exported {total_floats:,} weights ({file_size_kb:.1f} KB, v{version}) to:")
    print(f"  {output_path}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Export trained PyTorch policy weights to pure binary format for Unity C#.")
    parser.add_argument("--version", type=int, default=2, choices=[1, 2], help="Header format version (default: 2)")
    parser.add_argument("--out", type=str, default=str(RES_PATH), help="Destination path for .bytes file")
    args = parser.parse_args()
    export_binary(version=args.version, output_path=Path(args.out))

