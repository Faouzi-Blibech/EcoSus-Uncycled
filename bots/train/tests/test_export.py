import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import onnxruntime as ort
import pytest
import torch
from sb3_contrib import MaskablePPO


TRAIN_DIR = Path(__file__).resolve().parents[1]


@pytest.fixture(scope="module")
def checkpoint(tmp_path_factory):
    """A tiny checkpoint trained against the fake host."""
    out = tmp_path_factory.mktemp("ckpt")
    from conftest import FAKE_HOST
    cmd = [sys.executable, str(TRAIN_DIR / "train.py"), "--config", str(TRAIN_DIR / "config.yaml"),
           "--learner", "citizen", "--steps", "2048", "--run", "export", "--out-dir", str(out),
           "--host-cmd", f'"{sys.executable}" "{FAKE_HOST}"', "--matches", "2", "--device", "cpu"]
    r = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
    assert r.returncode == 0, r.stderr[-2000:]
    return out / "checkpoints" / "export" / "final.zip"


def test_onnx_matches_torch(checkpoint, tmp_path):
    onnx_path = tmp_path / "policy.onnx"
    r = subprocess.run([sys.executable, str(TRAIN_DIR / "export_onnx.py"), "--checkpoint", str(checkpoint),
                        "--out", str(onnx_path)], capture_output=True, text=True, timeout=300)
    assert r.returncode == 0, r.stderr[-2000:]
    meta = json.loads(onnx_path.with_suffix(".json").read_text())
    model = MaskablePPO.load(checkpoint, device="cpu")
    obs_size, n_actions = model.observation_space.shape[0], model.action_space.n
    assert meta["obs_size"] == obs_size and meta["action_count"] == n_actions
    assert len(meta["schema_hash"]) == 64 and meta["run_id"] == "export"

    rng = np.random.default_rng(0)
    obs = rng.uniform(-1, 1, (100, obs_size)).astype(np.float32)
    mask = rng.random((100, n_actions)) < 0.5
    mask[:, 0] = True
    sess = ort.InferenceSession(str(onnx_path), providers=["CPUExecutionProvider"])
    assert [i.name for i in sess.get_inputs()] == ["obs", "mask"]
    logits = sess.run(["logits"], {"obs": obs, "mask": mask})[0]
    assert logits.shape == (100, n_actions) and logits.dtype == np.float32
    assert (logits[~mask] == -1e9).all()

    with torch.no_grad():
        dist = model.policy.get_distribution(torch.as_tensor(obs), action_masks=mask)
        torch_logits = dist.distribution.logits.numpy()
        torch_pick = dist.get_actions(deterministic=True).numpy()
    assert (logits.argmax(axis=1) == torch_pick).all()
    assert (logits.argmax(axis=1) == torch_logits.argmax(axis=1)).all()
    # valid entries match up to the log-softmax shift
    shifted = logits - np.where(mask, logits, -np.inf).max(axis=1, keepdims=True)
    ref = torch_logits - np.where(mask, torch_logits, -np.inf).max(axis=1, keepdims=True)
    np.testing.assert_allclose(np.where(mask, shifted, 0), np.where(mask, ref, 0), atol=1e-4)
