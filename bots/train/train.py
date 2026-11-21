"""Train a MaskablePPO policy for one role against the Uncycled training host.

    python train.py --config config.yaml --learner citizen --steps 200000 --run smoke
"""
from __future__ import annotations

import argparse
import json
import platform
import shlex
import subprocess
import sys
import time
from pathlib import Path

import yaml

HERE = Path(__file__).resolve().parent


def load_config(path: Path) -> dict:
    with open(path, "r", encoding="utf-8") as f:
        return yaml.safe_load(f)


def git_commit() -> str:
    try:
        out = subprocess.run(["git", "rev-parse", "HEAD"], cwd=HERE, capture_output=True, text=True, timeout=10)
        return out.stdout.strip() or "unknown"
    except (OSError, subprocess.SubprocessError):
        return "unknown"


def resolve_device(requested: str) -> str:
    import torch
    if requested == "auto":
        return "cuda" if torch.cuda.is_available() else "cpu"
    return requested


def parse_args(argv=None):
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--config", type=Path, default=HERE / "config.yaml")
    p.add_argument("--learner", choices=["citizen", "polluter"], required=True)
    p.add_argument("--steps", type=int, required=True, help="total env steps (one decision of one agent)")
    p.add_argument("--run", required=True, help="run name (folder under runs/ and checkpoints/)")
    p.add_argument("--out-dir", type=Path, default=HERE, help="parent of runs/ and checkpoints/")
    p.add_argument("--host-cmd", help="host command line (default: host.exe from the config)")
    p.add_argument("--host-extra", default="", help="extra host flags, one string, split like a shell line")
    p.add_argument("--matches", type=int)
    p.add_argument("--seed", type=int)
    p.add_argument("--device", choices=["auto", "cuda", "cpu"])
    p.add_argument("--checkpoint-every", type=int)
    p.add_argument("--init", type=Path, metavar="CKPT.zip",
                   help="continue from this checkpoint (weights and network shape come from it; the ppo values of "
                        "the config / --set replace the saved ones; --steps counts from 0)")
    p.add_argument("--set", action="append", default=[], metavar="KEY=VALUE",
                   help="override a config value, dotted path, YAML value (e.g. ppo.n_steps=64); repeatable")
    return p.parse_args(argv)


def main(argv=None) -> int:
    args = parse_args(argv)
    cfg = load_config(args.config)
    for item in args.set:
        key, _, raw = item.partition("=")
        node = cfg
        *parents, leaf = key.split(".")
        for part in parents:
            node = node[part]
        node[leaf] = yaml.safe_load(raw)
    cfg_dir = args.config.resolve().parent

    # Heavy imports after argument parsing so --help is instant.
    import torch
    import stable_baselines3
    import sb3_contrib
    import gymnasium
    from sb3_contrib import MaskablePPO
    from stable_baselines3.common.callbacks import CheckpointCallback
    from stable_baselines3.common.logger import configure
    from stable_baselines3.common.vec_env import VecMonitor

    from uncycled_env import UncycledVecEnv

    host = cfg["host"]
    host_cmd = shlex.split(args.host_cmd) if args.host_cmd else [str((cfg_dir / host["exe"]).resolve())]
    matches = args.matches or host["matches"]
    seed = args.seed if args.seed is not None else cfg["seed"]
    ppo = dict(cfg["ppo"])
    device = resolve_device(args.device or ppo.pop("device", "auto"))
    ppo.pop("device", None)
    every = args.checkpoint_every or cfg["checkpoint_every"]
    net_arch = ppo.pop("net_arch")

    if args.init is not None and not args.init.is_file():
        print(f"error: --init checkpoint not found: {args.init}", file=sys.stderr)
        return 2

    run_runs = args.out_dir / "runs" / args.run
    run_ckpt = args.out_dir / "checkpoints" / args.run
    run_runs.mkdir(parents=True, exist_ok=True)
    run_ckpt.mkdir(parents=True, exist_ok=True)

    env = UncycledVecEnv(host_cmd, content_dir=str((cfg_dir / host["content_dir"]).resolve()),
                         learner=args.learner, matches=matches, seed=seed, reward_weights=cfg["rewards"],
                         threads=host.get("threads"), extra_args=shlex.split(args.host_extra))
    try:
        venv = VecMonitor(env)
        info = {
            "run": args.run, "learner": args.learner, "seed": seed, "steps": args.steps, "matches": matches,
            "agents": env.num_envs, "obs_size": env.obs_size, "action_count": env.action_count,
            "schema_hash": env.schema_hash, "git_commit": git_commit(), "host_cmd": env.host_cmd,
            "device": device, "cuda_available": torch.cuda.is_available(),
            "gpu": torch.cuda.get_device_name(0) if torch.cuda.is_available() else None,
            "versions": {"python": platform.python_version(), "torch": torch.__version__,
                         "stable_baselines3": stable_baselines3.__version__, "sb3_contrib": sb3_contrib.__version__,
                         "gymnasium": gymnasium.__version__},
            "init": str(args.init) if args.init is not None else None,
            "config": cfg, "started": time.strftime("%Y-%m-%dT%H:%M:%S"),
        }
        (run_ckpt / "run.json").write_text(json.dumps(info, indent=2), encoding="utf-8")
        print(f"device={device} agents={env.num_envs} obs={env.obs_size} actions={env.action_count}", flush=True)

        if args.init is not None:
            # The saved network (and its shape) is kept; the current ppo values replace the saved ones.
            model = MaskablePPO.load(str(args.init), env=venv, device=device, custom_objects=dict(ppo))
            model.set_random_seed(seed)
        else:
            model = MaskablePPO(
                "MlpPolicy", venv, seed=seed, device=device, verbose=1,
                tensorboard_log=None,
                policy_kwargs={"net_arch": net_arch},
                **ppo,
            )
        model.set_logger(configure(str(run_runs), ["stdout", "csv", "tensorboard"]))

        ckpt_cb = CheckpointCallback(save_freq=max(every // env.num_envs, 1), save_path=str(run_ckpt),
                                     name_prefix="ckpt")
        t0 = time.time()
        model.learn(total_timesteps=args.steps, callback=ckpt_cb, progress_bar=False)
        elapsed = time.time() - t0
        model.save(str(run_ckpt / "final"))
        info.update({"net_arch_used": model.policy_kwargs.get("net_arch"), "elapsed_s": elapsed,
                     "num_timesteps": int(model.num_timesteps),
                     "steps_per_second": model.num_timesteps / elapsed, "finished": time.strftime("%Y-%m-%dT%H:%M:%S"),
                     "host_invalid_action_lines": env.invalid_action_lines})
        (run_ckpt / "run.json").write_text(json.dumps(info, indent=2), encoding="utf-8")
        print(f"done: {model.num_timesteps} steps in {elapsed:.1f} s = {model.num_timesteps / elapsed:.0f} steps/s",
              flush=True)
    finally:
        venv.close() if "venv" in locals() else env.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
