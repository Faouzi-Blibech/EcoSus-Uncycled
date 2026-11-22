import unittest
from datetime import date, datetime, timezone

from ecoverify import config, rules

D = date(2026, 11, 21)
# The rules take the catalogue as data; this test list stays put when content/skins.json changes.
SKINS = [{"id": "a", "required_streak": 1}, {"id": "b", "required_streak": 3},
         {"id": "c", "required_streak": 7}, {"id": "d", "required_streak": 14}]


class StreakTests(unittest.TestCase):
    def test_next_streak(self):
        cases = [
            # last_day,          streak, expected
            (None,               0,      1),   # first ever
            (D,                  4,      4),   # second photo same day
            (date(2026, 11, 20), 4,      5),   # yesterday -> +1
            (date(2026, 11, 19), 4,      1),   # missed a day -> reset
        ]
        for last, streak, want in cases:
            with self.subTest(last=last, streak=streak):
                self.assertEqual(rules.next_streak(last, D, streak), want)

    def test_live_streak(self):
        cases = [(None, 0, 0), (D, 3, 3), (date(2026, 11, 20), 3, 3), (date(2026, 11, 19), 3, 0)]
        for last, streak, want in cases:
            with self.subTest(last=last):
                self.assertEqual(rules.live_streak(last, D, streak), want)

    def test_day_key_uses_server_offset(self):
        late = datetime(2026, 11, 21, 23, 30, tzinfo=timezone.utc)
        self.assertEqual(rules.day_key(late, 0), date(2026, 11, 21))
        self.assertEqual(rules.day_key(late, 1), date(2026, 11, 22))


class UnlockTests(unittest.TestCase):
    def test_new_unlocks(self):
        cases = [
            (1, set(), ["a"]),
            (3, {"a"}, ["b"]),
            (7, set(), ["a", "b", "c"]),
            (2, {"a"}, []),
        ]
        for streak, have, want in cases:
            with self.subTest(streak=streak):
                self.assertEqual([s["id"] for s in rules.new_unlocks(streak, have, SKINS)], want)

    def test_next_skin(self):
        self.assertEqual(rules.next_skin(set(), SKINS)["id"], "a")
        self.assertIsNone(rules.next_skin({s["id"] for s in SKINS}, SKINS))


class Co2Tests(unittest.TestCase):
    def test_award(self):
        cap, depot_max = 1000, 1
        cases = [
            # kind,     item, streak_today, depot_today, awarded, has_note
            ("streak",  82,   0,            0,           82,      False),
            ("streak",  82,   950,          0,           50,      True),   # trimmed to the cap
            ("streak",  82,   1000,         0,           0,       True),   # cap reached
            ("depot",   15000, 1000,        0,           15000,   False),  # depot ignores the streak cap
            ("depot",   15000, 0,           1,           0,       True),   # second depot item today
        ]
        for kind, item, st, dep, want, has_note in cases:
            with self.subTest(kind=kind, st=st, dep=dep):
                got, note = rules.award_co2(kind, item, st, dep, cap, depot_max)
                self.assertEqual(got, want)
                self.assertEqual(note is not None, has_note)


class ContentTests(unittest.TestCase):
    def test_every_detection_maps_to_a_taxonomy_item(self):
        config._check(config.CONFIG)  # raises on a bad mapping

    def test_e_waste_is_never_a_streak_item(self):
        for coco, e in config.DETECTIONS.items():
            if e["stream"] == "ELECTRONICS (E-WASTE)":
                self.assertEqual(e["kind"], "depot", coco)


if __name__ == "__main__":
    unittest.main()
