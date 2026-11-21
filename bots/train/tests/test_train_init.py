import json
import subprocess
import sys
from pathlib import Path

import numpy as np
import torch

TRAIN_DIR = Path(__file__).resolve().parents[1]
FAKE_HOST = Path(__file__).with_name("fake_host.py")


def run_train(tmp_path, run, *extra, steps="1500"):
    cmd = [
        sys.executable, str(TRAIN_DIR / "train.py"), "--config", str(TRAIN_DIR / "config.yaml"),
        "--learner", "citizen", "--steps", steps, "--run", run, "--out-dir", str(tmp_path),
        "--host-cmd", f'"{sys.executable}" "{FAKE_HOST}"', "--matches", "4", "--checkpoint-every", "100000",
        "--device", "cpu", *extra,
    ]
    return subprocess.run(cmd, capture_output=True, text=True, timeout=300)


def policy_params(zip_path):
    from sb3_contrib import MaskablePPO
    model = MaskablePPO.load(str(zip_path), device="cpu")
    return {k: v.detach().clone() for k, v in model.policy.state_dict().items()}, model


def test_init_resumes_from_checkpoint_weights_and_keeps_its_network(tmp_path):
    first = run_train(tmp_path, "a", "--set", "ppo.net_arch={pi: [16, 16], vf: [16, 16]}", "--seed", "5")
    assert first.returncode == 0, first.stderr[-2000:]
    init_zip = tmp_path / "checkpoints" / "a" / "final.zip"
    before, _ = policy_params(init_zip)

    # Learning rate 0: a run that really starts from the checkpoint ends with identical weights.
    # The net_arch of the default config (256) differs from the checkpoint (16): the checkpoint wins.
    second = run_train(tmp_path, "b", "--init", str(init_zip), "--set", "ppo.learning_rate=0.0", "--seed", "9")
    assert second.returncode == 0, second.stderr[-2000:]
    after, model = policy_params(tmp_path / "checkpoints" / "b" / "final.zip")
    assert after.keys() == before.keys()
    for key in before:
        assert torch.equal(before[key], after[key]), key
    assert model.num_timesteps >= 1500  # steps are counted per turn, not on top of the checkpoint
    run = json.loads((tmp_path / "checkpoints" / "b" / "run.json").read_text())
    assert run["init"] == str(init_zip)


def test_without_init_a_different_seed_gives_different_weights(tmp_path):
    # Guards the test above: weights equality must come from --init, not from a fixed initialisation.
    for name, seed in (("a", "5"), ("b", "9")):
        result = run_train(tmp_path, name, "--set", "ppo.learning_rate=0.0", "--seed", seed)
        assert result.returncode == 0, result.stderr[-2000:]
    a, _ = policy_params(tmp_path / "checkpoints" / "a" / "final.zip")
    b, _ = policy_params(tmp_path / "checkpoints" / "b" / "final.zip")
    assert any(not np.array_equal(a[k].numpy(), b[k].numpy()) for k in a)


def test_init_with_a_missing_file_fails_clearly(tmp_path):
    result = run_train(tmp_path, "c", "--init", str(tmp_path / "nope.zip"))
    assert result.returncode != 0
    assert "nope.zip" in result.stderr


def test_init_from_a_checkpoint_made_on_another_schema_is_refused(tmp_path):
    first = run_train(tmp_path, "a", "--seed", "5")
    assert first.returncode == 0, first.stderr[-2000:]
    ckpt_dir = tmp_path / "checkpoints" / "a"
    meta = json.loads((ckpt_dir / "run.json").read_text())
    host_hash = meta["schema_hash"]

    # same hash: accepted
    ok = run_train(tmp_path, "b", "--init", str(ckpt_dir / "final.zip"), "--seed", "6")
    assert ok.returncode == 0, ok.stderr[-2000:]

    # another hash (an older action list or observation): refused before any training, naming both hashes
    meta["schema_hash"] = "0" * 64
    (ckpt_dir / "run.json").write_text(json.dumps(meta), encoding="utf-8")
    bad = run_train(tmp_path, "c", "--init", str(ckpt_dir / "final.zip"), "--seed", "6")
    assert bad.returncode == 2, bad.stderr[-2000:]
    assert "schema" in bad.stderr and host_hash in bad.stderr and "0" * 64 in bad.stderr
    assert not (tmp_path / "checkpoints" / "c" / "final.zip").exists()


def test_init_without_a_recorded_schema_hash_is_refused(tmp_path):
    first = run_train(tmp_path, "a", "--seed", "5")
    assert first.returncode == 0, first.stderr[-2000:]
    ckpt_dir = tmp_path / "checkpoints" / "a"
    (ckpt_dir / "run.json").unlink()
    result = run_train(tmp_path, "b", "--init", str(ckpt_dir / "final.zip"), "--seed", "6")
    assert result.returncode == 2, result.stderr[-2000:]
    assert "run.json" in result.stderr
