import struct
from pathlib import Path
import torch

_THIS_DIR = Path(__file__).resolve().parent
_REPO_DIR = _THIS_DIR.parent.parent
CKPT_PATH = _THIS_DIR / "checkpoints" / "boxing_ai_final.pt"
OUT_PATH  = _REPO_DIR / "Assets" / "Models" / "boxing_ai_policy.bytes"

def export_binary():
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

    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with open(OUT_PATH, "wb") as f:
        # Magic: 'BOXI' (0x424F5849) + version 1
        f.write(struct.pack("<II", 0x424F5849, 1))
        total_floats = 0
        for name, shape in layers:
            tensor = sd[name].float().cpu().numpy()
            assert tuple(tensor.shape) == shape, f"Shape mismatch for {name}: {tensor.shape} != {shape}"
            f.write(tensor.tobytes())
            total_floats += tensor.size

    file_size_kb = OUT_PATH.stat().st_size / 1024
    print(f"Exported {total_floats:,} weights ({file_size_kb:.1f} KB) to:")
    print(f"  {OUT_PATH}")

if __name__ == "__main__":
    export_binary()

