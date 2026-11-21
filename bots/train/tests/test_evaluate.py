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
