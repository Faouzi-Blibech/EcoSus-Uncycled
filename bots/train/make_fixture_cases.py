"""Write the C# test cases for an exported ONNX policy.

    python make_fixture_cases.py --onnx ../Uncycled.Bots.Tests/fixtures/tiny.onnx --out ../Uncycled.Bots.Tests/fixtures/tiny_cases.json

Each case: obs (float32 values), mask (0/1) and the argmax of the ONNX logits computed by onnxruntime in
Python. C# must pick the same action at temperature 0.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
import onnxruntime as ort


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--onnx", type=Path, required=True)
    p.add_argument("--out", type=Path, required=True)
    p.add_argument("--cases", type=int, default=20)
    p.add_argument("--seed", type=int, default=7)
    args = p.parse_args()

    meta = json.loads(args.onnx.with_suffix(".json").read_text(encoding="utf-8"))
    obs_size, action_count = meta["obs_size"], meta["action_count"]
    rng = np.random.default_rng(args.seed)
    session = ort.InferenceSession(str(args.onnx), providers=["CPUExecutionProvider"])
    cases = []
    for _ in range(args.cases):
        obs = rng.uniform(-1.0, 1.0, size=(1, obs_size)).astype(np.float32)
        mask = rng.random((1, action_count)) < 0.5
        mask[0, rng.integers(action_count)] = True
        logits = session.run(["logits"], {"obs": obs, "mask": mask})[0]
        cases.append({
            "obs": [float(x) for x in obs[0]],
            "mask": [int(x) for x in mask[0]],
            "argmax": int(np.argmax(logits[0])),
        })
    args.out.write_text(json.dumps({"schema_hash": meta["schema_hash"], "cases": cases}), encoding="utf-8")
    print(f"wrote {len(cases)} cases to {args.out}; distinct argmax: {len({c['argmax'] for c in cases})}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
