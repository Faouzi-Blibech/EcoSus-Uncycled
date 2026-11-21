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
        "--device", "cpu", "--set", "host.seats=6",
    ]
    result = subprocess.run(cmd, capture_output=True, text=True, timeout=300)
    assert result.returncode == 0, result.stderr[-2000:]
    ckpts = sorted((tmp_path / "checkpoints" / "pytest-smoke").glob("*.zip"))
    assert ckpts, "no checkpoint written"
    run = json.loads((tmp_path / "checkpoints" / "pytest-smoke" / "run.json").read_text())
    assert run["seed"] == 1 and run["learner"] == "citizen" and "git_commit" in run
    # the map and the room size are inputs of the run: kept in run.json and in the host command
    assert run["map"] == "greybox_01" and run["seats"] == 6
    assert run["host_cmd"][run["host_cmd"].index("--map") + 1] == "greybox_01"
    assert run["host_cmd"][run["host_cmd"].index("--seats") + 1] == "6"
    assert list((tmp_path / "runs" / "pytest-smoke").rglob("events.out.tfevents*"))
