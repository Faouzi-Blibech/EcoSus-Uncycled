import json
import sys

import pytest

import evaluate


def test_wilson_interval_known_values():
    lo, hi = evaluate.wilson_interval(50, 100)
    assert lo == pytest.approx(0.4038, abs=1e-3) and hi == pytest.approx(0.5962, abs=1e-3)
    lo, hi = evaluate.wilson_interval(0, 10)
    assert lo == 0.0 and hi == pytest.approx(0.2775, abs=1e-3)
    lo, hi = evaluate.wilson_interval(10, 10)
    assert hi == pytest.approx(1.0) and lo == pytest.approx(0.7225, abs=1e-3)
    assert evaluate.wilson_interval(0, 0) == (0.0, 1.0)


def test_summarise_counts_wins_and_means():
    lines = [
        {"seed": 1, "winner": "citizens", "reason": "tasks_done", "tasks_done": 10, "meter": 0.2, "correct_ejections": 1},
        {"seed": 2, "winner": "polluters", "reason": "pollution_full", "tasks_done": 4, "meter": 1.0, "correct_ejections": 0},
        {"seed": 3, "winner": "citizens", "reason": "deadline", "tasks_done": 6, "meter": 0.4, "correct_ejections": 2},
    ]
    s = evaluate.summarise(lines)
    assert s["matches"] == 3
    assert s["citizen_wins"] == 2 and s["polluter_wins"] == 1
    assert s["citizen_win_rate"] == pytest.approx(2 / 3)
    assert s["means"]["tasks_done"] == pytest.approx(20 / 3)
    assert s["reasons"] == {"tasks_done": 1, "pollution_full": 1, "deadline": 1}
    assert "seed" not in s["means"]


def test_parse_seed_range():
    assert evaluate.parse_seeds("1000-1499") == (1000, 1499)
    assert evaluate.parse_seeds("7") == (7, 7)
    with pytest.raises(ValueError):
        evaluate.parse_seeds("9-3")


def test_default_eval_seeds_are_clear_of_training_seeds():
    lo, hi = evaluate.parse_seeds(evaluate.DEFAULT_SEEDS)
    assert (lo, hi) == (100000, 100499)


def test_host_without_eval_mode_gives_a_clear_error(fake_host_cmd):
    with pytest.raises(evaluate.EvalUnsupportedError, match="--eval"):
        evaluate.run_host_eval(fake_host_cmd, content_dir="x", citizen="scripted", polluter="scripted",
                               seeds=(1, 5))


def _summary(side: str, wins: int, n: int, ci: tuple[float, float] | None = None) -> dict:
    s = evaluate.summarise([{"winner": f"{side}s" if i < wins else "other"} for i in range(n)])
    if ci is not None:
        s[f"{side}_win_boot_ci95"] = list(ci)
    return s


def test_bootstrap_ci_brackets_rate():
    wins = [1] * 300 + [0] * 200
    lo, hi = evaluate.bootstrap_ci(wins)
    assert lo < 0.6 < hi
    assert 0.07 <= hi - lo <= 0.10
    assert evaluate.bootstrap_ci(wins) == (lo, hi)  # seeded, repeatable


def test_promote_needs_ten_points():
    base = _summary("polluter", 200, 500)
    nn = _summary("polluter", 245, 500)  # 0.49 vs 0.40
    ok, reason = evaluate.promote(nn, base, "polluter")
    assert ok is False and "0.10" in reason


def test_promote_needs_ci_above_base():
    base = _summary("polluter", 200, 500)
    nn = _summary("polluter", 255, 500, ci=(0.39, 0.55))  # 0.51, lower bound under 0.40
    ok, reason = evaluate.promote(nn, base, "polluter")
    assert ok is False and "CI" in reason


def test_promote_passes():
    base = _summary("polluter", 200, 500)
    nn = _summary("polluter", 275, 500)  # 0.55
    ok, reason = evaluate.promote(nn, base, "polluter")
    assert ok is True, reason
    # the citizen role reads the citizen side
    assert evaluate.promote(_summary("citizen", 275, 500), _summary("citizen", 200, 500), "citizen")[0] is True
    assert evaluate.promote(nn, base, "citizen")[0] is False


def test_summary_reports_ejections_per_100():
    rows = [{"winner": "citizens", "correct_ejections": 1, "wrong_ejections": 0, "deliveries": 4},
            {"winner": "polluters", "correct_ejections": 0, "wrong_ejections": 1, "deliveries": 2},
            {"winner": "citizens", "correct_ejections": 2, "wrong_ejections": 0, "deliveries": 3},
            {"winner": "polluters", "correct_ejections": 1, "wrong_ejections": 1, "deliveries": 3}]
    s = evaluate.summarise(rows)
    assert s["per_100"]["correct_ejections"] == pytest.approx(100.0)
    assert s["per_100"]["wrong_ejections"] == pytest.approx(50.0)
    assert s["means"]["deliveries"] == pytest.approx(3.0)
    assert len(s["polluter_win_boot_ci95"]) == 2 and len(s["citizen_win_boot_ci95"]) == 2


def test_eval_json_keeps_inputs(tmp_path):
    onnx = tmp_path / "t1.onnx"
    onnx.write_bytes(b"x")
    onnx.with_suffix(".json").write_text(json.dumps({"run_id": "r-7", "schema_hash": "abc123"}), encoding="utf-8")
    rec = evaluate.eval_inputs(["host.exe", "--map", "greybox_01", "--seats", "6"], (100000, 100499),
                               f"onnx:{onnx}", "scripted")
    assert rec["seeds"] == "100000-100499"
    assert "--map" in rec["host_cmd"] and "--seats" in rec["host_cmd"]
    assert rec["git_commit"]
    assert rec["models"]["citizen"] == {"path": str(onnx), "run_id": "r-7", "schema_hash": "abc123"}
    assert rec["models"]["polluter"] is None
