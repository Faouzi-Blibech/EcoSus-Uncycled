import json
from pathlib import Path

import pytest

import league


def summary(citizen_rate, polluter_rate=None, n=500):
    cw = round(citizen_rate * n)
    pw = n - cw if polluter_rate is None else round(polluter_rate * n)
    return {"matches": n, "citizen_wins": cw, "polluter_wins": pw, "other": n - cw - pw,
            "citizen_win_rate": cw / n, "citizen_win_ci95": [cw / n - 0.04, cw / n + 0.04],
            "polluter_win_rate": pw / n, "polluter_win_ci95": [pw / n - 0.04, pw / n + 0.04]}


class FakeOps:
    """Records every call. The win rate of the learner network is read from `rates` by its onnx file name."""

    def __init__(self, rates):
        self.rates = rates
        self.role = None
        self.trained = []
        self.exported = []
        self.evals = []

    def train(self, role, steps, run, init, opponents, seed):
        self.role = role
        self.trained.append({"role": role, "steps": steps, "run": run, "init": str(init), "opponents": opponents,
                             "seed": seed})
        return Path(f"ckpt/{run}/final.zip")

    def export(self, ckpt, onnx):
        self.exported.append((str(ckpt), str(onnx)))
        return Path(onnx)

    def evaluate(self, citizen, polluter):
        self.evals.append((citizen, polluter))
        learner = citizen if self.role == "citizen" else polluter
        rate = self.rates[Path(learner.split(":", 1)[-1]).name]
        return summary(rate) if self.role == "citizen" else summary(1 - rate, rate)


def make_cfg(tmp_path, turns=3, first="polluter", **kw):
    return league.LeagueConfig(
        out_dir=tmp_path, turns=turns, turn_steps=1000, first_learner=first,
        citizen_ckpt=Path("start/cit.zip"), polluter_ckpt=Path("start/pol.zip"),
        citizen_onnx=Path("start/cit.onnx"), polluter_onnx=Path("start/pol.onnx"),
        w_current=0.6, w_scripted=0.3, w_previous=0.1, seed=1, **kw)


def test_keep_new_needs_better_vs_current_and_not_worse_vs_scripted():
    assert league.keep_new(0.51, 0.50, 0.60, 0.60) is True
    assert league.keep_new(0.50, 0.50, 0.60, 0.60) is False  # a tie vs the current opponent is a rejection
    assert league.keep_new(0.60, 0.50, 0.56, 0.60) is False  # 4 points worse vs scripted
    assert league.keep_new(0.60, 0.50, 0.58, 0.60) is True   # within the 0.03 noise allowance


def test_opponent_mix_drops_previous_when_there_is_none():
    assert league.opponent_mix("cur.onnx", "prev.onnx", 0.6, 0.3, 0.1) == \
        "onnx:cur.onnx=0.6,scripted=0.3,onnx:prev.onnx=0.1"
    assert league.opponent_mix("cur.onnx", None, 0.6, 0.3, 0.1) == "onnx:cur.onnx=0.6,scripted=0.3"
    assert league.opponent_mix("cur.onnx", "prev.onnx", 0.7, 0.3, 0.0) == "onnx:cur.onnx=0.7,scripted=0.3"


def test_turns_alternate_and_better_network_is_kept(tmp_path):
    # every new network wins 60 %, every old one 50 %
    rates = {"t1-polluter.onnx": 0.6, "t2-citizen.onnx": 0.6, "t3-polluter.onnx": 0.7, "pol.onnx": 0.5, "cit.onnx": 0.5}
    ops = FakeOps(rates)
    result = league.run_league(make_cfg(tmp_path), ops)

    assert [t["role"] for t in result["turns"]] == ["polluter", "citizen", "polluter"]
    assert [t["kept"] for t in result["turns"]] == [True, True, True]
    # turn 1: no previous citizen version yet, so the mix is current + scripted only
    assert "pol.zip" in ops.trained[0]["init"]
    assert ops.trained[0]["opponents"].endswith("cit.onnx=0.6,scripted=0.3")
    # turn 2 learner is the citizen against the NEW polluter; the previous polluter is the old league member
    assert "t1-polluter.onnx=0.6" in ops.trained[1]["opponents"]
    assert ops.trained[1]["opponents"].endswith("pol.onnx=0.1")
    # turn 3 continues from the kept turn-1 polluter checkpoint
    assert "t1-polluter" in ops.trained[2]["init"]
    assert result["final"]["polluter"]["onnx"].endswith("t3-polluter.onnx")
    assert result["final"]["citizen"]["onnx"].endswith("t2-citizen.onnx")


def test_worse_or_equal_network_is_rejected_and_previous_stays(tmp_path):
    rates = {"t1-polluter.onnx": 0.40, "pol.onnx": 0.50, "t2-citizen.onnx": 0.50, "cit.onnx": 0.50}
    ops = FakeOps(rates)
    result = league.run_league(make_cfg(tmp_path, turns=2), ops)
    t1, t2 = result["turns"]
    assert t1["kept"] is False and t1["decision"] == "rejected"
    assert t2["kept"] is False  # a tie is a rejection
    assert result["final"]["polluter"]["onnx"].endswith("pol.onnx")
    assert result["final"]["polluter"]["ckpt"].endswith("pol.zip")
    # the citizen turn still faces the starting polluter, not the rejected one
    assert "t1-polluter" not in ops.trained[1]["opponents"]
    # kept-network vs scripted is the previous (kept) network when the new one was rejected
    assert t1["vs_scripted"]["onnx"].endswith("pol.onnx")


def test_new_and_previous_face_the_same_opponent_and_scripted_is_for_the_kept_one(tmp_path):
    rates = {"t1-polluter.onnx": 0.6, "pol.onnx": 0.5}
    ops = FakeOps(rates)
    result = league.run_league(make_cfg(tmp_path, turns=1), ops)
    vs_cit = [e for e in ops.evals if e[0].endswith("cit.onnx")]
    assert len(vs_cit) == 2  # new and previous polluter, both against the citizen's current network
    assert {e[1].split(":", 1)[-1].split("/")[-1].split("\\")[-1] for e in vs_cit} == {"t1-polluter.onnx", "pol.onnx"}
    scripted = [e for e in ops.evals if e[0] == "scripted"]
    assert len(scripted) == 2  # new and previous polluter, for the scripted half of the gate
    assert any(e[1].endswith("t1-polluter.onnx") for e in scripted)
    assert result["turns"][0]["vs_scripted"]["onnx"].endswith("t1-polluter.onnx")


def test_league_json_and_md_are_written_every_turn(tmp_path):
    rates = {"t1-polluter.onnx": 0.6, "t2-citizen.onnx": 0.4, "pol.onnx": 0.5, "cit.onnx": 0.5}
    league.run_league(make_cfg(tmp_path, turns=2), FakeOps(rates))
    data = json.loads((tmp_path / "league.json").read_text(encoding="utf-8"))
    assert set(data) >= {"config", "turns", "final"}
    assert set(data["config"]) >= {"turns", "turn_steps", "weights", "eval_seeds", "first_learner"}
    turn = data["turns"][0]
    for key in ("turn", "role", "steps", "run", "opponents", "opponent_onnx", "new", "previous", "kept", "decision",
                "vs_scripted", "paths"):
        assert key in turn, key
    for side in ("new", "previous"):
        assert set(turn[side]) >= {"onnx", "win_rate", "ci95", "matches"}
    assert set(turn["vs_scripted"]) >= {"onnx", "win_rate", "ci95", "matches"}
    assert set(turn["paths"]) >= {"ckpt", "onnx"}
    md = (tmp_path / "league.md").read_text(encoding="utf-8")
    assert "kept" in md and "rejected" in md
    assert md.count("\n| ") >= 3  # header, separator-adjacent row and one row per turn
    assert "t2-citizen" in md


def test_bad_weights_are_refused():
    with pytest.raises(ValueError):
        league.opponent_mix("c.onnx", None, 0.0, 0.0, 0.0)
    with pytest.raises(ValueError):
        league.opponent_mix("c.onnx", None, -1.0, 1.0, 0.0)


def test_worse_vs_scripted_is_rejected_even_if_better_vs_current(tmp_path):
    class Ops(FakeOps):
        def evaluate(self, citizen, polluter):
            new = "t1-polluter" in polluter
            if citizen == "scripted":
                return summary(0.5, 0.40 if new else 0.50)  # polluter 10 points worse vs scripted
            return summary(0.5, 0.60 if new else 0.50)
    ops = Ops({})
    ops.role = "polluter"
    result = league.run_league(make_cfg(tmp_path, turns=1), ops)
    t = result["turns"][0]
    assert t["kept"] is False
    assert t["new"]["win_rate"] > t["previous"]["win_rate"]
    assert t["new_vs_scripted"]["win_rate"] < t["previous_vs_scripted"]["win_rate"]


def test_turn_report_lists_ejections_right_and_wrong(tmp_path):
    ops = FakeOps({"t1-polluter.onnx": 0.6, "pol.onnx": 0.5})
    result = league.run_league(make_cfg(tmp_path, turns=1), ops)
    t = result["turns"][0]
    for key in ("vs_scripted", "new"):
        assert "correct_ejections" in t[key] and "wrong_ejections" in t[key]
    md = (tmp_path / "league.md").read_text(encoding="utf-8")
    assert "ejections" in md
