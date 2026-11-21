"""The env tests that matter against the REAL published host (skipped when it is not built)."""
import time
from pathlib import Path

import numpy as np
import pytest

from uncycled_env import UncycledVecEnv

ROOT = Path(__file__).resolve().parents[2]
HOST = ROOT / ".build" / "train-win" / "Uncycled.Train.exe"
CONTENT = ROOT.parent / "content"

pytestmark = pytest.mark.skipif(not HOST.exists(), reason="real host not published (bots/.build/train-win)")


def make_env(learner="citizen", matches=2, seed=5):
    return UncycledVecEnv([str(HOST)], content_dir=str(CONTENT), learner=learner, matches=matches, seed=seed, threads=2)


def valid_actions(env, rng):
    masks = np.stack(env.env_method("action_masks"))
    return np.array([rng.choice(np.flatnonzero(m)) for m in masks]), masks


def test_real_reset_shapes():
    env = make_env()
    try:
        obs = env.reset()
        assert env.num_envs == 2 * 6
        assert obs.shape == (12, 154)
        assert obs.dtype == np.float32
        assert np.isfinite(obs).all()
        masks = np.stack(env.env_method("action_masks"))
        assert masks.shape == (12, 30) and masks.dtype == bool
        assert masks[:, 0].all()
        assert len(env.schema_hash) == 64
    finally:
        env.close()


def test_real_polluter_agent_count():
    env = make_env(learner="polluter")
    try:
        assert env.num_envs == 2 * 2
    finally:
        env.close()


def test_real_masked_action_never_rejected_and_matches_end():
    env = make_env(matches=2)
    rng = np.random.default_rng(0)
    saw_done = False
    try:
        env.reset()
        for _ in range(1500):
            actions, _ = valid_actions(env, rng)
            obs, rew, done, infos = env.step(actions)
            assert np.isfinite(obs).all() and np.isfinite(rew).all()
            for i in np.flatnonzero(done):
                saw_done = True
                assert infos[i]["terminal_observation"].shape == obs[i].shape
            if saw_done:
                break
        time.sleep(0.3)  # let the stderr reader catch up
        assert env.invalid_action_lines == 0
        assert saw_done, "a match should end within 1500 decisions"
    finally:
        env.close()


def test_real_masked_out_action_is_noticed():
    env = make_env(matches=1)
    try:
        env.reset()
        masks = np.stack(env.env_method("action_masks"))
        bad = [int(np.flatnonzero(~m)[0]) for m in masks]
        env.step(np.array(bad))
        deadline = time.time() + 3
        while env.invalid_action_lines == 0 and time.time() < deadline:
            time.sleep(0.05)
        assert env.invalid_action_lines > 0
    finally:
        env.close()


def test_real_close_kills_host():
    env = make_env(matches=1)
    env.reset()
    proc = env._proc
    assert proc.poll() is None
    env.close()
    assert proc.poll() is not None
