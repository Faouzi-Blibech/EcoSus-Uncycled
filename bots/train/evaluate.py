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
    return {
        "matches": n,
        "citizen_wins": cw,
        "polluter_wins": pw,
        "other": sides["other"],
        "citizen_win_rate": cw / n if n else 0.0,
        "citizen_win_ci95": list(wilson_interval(cw, n)),
        "polluter_win_rate": pw / n if n else 0.0,
        "polluter_win_ci95": list(wilson_interval(pw, n)),
        "means": {k: sum(v) / len(v) for k, v in numeric.items()},
        "reasons": dict(Counter(str(m.get("reason", "")) for m in matches)),
    }


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
    try:
        rows = run_host_eval(host_cmd, content, citizen, polluter, seeds, threads=args.threads)
    except EvalUnsupportedError as e:
        print(f"error: {e}", file=sys.stderr)
        return 2
    summary = summarise(rows)
    summary.update({"citizen": citizen, "polluter": polluter, "seeds": f"{seeds[0]}-{seeds[1]}"})

    name = args.name or f"{Path(citizen.split(':')[-1]).stem}-vs-{Path(polluter.split(':')[-1]).stem}"
    args.out_dir.mkdir(parents=True, exist_ok=True)
    out = args.out_dir / f"eval-{name}.json"
    out.write_text(json.dumps(summary, indent=2), encoding="utf-8")

    def pct(x):
        return f"{100 * x:.1f}%"

    print(f"matches {summary['matches']}  seeds {summary['seeds']}  citizen={citizen}  polluter={polluter}")
    print(f"citizen win  {pct(summary['citizen_win_rate'])}  95% CI [{pct(summary['citizen_win_ci95'][0])}, "
          f"{pct(summary['citizen_win_ci95'][1])}]")
    print(f"polluter win {pct(summary['polluter_win_rate'])}  95% CI [{pct(summary['polluter_win_ci95'][0])}, "
          f"{pct(summary['polluter_win_ci95'][1])}]")
    for k, v in summary["means"].items():
        print(f"mean {k}: {v:.3f}")
    print(f"reasons: {summary['reasons']}")
    print(f"wrote {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
