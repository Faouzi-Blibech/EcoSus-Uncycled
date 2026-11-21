"""Export a MaskablePPO checkpoint to ONNX.

    python export_onnx.py --checkpoint checkpoints/run/final.zip --out citizen.onnx

Inputs: obs float32 [batch, obsSize], mask bool [batch, actionCount].
Output: logits float32 [batch, actionCount], masked entries at -1e9.
Also writes <out>.json with schema_hash, obs_size, action_count and the run id. The trainer does not use
VecNormalize, so no normalisation statistics have to be applied outside the network.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import torch
from torch import nn

MASKED_LOGIT = -1e9


class MaskedLogits(nn.Module):
    def __init__(self, policy):
        super().__init__()
        self.policy = policy

    def forward(self, obs: torch.Tensor, mask: torch.Tensor) -> torch.Tensor:
        features = self.policy.extract_features(obs, self.policy.pi_features_extractor)
        latent_pi = self.policy.mlp_extractor.forward_actor(features)
        logits = self.policy.action_net(latent_pi)
        return torch.where(mask, logits, torch.full_like(logits, MASKED_LOGIT))


def export(checkpoint: Path, out: Path, run_id: str | None = None) -> Path:
    from sb3_contrib import MaskablePPO

    model = MaskablePPO.load(str(checkpoint), device="cpu")
    policy = model.policy.eval()
    obs_size = int(model.observation_space.shape[0])
    action_count = int(model.action_space.n)

    run_json = checkpoint.parent / "run.json"
    run_info = json.loads(run_json.read_text(encoding="utf-8")) if run_json.exists() else {}
    schema_hash = run_info.get("schema_hash")
    if not schema_hash:
        raise SystemExit(f"no schema_hash in {run_json}; the checkpoint was not made by train.py")

    wrapper = MaskedLogits(policy).eval()
    dummy_obs = torch.zeros(2, obs_size, dtype=torch.float32)
    dummy_mask = torch.ones(2, action_count, dtype=torch.bool)
    out.parent.mkdir(parents=True, exist_ok=True)
    with torch.no_grad():
        torch.onnx.export(
            wrapper, (dummy_obs, dummy_mask), str(out),
            input_names=["obs", "mask"], output_names=["logits"],
            dynamic_axes={"obs": {0: "batch"}, "mask": {0: "batch"}, "logits": {0: "batch"}},
            opset_version=17, dynamo=False,
        )
    meta = {
        "schema_hash": schema_hash,
        "obs_size": obs_size,
        "action_count": action_count,
        "run_id": run_id or run_info.get("run", checkpoint.parent.name),
        "learner": run_info.get("learner"),
        "git_commit": run_info.get("git_commit"),
    }
    out.with_suffix(".json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
    return out


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--checkpoint", type=Path, required=True)
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--run-id")
    args = p.parse_args(argv)
    export(args.checkpoint, args.out, args.run_id)
    print(f"wrote {args.out} and {args.out.with_suffix('.json')}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
