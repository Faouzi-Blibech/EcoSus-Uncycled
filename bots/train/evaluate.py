"""Evaluate brains with the training host's --eval mode.

    python evaluate.py --citizen scripted --polluter scripted --seeds 100000-100499

Each side is `scripted` or `onnx:<path>`. The host (--eval) prints one JSON line per match with at least
`winner` ("citizens"/"polluters"; any spelling starting with "citizen" or "polluter" is accepted), `reason`
and numeric fields such as tasks_done, meter, correct_ejections. This script prints the win rate per side with
a 95 % Wilson interval and the mean of every numeric field, and writes runs/eval-<name>.json.
"""
from __future__ import annotations

import argparse
import json
import math
import shlex
import subprocess
import sys
from collections import Counter
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
DEFAULT_SEEDS = "100000-100499"  # clear of the training seeds (host trains from seed 1)
Z95 = 1.959963984540054


class EvalUnsupportedError(RuntimeError):
    """The host binary has no usable --eval mode."""


def wilson_interval(successes: int, n: int, z: float = Z95) -> tuple[float, float]:
    if n <= 0:
        return 0.0, 1.0
    p = successes / n
    denom = 1 + z * z / n
    centre = (p + z * z / (2 * n)) / denom
    half = z * math.sqrt(p * (1 - p) / n + z * z / (4 * n * n)) / denom
    return max(0.0, centre - half), min(1.0, centre + half)


def bootstrap_ci(wins: list[int], n_boot: int = 10000, seed: int = 0, alpha: float = 0.05) -> tuple[float, float]:
    """Percentile bootstrap of the win rate, resampling matches with replacement."""
    arr = np.asarray(wins, dtype=np.float64)
    if arr.size == 0:
        return 0.0, 1.0
    rng = np.random.default_rng(seed)
    rates = rng.choice(arr, size=(n_boot, arr.size), replace=True).mean(axis=1)
    lo, hi = np.percentile(rates, [100 * alpha / 2, 100 * (1 - alpha / 2)])
    return float(lo), float(hi)


PROMOTE_MIN_GAIN = 0.10
EJECTION_FIELDS = ("correct_ejections", "wrong_ejections")


def _host_flag(host_cmd: str, flag: str) -> str | None:
    parts = shlex.split(host_cmd)
    return parts[parts.index(flag) + 1] if flag in parts and parts.index(flag) + 1 < len(parts) else None


def promote(nn: dict, baseline: dict, role: str) -> tuple[bool, str]:
    """The promotion rule: >= 10 points above the scripted baseline and both bootstrap CI bounds above it.
    `nn` and `baseline` are summarise() dicts; role polluter reads the Trasher side, citizen the Cleaner side."""
    if role not in ("polluter", "citizen"):
        raise ValueError(f"role must be polluter or citizen, got {role!r}")
    opposing = "citizen" if role == "polluter" else "polluter"
    for name, doc, keys in (("nn eval", nn, (f"{role}_win_rate", f"{role}_win_boot_ci95", role, opposing, "seeds",
                                              "host_cmd")),
                            ("baseline", baseline, (f"{role}_win_rate", "citizen", "polluter", "seeds",
                                                    "host_cmd"))):
        for key in keys:
            if key not in doc:
                return False, f"not comparable: {name} field {key} missing"
    if baseline["citizen"] != "scripted" or baseline["polluter"] != "scripted":
        return False, "not comparable: baseline is not scripted vs scripted"
    if nn[role] == "scripted":
        return False, f"not comparable: nn eval {role} side is scripted, nothing to promote"
    if nn[opposing] != "scripted":
        return False, f"not comparable: nn eval opposing side ({opposing}) is {nn[opposing]}, not scripted"
    if nn["seeds"] != baseline["seeds"]:
        return False, f"not comparable: seeds differ (nn {nn['seeds']}, baseline {baseline['seeds']})"
    for flag in ("--map", "--seats"):
        a, b = _host_flag(nn["host_cmd"], flag), _host_flag(baseline["host_cmd"], flag)
        if a != b:
            return False, f"not comparable: {flag[2:]} differs (nn {a}, baseline {b})"
    nn_rate, base_rate = nn[f"{role}_win_rate"], baseline[f"{role}_win_rate"]
    lo, hi = nn[f"{role}_win_boot_ci95"]
    gain = nn_rate - base_rate
    detail = (f"{role} win {nn_rate:.3f} vs scripted {base_rate:.3f}, gain {gain:+.3f}, "
              f"bootstrap CI [{lo:.3f}, {hi:.3f}]")
    if gain < PROMOTE_MIN_GAIN - 1e-9:
        return False, f"gain below {PROMOTE_MIN_GAIN:.2f}: {detail}"
    if not (lo > base_rate and hi > base_rate):
        return False, f"CI not above the scripted rate: {detail}"
    return True, detail


def parse_seeds(text: str) -> tuple[int, int]:
    first, _, last = text.partition("-")
    lo = int(first)
    hi = int(last) if last else lo
    if hi < lo:
        raise ValueError(f"empty seed range {text!r}")
    return lo, hi


def _side(winner: str) -> str:
    w = str(winner).lower()
    if w.startswith("citizen"):
        return "citizen"
    if w.startswith("polluter"):
        return "polluter"
    return "other"


def summarise(matches: list[dict]) -> dict:
    n = len(matches)
    sides = Counter(_side(m.get("winner", "")) for m in matches)
    cw, pw = sides["citizen"], sides["polluter"]
    numeric: dict[str, list[float]] = {}
    for m in matches:
        for k, v in m.items():
            if k != "seed" and isinstance(v, (int, float)) and not isinstance(v, bool):
                numeric.setdefault(k, []).append(float(v))
    per_100 = {k: 100 * sum(numeric[k]) / n for k in EJECTION_FIELDS if k in numeric and n}
    return {
        "matches": n,
        "citizen_wins": cw,
        "polluter_wins": pw,
        "other": sides["other"],
        "citizen_win_rate": cw / n if n else 0.0,
        "citizen_win_ci95": list(wilson_interval(cw, n)),
        "polluter_win_rate": pw / n if n else 0.0,
        "polluter_win_ci95": list(wilson_interval(pw, n)),
        "citizen_win_boot_ci95": list(bootstrap_ci([1 if _side(m.get("winner", "")) == "citizen" else 0
                                                    for m in matches])),
        "polluter_win_boot_ci95": list(bootstrap_ci([1 if _side(m.get("winner", "")) == "polluter" else 0
                                                     for m in matches])),
        "per_100": per_100,
        "means": {k: sum(v) / len(v) for k, v in numeric.items()},
        "reasons": dict(Counter(str(m.get("reason", "")) for m in matches)),
    }


def git_commit() -> str:
    try:
        out = subprocess.run(["git", "rev-parse", "HEAD"], cwd=HERE, capture_output=True, text=True, timeout=20)
        commit = out.stdout.strip()
        if not commit:
            return "unknown"
        status = subprocess.run(["git", "status", "--porcelain", "--untracked-files=no"], cwd=HERE,
                                capture_output=True, text=True, timeout=20)
        return commit + "-dirty" if status.stdout.strip() else commit
    except (OSError, subprocess.SubprocessError):
        return "unknown"


def _model_info(spec: str) -> dict | None:
    if not spec.startswith("onnx:"):
        return None
    path = Path(spec[len("onnx:"):])
    meta_file = path.with_suffix(".json")
    meta = json.loads(meta_file.read_text(encoding="utf-8")) if meta_file.exists() else {}
    return {"path": str(path), "run_id": meta.get("run_id"), "schema_hash": meta.get("schema_hash")}


def eval_inputs(host_cmd, seeds: tuple[int, int], citizen: str, polluter: str) -> dict:
    """What the eval JSON keeps so the run can be repeated: seeds, host command, git commit, model ids."""
    return {"seeds": f"{seeds[0]}-{seeds[1]}", "host_cmd": " ".join(str(c) for c in host_cmd),
            "git_commit": git_commit(),
            "models": {"citizen": _model_info(citizen), "polluter": _model_info(polluter)}}


def run_host_eval(host_cmd, content_dir, citizen: str, polluter: str, seeds: tuple[int, int],
                  threads: int | None = None, timeout: float | None = None) -> list[dict]:
    cmd = [str(c) for c in host_cmd] + [
        "--eval", "--content", str(content_dir), "--citizen", citizen, "--polluter", polluter,
        "--seeds", f"{seeds[0]}-{seeds[1]}",
    ]
    if threads is not None:
        cmd += ["--threads", str(threads)]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=timeout)
    except FileNotFoundError as e:
        raise EvalUnsupportedError(f"host executable not found: {e}. Build it with Task 4 first.") from e
    rows = []
    for line in proc.stdout.splitlines():
        line = line.strip()
        if line.startswith("{"):
            try:
                rows.append(json.loads(line))
            except json.JSONDecodeError:
                pass
    if proc.returncode != 0 or not rows:
        raise EvalUnsupportedError(
            "the host did not run --eval (exit code %s, %d JSON lines). The host's --eval mode belongs to the "
            "Task 6 host work and may not be built yet. Command: %s\nstderr tail:\n%s"
            % (proc.returncode, len(rows), " ".join(cmd), "\n".join(proc.stderr.splitlines()[-8:])))
    return rows


def main(argv=None) -> int:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--citizen", default="scripted", help="scripted | onnx:<path> | <path>.onnx")
    p.add_argument("--polluter", default="scripted")
    p.add_argument("--seeds", default=DEFAULT_SEEDS)
    p.add_argument("--host-cmd", help="host command line (default: host.exe from config.yaml)")
    p.add_argument("--content-dir")
    p.add_argument("--config", type=Path, default=HERE / "config.yaml")
    p.add_argument("--threads", type=int)
    p.add_argument("--map", help="map id passed to the host (default: the host's default map)")
    p.add_argument("--seats", type=int, help="room size passed to the host")
    p.add_argument("--baseline", type=Path, help="eval JSON of scripted vs scripted, for the promotion verdict")
    p.add_argument("--promote-role", choices=["polluter", "citizen"], help="role whose win rate is judged")
    p.add_argument("--name", help="eval-<name>.json (default: citizen-vs-polluter)")
    p.add_argument("--out-dir", type=Path, default=HERE / "runs")
    args = p.parse_args(argv)

    import yaml
    cfg = yaml.safe_load(args.config.read_text(encoding="utf-8"))
    cfg_dir = args.config.resolve().parent
    host_cmd = shlex.split(args.host_cmd) if args.host_cmd else [str((cfg_dir / cfg["host"]["exe"]).resolve())]
    content = args.content_dir or str((cfg_dir / cfg["host"]["content_dir"]).resolve())

    def norm(side: str) -> str:
        return side if side == "scripted" or side.startswith("onnx:") else f"onnx:{side}"

    citizen, polluter = norm(args.citizen), norm(args.polluter)
    seeds = parse_seeds(args.seeds)
    if args.map:
        host_cmd += ["--map", args.map]
    if args.seats:
        host_cmd += ["--seats", str(args.seats)]
    if bool(args.baseline) != bool(args.promote_role):
        print("error: --baseline and --promote-role go together", file=sys.stderr)
        return 2
    try:
        rows = run_host_eval(host_cmd, content, citizen, polluter, seeds, threads=args.threads)
    except EvalUnsupportedError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    summary = summarise(rows)
    summary.update({"citizen": citizen, "polluter": polluter})
    summary.update(eval_inputs(host_cmd, seeds, citizen, polluter))
    verdict = None
    if args.baseline:
        baseline = json.loads(args.baseline.read_text(encoding="utf-8"))
        verdict = promote(summary, baseline, args.promote_role)
        summary["promote"] = {"role": args.promote_role, "ok": verdict[0], "reason": verdict[1],
                              "baseline": str(args.baseline)}

    name = args.name or f"{Path(citizen.split(':')[-1]).stem}-vs-{Path(polluter.split(':')[-1]).stem}"
    args.out_dir.mkdir(parents=True, exist_ok=True)
    out = args.out_dir / f"eval-{name}.json"
    out.write_text(json.dumps(summary, indent=2), encoding="utf-8")

    def pct(x):
        return f"{100 * x:.1f}%"

    print(f"matches {summary['matches']}  seeds {summary['seeds']}  citizen={citizen}  polluter={polluter}")
    for side, label in (("citizen", "cleaner (citizen)"), ("polluter", "trasher (polluter)")):
        w, b = summary[f"{side}_win_ci95"], summary[f"{side}_win_boot_ci95"]
        print(f"{label} win {pct(summary[f'{side}_win_rate'])}  Wilson [{pct(w[0])}, {pct(w[1])}]  "
              f"bootstrap [{pct(b[0])}, {pct(b[1])}]")
    for k, v in summary["per_100"].items():
        print(f"per 100 matches {k}: {v:.1f}")
    for k, v in summary["means"].items():
        if k not in EJECTION_FIELDS:
            print(f"mean {k}: {v:.3f}")
    print(f"reasons: {summary['reasons']}")
    if verdict:
        print(f"PROMOTE: {'yes' if verdict[0] else 'no'} ({verdict[1]})")
    print(f"wrote {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
