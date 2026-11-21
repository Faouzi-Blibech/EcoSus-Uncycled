"""Stable-Baselines3 VecEnv over the Uncycled training host (plan Task 4 pipe protocol).

Every seat of the learner role in every match is one "environment" of the vector env. The host runs the
matches; this class only reads frames and writes actions.

Protocol (stdin/stdout of the host, little-endian, logs on stderr):
  host -> py, once : b"UCT1", int32 obsSize, int32 actionCount, int32 agentCount, 64 bytes ASCII schema hash
  host -> py, loop : per agent float32[obsSize] obs, uint8[actionCount] mask, float32 reward, uint8 done
  py -> host, loop : int32[agentCount] actions

A frame with done=1 for an agent carries the first observation and mask of the replacement match. The
terminal observation is not on the wire, so infos[i]["terminal_observation"] is the previous observation of
that agent. Every match end is a true terminal (never a time-limit truncation), so that value is never
bootstrapped from.
"""
from __future__ import annotations

import collections
import struct
import subprocess
import threading
from typing import Any, Iterable, Optional, Sequence

import gymnasium as gym
import numpy as np
from stable_baselines3.common.vec_env.base_vec_env import VecEnv

MAGIC = b"UCT1"
HASH_LEN = 64
HANDSHAKE_LEN = len(MAGIC) + 3 * 4 + HASH_LEN
INVALID_ACTION_MARKER = "invalid action"


class UncycledVecEnv(VecEnv):
    def __init__(
        self,
        host_cmd: Sequence[str],
        content_dir: str,
        learner: str,
        matches: int,
        seed: int,
        reward_weights: Optional[dict] = None,
        threads: Optional[int] = None,
        extra_args: Optional[Iterable[str]] = None,
        expected_schema_hash: Optional[str] = None,
    ):
        cmd = [str(c) for c in host_cmd]
        cmd += ["--content", str(content_dir), "--matches", str(matches), "--seed", str(seed),
                "--learner", learner]
        if threads is not None:
            cmd += ["--threads", str(threads)]
        for key, value in (reward_weights or {}).items():
            cmd += [f"--rw-{key}", str(value)]
        cmd += [str(a) for a in (extra_args or [])]
        self.host_cmd = cmd

        self._proc: Optional[subprocess.Popen] = None
        self._closed = False
        self.invalid_action_lines = 0
        self.stderr_tail: collections.deque = collections.deque(maxlen=200)
        self._proc = subprocess.Popen(cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                      stderr=subprocess.PIPE, bufsize=-1)
        self._drain_thread = threading.Thread(target=self._drain_stderr, daemon=True)
        self._drain_thread.start()

        try:
            head = self._read_exact(HANDSHAKE_LEN, what="handshake")
            if head[:4] != MAGIC:
                raise RuntimeError(f"bad handshake from host: magic {head[:4]!r}, expected {MAGIC!r}")
            obs_size, n_actions, n_agents = struct.unpack("<iii", head[4:16])
            self.schema_hash = head[16:].decode("ascii")
            if min(obs_size, n_actions, n_agents) <= 0:
                raise RuntimeError(f"bad handshake sizes: {obs_size}, {n_actions}, {n_agents}")
            if expected_schema_hash is not None and expected_schema_hash != self.schema_hash:
                raise RuntimeError(f"schema hash mismatch: host {self.schema_hash}, expected {expected_schema_hash}")
        except BaseException:
            self.close()
            raise

        self.obs_size, self.action_count = obs_size, n_actions
        self._frame_dtype = np.dtype([("obs", "<f4", (obs_size,)), ("mask", "u1", (n_actions,)),
                                      ("reward", "<f4"), ("done", "u1")])
        self._pending_actions: Optional[np.ndarray] = None
        self._obs: Optional[np.ndarray] = None
        self._masks: Optional[np.ndarray] = None

        super().__init__(
            n_agents,
            gym.spaces.Box(-1.0, 1.0, shape=(obs_size,), dtype=np.float32),
            gym.spaces.Discrete(n_actions),
        )

    # ---- pipe helpers -------------------------------------------------------------------------

    def _drain_stderr(self) -> None:
        for raw in iter(self._proc.stderr.readline, b""):
            line = raw.decode("utf-8", errors="replace").rstrip()
            if INVALID_ACTION_MARKER in line:
                self.invalid_action_lines += 1
            self.stderr_tail.append(line)

    def _read_exact(self, n: int, what: str = "frame") -> bytes:
        data = self._proc.stdout.read(n)
        if data is None or len(data) < n:
            self._proc.poll()
            tail = "\n".join(list(self.stderr_tail)[-10:])
            raise RuntimeError(f"host closed the pipe while reading {what} (exit code {self._proc.returncode}); "
                               f"stderr tail:\n{tail}")
        return data

    def _read_frame(self) -> np.ndarray:
        raw = self._read_exact(self.num_envs * self._frame_dtype.itemsize)
        return np.frombuffer(raw, dtype=self._frame_dtype)

    def _store(self, frame: np.ndarray) -> None:
        self._obs = np.array(frame["obs"], dtype=np.float32)
        self._masks = frame["mask"].astype(bool)

    @property
    def host_pid(self) -> int:
        return self._proc.pid

    # ---- VecEnv API ---------------------------------------------------------------------------

    def reset(self):
        """Return the current observations. The host cannot be restarted mid-run; the first call reads
        the host's opening frame, later calls return the latest observations again."""
        if self._obs is None:
            self._store(self._read_frame())
        self.reset_infos = [{} for _ in range(self.num_envs)]
        return self._obs.copy()

    def action_masks(self) -> np.ndarray:
        """Masks of the current observations, shape (agents, actionCount). Its presence is what sb3-contrib
        checks (`has_attr`) before it enables masking."""
        return self._masks.copy()

    def step_async(self, actions: np.ndarray) -> None:
        if self._obs is None:
            raise RuntimeError("call reset() before step()")
        actions = np.asarray(actions, dtype="<i4").reshape(-1)
        if actions.shape[0] != self.num_envs:
            raise ValueError(f"expected {self.num_envs} actions, got {actions.shape[0]}")
        self._pending_actions = actions

    def step_wait(self):
        actions, self._pending_actions = self._pending_actions, None
        self._proc.stdin.write(actions.tobytes())
        self._proc.stdin.flush()
        previous_obs = self._obs
        frame = self._read_frame()
        rewards = np.array(frame["reward"], dtype=np.float32)
        dones = frame["done"].astype(bool)
        self._store(frame)
        infos: list[dict[str, Any]] = [{} for _ in range(self.num_envs)]
        for i in np.flatnonzero(dones):
            infos[i]["terminal_observation"] = previous_obs[i].copy()
        return self._obs.copy(), rewards, dones, infos

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        proc = self._proc
        if proc is None:
            return
        try:
            if proc.stdin and not proc.stdin.closed:
                proc.stdin.close()
        except OSError:
            pass
        try:
            proc.wait(timeout=2)
        except subprocess.TimeoutExpired:
            proc.terminate()
            try:
                proc.wait(timeout=2)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait()
        for stream in (proc.stdout, proc.stderr):
            try:
                if stream:
                    stream.close()
            except OSError:
                pass

    def __del__(self):
        try:
            self.close()
        except Exception:
            pass

    def get_images(self):
        return [None] * self.num_envs

    def render(self, mode=None):
        return None

    def _indices(self, indices):
        if indices is None:
            return list(range(self.num_envs))
        if isinstance(indices, int):
            return [indices]
        return list(indices)

    def env_method(self, method_name: str, *method_args, indices=None, **method_kwargs):
        idx = self._indices(indices)
        if method_name == "action_masks":
            return [self._masks[i].copy() for i in idx]
        raise AttributeError(f"UncycledVecEnv has no env method {method_name!r}")

    def get_attr(self, attr_name: str, indices=None):
        value = None if attr_name == "render_mode" else getattr(self, attr_name)
        return [value for _ in self._indices(indices)]

    def set_attr(self, attr_name: str, value, indices=None) -> None:
        setattr(self, attr_name, value)

    def env_is_wrapped(self, wrapper_class, indices=None):
        return [False for _ in self._indices(indices)]

    def seed(self, seed=None):
        return [None for _ in range(self.num_envs)]
