import time

import numpy as np
import pytest

from uncycled_env import UncycledVecEnv

MATCHES = 4


def make_env(cmd, learner="citizen", matches=MATCHES, seed=3, **kw):
    return UncycledVecEnv(cmd, content_dir="unused", learner=learner, matches=matches, seed=seed, **kw)


def test_reset_shapes(fake_host_cmd):
    env = make_env(fake_host_cmd)
    try:
        obs = env.reset()
        agents = MATCHES * 6
        assert env.num_envs == agents
        assert obs.shape == (agents, env.observation_space.shape[0])
        assert obs.dtype == np.float32
        masks = np.stack(env.env_method("action_masks"))
        assert masks.shape == (agents, env.action_space.n)
        assert masks.dtype == bool
        assert masks[:, 0].all()
        assert len(env.schema_hash) == 64
    finally:
        env.close()


def test_polluter_agent_count(fake_host_cmd):
    env = make_env(fake_host_cmd, learner="polluter")
    try:
        assert env.num_envs == MATCHES * 2
    finally:
        env.close()


def test_masked_action_never_rejected(fake_host_cmd):
    env = make_env(fake_host_cmd)
    rng = np.random.default_rng(0)
    try:
        env.reset()
        for _ in range(100):
            masks = np.stack(env.env_method("action_masks"))
            actions = np.array([rng.choice(np.flatnonzero(m)) for m in masks])
            env.step(actions)
        time.sleep(0.3)  # let the stderr reader catch up
        assert env.invalid_action_lines == 0
    finally:
        env.close()


def test_unmasked_action_is_noticed_by_the_host(fake_host_cmd):
    """Guards the counter used above: an invalid action must show up on stderr."""
    env = make_env(fake_host_cmd, matches=1)
    try:
        env.reset()
        for _ in range(20):
            masks = np.stack(env.env_method("action_masks"))
            bad = [int(np.flatnonzero(~m)[0]) if (~m).any() else 0 for m in masks]
            env.step(np.array(bad))
        env.step(np.zeros(env.num_envs, dtype=np.int64))
        deadline = time.time() + 3
        while env.invalid_action_lines == 0 and time.time() < deadline:
            time.sleep(0.05)
        assert env.invalid_action_lines > 0
    finally:
        env.close()


def test_done_autoreset_and_terminal_obs(fake_host_cmd):
    env = make_env(fake_host_cmd, matches=2)
    try:
        env.reset()
        saw_done = False
        for _ in range(200):
            masks = np.stack(env.env_method("action_masks"))
            obs, rew, done, infos = env.step(np.array([np.flatnonzero(m)[0] for m in masks]))
            assert obs.shape[0] == env.num_envs
            for i in np.flatnonzero(done):
                saw_done = True
                assert "terminal_observation" in infos[i]
                assert infos[i]["terminal_observation"].shape == obs[i].shape
                assert not infos[i].get("TimeLimit.truncated", False)
            # the two agents groups of a match end together
            assert done.reshape(2, 6).all(axis=1).tolist() == done.reshape(2, 6).any(axis=1).tolist()
        assert saw_done
    finally:
        env.close()


def test_good_action_rewarded(fake_host_cmd):
    env = make_env(fake_host_cmd, matches=1, extra_args=["--good-action", "7", "--mask-p", "1.0"])
    try:
        env.reset()
        _, rew, _, _ = env.step(np.full(env.num_envs, 7))
        assert rew.tolist() == [1.0] * env.num_envs
        _, rew2, _, _ = env.step(np.full(env.num_envs, 2))
        assert rew2.tolist() == [0.0] * env.num_envs
    finally:
        env.close()


def test_close_kills_host(fake_host_cmd):
    env = make_env(fake_host_cmd)
    env.reset()
    proc = env._proc
    assert proc.poll() is None
    env.close()
    assert proc.poll() is not None
    env.close()  # idempotent


def test_schema_hash_mismatch_is_rejected(fake_host_cmd):
    with pytest.raises(RuntimeError, match="schema"):
        make_env(fake_host_cmd, expected_schema_hash="0" * 64)


def test_bad_handshake_raises(tmp_path):
    import sys
    bad = tmp_path / "bad_host.py"
    bad.write_text("import sys; sys.stdout.buffer.write(b'NOPE' + bytes(80)); sys.stdout.flush()\n")
    with pytest.raises(RuntimeError, match="handshake"):
        UncycledVecEnv([sys.executable, str(bad)], content_dir="x", learner="citizen", matches=1, seed=1)
