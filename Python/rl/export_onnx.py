"""
export_onnx.py
==============
Export the trained BoxingGame policy to ONNX format.

Usage
-----
    python -m Python.rl.export_onnx
    # or:
    python c:/Users/harsh/Desktop/BoxingGame/Python/rl/export_onnx.py

Input
-----
    float32 tensor of shape [1, 18]

Outputs
-------
    action : int64 tensor of shape [1]   (argmax of logits)
    logits : float32 tensor of shape [1, 12]

Saved to
--------
    Python/rl/boxing_ai_policy.onnx
"""

from __future__ import annotations

import sys
from pathlib import Path

import torch

# -- Path setup ----------------------------------------------------------------
_THIS_DIR = Path(__file__).resolve().parent
_ROOT     = _THIS_DIR.parent.parent
sys.path.insert(0, str(_ROOT))

from Python.rl.policy import BoxingPolicy, ActorOnly  # noqa: E402

CHECKPOINT = _THIS_DIR / "checkpoints" / "boxing_ai_final.pt"
ONNX_OUT   = _THIS_DIR / "boxing_ai_policy.onnx"


def export():
    # -- Load checkpoint ---------------------------------------------------
    if not CHECKPOINT.exists():
        raise FileNotFoundError(
            f"Checkpoint not found: {CHECKPOINT}\n"
            "Please run train.py first."
        )

    print(f"Loading checkpoint: {CHECKPOINT}")
    ckpt = torch.load(CHECKPOINT, map_location='cpu', weights_only=True)

    policy = BoxingPolicy()
    policy.load_state_dict(ckpt['policy_state_dict'])
    policy.eval()

    step_val = ckpt.get('global_step', None)
    step_str = f"{step_val:,}" if step_val is not None else "?"
    print(f"  Trained for {step_str} steps")
    mr = ckpt.get('mean_reward', None)
    mr_str = f"{mr:.2f}" if mr is not None else "?"
    print(f"  Mean reward : {mr_str}")
    wr = ckpt.get('win_rate', None)
    wr_str = f"{wr*100:.1f}%" if wr is not None else "?"
    print(f"  Win rate    : {wr_str}")

    # -- Wrap actor-only ---------------------------------------------------
    actor = ActorOnly(policy)
    actor.eval()

    # -- Dummy input -------------------------------------------------------
    dummy_input = torch.zeros(1, 18, dtype=torch.float32)

    # -- Sanity check in PyTorch -------------------------------------------
    with torch.no_grad():
        action_pt, logits_pt = actor(dummy_input)
    print(f"\nPyTorch inference check:")
    print(f"  action : {action_pt.tolist()}")
    print(f"  logits : {logits_pt.tolist()}")

    # -- Export to ONNX ----------------------------------------------------
    # Use dynamic_shapes API (torch >= 2.1) to suppress deprecation warning.
    # Falls back to dynamic_axes if dynamic_shapes is unavailable.
    print(f"\nExporting to ONNX: {ONNX_OUT}")
    import warnings
    try:
        batch_dim = torch.export.Dim("batch_size", min=1, max=64)
        dynamic_shapes = {"obs": {0: batch_dim}}
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            torch.onnx.export(
                actor,
                (dummy_input,),
                str(ONNX_OUT),
                export_params=True,
                opset_version=18,
                do_constant_folding=True,
                input_names=['observation'],
                output_names=['action', 'logits'],
                dynamic_shapes=dynamic_shapes,
                dynamo=False,
                verbose=False,
            )
    except Exception:
        # Fallback: legacy export with dynamic_axes
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            torch.onnx.export(
                actor,
                dummy_input,
                str(ONNX_OUT),
                export_params=True,
                opset_version=18,
                do_constant_folding=True,
                input_names=['observation'],
                output_names=['action', 'logits'],
                dynamic_axes={
                    'observation': {0: 'batch_size'},
                    'action':      {0: 'batch_size'},
                    'logits':      {0: 'batch_size'},
                },
                verbose=False,
            )
    print("  Export complete.")

    # -- Verify with onnx.checker ------------------------------------------
    try:
        import onnx
        model = onnx.load(str(ONNX_OUT))
        onnx.checker.check_model(model)
        print("  [OK] ONNX model verified successfully.")

        # Print model info
        print(f"\nONNX model info:")
        print(f"  IR version : {model.ir_version}")
        print(f"  Opset      : {model.opset_import[0].version}")
        for inp in model.graph.input:
            print(f"  Input      : {inp.name}  {[d.dim_value for d in inp.type.tensor_type.shape.dim]}")
        for out in model.graph.output:
            print(f"  Output     : {out.name}  {[d.dim_value for d in out.type.tensor_type.shape.dim]}")

    except ImportError:
        print("  [WARNING] onnx package not installed - skipping model verification.")
        print("  Install with: pip install onnx")

    # -- Optional: verify with onnxruntime ---------------------------------
    try:
        import onnxruntime as ort
        sess = ort.InferenceSession(str(ONNX_OUT))
        inp  = dummy_input.numpy()
        ort_action, ort_logits = sess.run(None, {'observation': inp})
        print(f"\nONNXRuntime inference check:")
        print(f"  action : {ort_action.tolist()}")
        print(f"  logits : {ort_logits.tolist()}")
        print("  [OK] ONNXRuntime verification passed.")
    except ImportError:
        print("  [INFO] onnxruntime not installed - skipping runtime check.")

    print(f"\nDone. ONNX model saved to:\n  {ONNX_OUT}")


if __name__ == '__main__':
    export()
