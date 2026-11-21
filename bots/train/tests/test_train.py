import json
import subprocess
import sys
from pathlib import Path

TRAIN_DIR = Path(__file__).resolve().parents[1]
FAKE_HOST = Path(__file__).with_name("fake_host.py")


def test_train_smoke_writes_checkpoint_and_run_json(tmp_path):
    cmd = [
        sys.executable, str(TRAIN_DIR / "train.py"), "--config", str(TRAIN_DIR / "config.yaml"),
        "--learner", "citizen", "--steps", "3000", "--run", "pytest-smoke", "--out-dir", str(tmp_path),
        "--host-cmd", f'"{sys.executable}" "{FAKE_HOST}"', "--matches", "4", "--checkpoint-every", "1500",
        "--device", "cpu",
    ]
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
    assert result.returncode == 0, result.stderr[-2000:]
    ckpts = sorted((tmp_path / "checkpoints" / "pytest-smoke").glob("*.zip"))
    assert ckpts, "no checkpoint written"
    run = json.loads((tmp_path / "checkpoints" / "pytest-smoke" / "run.json").read_text())
    assert run["seed"] == 1 and run["learner"] == "citizen" and "git_commit" in run
    assert list((tmp_path / "runs" / "pytest-smoke").rglob("events.out.tfevents*"))
