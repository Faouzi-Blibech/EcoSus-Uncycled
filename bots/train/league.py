"""Self-play by turns ("league"): train one role per turn against a mix of opponents, keep the new network
only if it beats the previous version of that role against the same opponent.

    python league.py --out-dir E:/uncycled-train/league1 --turns 6 --turn-steps 10000000 \
        --citizen-ckpt <cit>/final.zip --polluter-ckpt <pol>/final.zip \
        --citizen-onnx <cit>.onnx --polluter-onnx <pol>.onnx \
        --host-cmd "<host>/Uncycled.Train.exe --bots <wt>/bots/bots.json" \
        --host-extra "--threads 2" --train-set ppo.gamma=0.995

Each turn (roles alternate, --first-learner starts):
  a) train the learner role for --turn-steps, continuing from its current checkpoint (train.py --init). The
     host plays the other role with a mix: its current network (--w-current), scripted bots (--w-scripted) and
     its previous version (--w-previous, left out while there is none);
  b) export the new checkpoint to ONNX;
  c) evaluate on --eval-seeds (default 100000-100499): the NEW network and the PREVIOUS network of the learner,
     each against the other role's current network;
  d) evaluate both against scripted opponents too; keep the new version only if its win rate against the
     other role's network is strictly higher than the previous one's AND its win rate against scripted is not
     more than 0.03 below the previous one's, else log "rejected" and keep the previous one;
  e) the turn report gives the kept network's rates against scripted and the other role's network, with
     ejections right/wrong per 100 matches.
league.json and league.md are rewritten after every turn. Everything lives under --out-dir. One training
process at a time. Paths given to the host (--host-cmd, --host-extra) use forward slashes: train.py splits
them like a shell line.
"""
from __future__ import annotations

import argparse
import json
import math
import shlex
import subprocess
import sys
import time
from dataclasses import dataclass, field
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROLES = ("citizen", "polluter")


def other(role: str) -> str:
    return "polluter" if role == "citizen" else "citizen"


SCRIPTED_NOISE_ALLOWANCE = 0.03  # assumption: about 1.4 standard errors at n = 500


def keep_new(new_vs_current: float, prev_vs_current: float, new_vs_scripted: float,
             prev_vs_scripted: float) -> bool:
    """Keep the new version only when strictly better against the other role's current network and not
    more than the noise allowance worse against scripted opponents."""
    return (new_vs_current > prev_vs_current
            and new_vs_scripted >= prev_vs_scripted - SCRIPTED_NOISE_ALLOWANCE - 1e-9)


def opponent_mix(current: str | Path, previous: str | Path | None, w_current: float, w_scripted: float,
                 w_previous: float) -> str:
    """The host's --opponents value. An entry with weight 0 (or a previous version that does not exist) is left out."""
    weights = (w_current, w_scripted, w_previous)
    if any(not math.isfinite(w) or w < 0 for w in weights) or sum(weights) <= 0:
        raise ValueError(f"opponent weights must be finite, not negative and not all zero: {weights}")
    parts = []
    for spec, w in ((f"onnx:{Path(current).as_posix()}", w_current), ("scripted", w_scripted),
                    (f"onnx:{Path(previous).as_posix()}" if previous is not None else None, w_previous)):
        if spec is None or w == 0:
            continue
        if "," in spec:
            raise ValueError(f"a comma in the model path cannot go in --opponents: {spec}")
        parts.append(f"{spec}={w:g}")
    return ",".join(parts)


@dataclass
class LeagueConfig:
    out_dir: Path
    turns: int
    turn_steps: int
    citizen_ckpt: Path
    polluter_ckpt: Path
    citizen_onnx: Path | None = None
    polluter_onnx: Path | None = None
    first_learner: str = "citizen"
    w_current: float = 0.6
    w_scripted: float = 0.3
    w_previous: float = 0.1
    seed: int = 1
    eval_seeds: str = "100000-100499"
    extra: dict = field(default_factory=dict)  # free notes stored in league.json (commands, versions)


def pct(x: float) -> str:
    return f"{100 * x:.1f}%"


def side_stats(summary: dict, role: str, onnx) -> dict:
    per_100 = summary.get("per_100", {})
    return {"onnx": str(onnx), "win_rate": summary[f"{role}_win_rate"], "ci95": list(summary[f"{role}_win_ci95"]),
            "matches": summary["matches"], "correct_ejections": per_100.get("correct_ejections", 0.0),
            "wrong_ejections": per_100.get("wrong_ejections", 0.0)}


def spec_of(onnx) -> str:
    return f"onnx:{Path(onnx).as_posix()}"


def evaluate_role(ops, role: str, learner_onnx, opponent_onnx) -> dict:
    """Learner network against the opponent network (None = scripted opponents); returns the learner's stats."""
    learner = spec_of(learner_onnx)
    opponent = "scripted" if opponent_onnx is None else spec_of(opponent_onnx)
    citizen, polluter = (learner, opponent) if role == "citizen" else (opponent, learner)
    return side_stats(ops.evaluate(citizen, polluter), role, learner_onnx)


def run_league(cfg: LeagueConfig, ops) -> dict:
    out = Path(cfg.out_dir)
    out.mkdir(parents=True, exist_ok=True)
    models = out / "models"
    state = {}
    for role, ckpt, onnx in (("citizen", cfg.citizen_ckpt, cfg.citizen_onnx),
                             ("polluter", cfg.polluter_ckpt, cfg.polluter_onnx)):
        if onnx is None:
            onnx = ops.export(Path(ckpt), models / f"start-{role}.onnx")
        state[role] = {"ckpt": Path(ckpt), "onnx": Path(onnx), "history": [Path(onnx)]}

    result = {
        "config": {"turns": cfg.turns, "turn_steps": cfg.turn_steps, "first_learner": cfg.first_learner,
                   "weights": {"current": cfg.w_current, "scripted": cfg.w_scripted, "previous": cfg.w_previous},
                   "eval_seeds": cfg.eval_seeds, "seed": cfg.seed, "out_dir": str(out), **cfg.extra},
        "start": {r: {"ckpt": str(state[r]["ckpt"]), "onnx": str(state[r]["onnx"])} for r in ROLES},
        "turns": [], "final": {},
    }
    scripted_cache: dict[tuple[str, str], dict] = {}

    for turn in range(1, cfg.turns + 1):
        learner = cfg.first_learner if turn % 2 == 1 else other(cfg.first_learner)
        opp = other(learner)
        mine, theirs = state[learner], state[opp]
        opp_previous = theirs["history"][-2] if len(theirs["history"]) >= 2 else None
        mix = opponent_mix(theirs["onnx"], opp_previous, cfg.w_current, cfg.w_scripted, cfg.w_previous)
        run = f"t{turn}-{learner}"
        started = time.time()
        print(f"[league] turn {turn}/{cfg.turns}: train {learner} {cfg.turn_steps} steps from {mine['ckpt']} "
              f"against {mix}", flush=True)
        ckpt = Path(ops.train(learner, cfg.turn_steps, run, mine["ckpt"], mix, cfg.seed + 1000 * (turn - 1)))
        trained_s = time.time() - started
        onnx = Path(ops.export(ckpt, models / f"{run}.onnx"))

        new = evaluate_role(ops, learner, onnx, theirs["onnx"])
        previous = evaluate_role(ops, learner, mine["onnx"], theirs["onnx"])

        def vs_scripted_of(path):
            key = (learner, str(path))
            if key not in scripted_cache:
                scripted_cache[key] = evaluate_role(ops, learner, path, None)
            return scripted_cache[key]

        new_scripted = vs_scripted_of(onnx)
        previous_scripted = vs_scripted_of(mine["onnx"])
        kept = keep_new(new["win_rate"], previous["win_rate"], new_scripted["win_rate"],
                        previous_scripted["win_rate"])
        decision = "kept" if kept else "rejected"
        print(f"[league] turn {turn} {learner}: new {pct(new['win_rate'])} vs previous {pct(previous['win_rate'])} "
              f"against the {opp} network; vs scripted new {pct(new_scripted['win_rate'])} previous "
              f"{pct(previous_scripted['win_rate'])} -> {decision}", flush=True)
        if kept:
            mine["ckpt"], mine["onnx"] = ckpt, onnx
            mine["history"].append(onnx)

        vs_scripted = new_scripted if kept else previous_scripted
        print(f"[league] turn {turn} kept {learner} network vs scripted: {pct(vs_scripted['win_rate'])}", flush=True)

        result["turns"].append({
            "turn": turn, "role": learner, "steps": cfg.turn_steps, "run": run, "opponents": mix,
            "opponent_onnx": str(theirs["onnx"]), "new": new, "previous": previous, "kept": kept,
            "decision": decision, "vs_scripted": vs_scripted,
            "new_vs_scripted": new_scripted, "previous_vs_scripted": previous_scripted, "train_seconds": trained_s,
            "paths": {"ckpt": str(ckpt), "onnx": str(onnx)},
        })
        result["final"] = {r: {"ckpt": str(state[r]["ckpt"]), "onnx": str(state[r]["onnx"])} for r in ROLES}
        write_outputs(out, result)
    result["final"] = {r: {"ckpt": str(state[r]["ckpt"]), "onnx": str(state[r]["onnx"])} for r in ROLES}
    write_outputs(out, result)
    return result


def cell(stats: dict) -> str:
    lo, hi = stats["ci95"]
    return (f"{pct(stats['win_rate'])} [{pct(lo)}, {pct(hi)}] "
            f"(ejections/100 right {stats.get('correct_ejections', 0.0):.1f}, "
            f"wrong {stats.get('wrong_ejections', 0.0):.1f})")


def render_md(result: dict) -> str:
    cfg = result["config"]
    lines = [
        "# League",
        "",
        f"{cfg['turns']} turns of {cfg['turn_steps']} steps, first learner {cfg['first_learner']}, opponent weights "
        f"{cfg['weights']}, eval seeds {cfg['eval_seeds']}. Win rate is the learner role's, 95% Wilson CI.",
        "",
        "| turn | role | steps | new vs current opponent | previous vs current opponent | kept network vs scripted | decision |",
        "|---|---|---|---|---|---|---|",
    ]
    for t in result["turns"]:
        lines.append(f"| {t['turn']} | {t['role']} | {t['steps']} | {cell(t['new'])} | {cell(t['previous'])} | "
                     f"{cell(t['vs_scripted'])} | {t['decision']} |")
    lines += ["", "## Paths", ""]
    for t in result["turns"]:
        lines.append(f"- turn {t['turn']} ({t['role']}, {t['decision']}): checkpoint `{t['paths']['ckpt']}`, "
                     f"onnx `{t['paths']['onnx']}`, opponent `{t['opponent_onnx']}`, mix `{t['opponents']}`")
    lines += ["", "## Final networks", ""]
    for role, paths in result["final"].items():
        lines.append(f"- {role}: onnx `{paths['onnx']}`, checkpoint `{paths['ckpt']}`")
    return "\n".join(lines) + "\n"


def write_outputs(out: Path, result: dict) -> None:
    (out / "league.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
    (out / "league.md").write_text(render_md(result), encoding="utf-8")


class RealOps:
    """Runs train.py, export_onnx.py and the host's --eval, one process at a time."""

    def __init__(self, out_dir: Path, host_cmd: str, host_extra: str, content_dir: str, config: Path,
                 eval_seeds: str, eval_threads: int | None, train_set: list[str], device: str | None,
                 matches: int | None, checkpoint_every: int | None):
        self.out = Path(out_dir)
        self.host_cmd = host_cmd
        self.host_extra = host_extra
        self.content_dir = content_dir
        self.config = config
        self.seeds = eval_seeds
        self.eval_threads = eval_threads
        self.train_set = train_set
        self.device = device
        self.matches = matches
        self.checkpoint_every = checkpoint_every

    def _run(self, cmd: list[str], log: Path) -> None:
        log.parent.mkdir(parents=True, exist_ok=True)
        with open(log, "w", encoding="utf-8") as f:
            proc = subprocess.run(cmd, stdout=f, stderr=subprocess.STDOUT, text=True)
        if proc.returncode != 0:
            tail = "\n".join(log.read_text(encoding="utf-8", errors="replace").splitlines()[-15:])
            raise RuntimeError(f"command failed ({proc.returncode}): {' '.join(cmd)}\nlog {log}:\n{tail}")

    def train(self, role, steps, run, init, opponents, seed) -> Path:
        extra = f"{self.host_extra} --opponents {shlex.quote(opponents)}".strip()
        cmd = [sys.executable, str(HERE / "train.py"), "--config", str(self.config), "--learner", role,
               "--steps", str(steps), "--run", run, "--out-dir", str(self.out / "train"), "--init", str(init),
               "--seed", str(seed), "--host-extra", extra]
        if self.host_cmd:
            cmd += ["--host-cmd", self.host_cmd]
        if self.device:
            cmd += ["--device", self.device]
        if self.matches:
            cmd += ["--matches", str(self.matches)]
        if self.checkpoint_every:
            cmd += ["--checkpoint-every", str(self.checkpoint_every)]
        for item in self.train_set:
            cmd += ["--set", item]
        self._run(cmd, self.out / "logs" / f"{run}-train.log")
        return self.out / "train" / "checkpoints" / run / "final.zip"

    def export(self, ckpt, onnx) -> Path:
        onnx = Path(onnx)
        onnx.parent.mkdir(parents=True, exist_ok=True)
        self._run([sys.executable, str(HERE / "export_onnx.py"), "--checkpoint", str(ckpt), "--out", str(onnx)],
                  self.out / "logs" / f"{onnx.stem}-export.log")
        return onnx

    def evaluate(self, citizen: str, polluter: str) -> dict:
        import evaluate
        rows = evaluate.run_host_eval(shlex.split(self.host_cmd), self.content_dir, citizen, polluter,
                                      evaluate.parse_seeds(self.seeds), threads=self.eval_threads)
        return evaluate.summarise(rows)


def parse_args(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--out-dir", type=Path, required=True)
    p.add_argument("--turns", type=int, required=True)
    p.add_argument("--turn-steps", type=int, required=True)
    p.add_argument("--citizen-ckpt", type=Path, required=True, help="starting citizen checkpoint (.zip, with run.json beside it)")
    p.add_argument("--polluter-ckpt", type=Path, required=True)
    p.add_argument("--citizen-onnx", type=Path, help="ONNX of the starting citizen (default: export the checkpoint)")
    p.add_argument("--polluter-onnx", type=Path)
    p.add_argument("--first-learner", choices=ROLES, default="citizen")
    p.add_argument("--w-current", type=float, default=0.6, help="weight of the other role's current network")
    p.add_argument("--w-scripted", type=float, default=0.3)
    p.add_argument("--w-previous", type=float, default=0.1, help="weight of the other role's previous version")
    p.add_argument("--seed", type=int, default=1)
    p.add_argument("--eval-seeds", default="100000-100499")
    p.add_argument("--eval-threads", type=int, default=2)
    p.add_argument("--host-cmd", help="host command line, also used for --eval (default: host.exe of config.yaml)")
    p.add_argument("--host-extra", default="", help="extra host flags for training only (e.g. '--threads 2')")
    p.add_argument("--content-dir")
    p.add_argument("--config", type=Path, default=HERE / "config.yaml")
    p.add_argument("--train-set", action="append", default=[], metavar="KEY=VALUE", help="train.py --set, repeatable")
    p.add_argument("--device", choices=["auto", "cuda", "cpu"])
    p.add_argument("--matches", type=int, help="host matches in training")
    p.add_argument("--checkpoint-every", type=int)
    return p.parse_args(argv)


def main(argv=None) -> int:
    args = parse_args(argv)
    import yaml
    cfg_file = yaml.safe_load(args.config.read_text(encoding="utf-8"))
    cfg_dir = args.config.resolve().parent
    host_cmd = args.host_cmd or str((cfg_dir / cfg_file["host"]["exe"]).resolve()).replace("\\", "/")
    content = args.content_dir or str((cfg_dir / cfg_file["host"]["content_dir"]).resolve())
    if args.turns < 1 or args.turn_steps < 1:
        print("error: --turns and --turn-steps must be at least 1", file=sys.stderr)
        return 2
    ops = RealOps(args.out_dir, host_cmd, args.host_extra, content, args.config, args.eval_seeds,
                  args.eval_threads, args.train_set, args.device, args.matches, args.checkpoint_every)
    cfg = LeagueConfig(
        out_dir=args.out_dir, turns=args.turns, turn_steps=args.turn_steps, citizen_ckpt=args.citizen_ckpt,
        polluter_ckpt=args.polluter_ckpt, citizen_onnx=args.citizen_onnx, polluter_onnx=args.polluter_onnx,
        first_learner=args.first_learner, w_current=args.w_current, w_scripted=args.w_scripted,
        w_previous=args.w_previous, seed=args.seed, eval_seeds=args.eval_seeds,
        extra={"host_cmd": host_cmd, "host_extra": args.host_extra, "train_set": args.train_set,
               "argv": sys.argv[1:], "started": time.strftime("%Y-%m-%dT%H:%M:%S")})
    try:
        result = run_league(cfg, ops)
    except (RuntimeError, ValueError) as e:
        print(f"error: {e}", file=sys.stderr)
        return 1
    print(f"[league] done: {args.out_dir / 'league.md'}")
    for role, paths in result["final"].items():
        print(f"[league] final {role}: {paths['onnx']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
