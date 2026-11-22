"""Scan links, points and the scan endpoints (plan L1). Fake detector: no model weights needed."""
import os
import unittest
import uuid
from datetime import date, datetime, timedelta, timezone

os.environ.setdefault("ECO_DB", ":memory:")
os.environ.setdefault("ECO_DISABLE_CLIP", "1")

from fastapi.testclient import TestClient  # noqa: E402

import app as service_app  # noqa: E402
from ecoverify import config, rules  # noqa: E402
from ecoverify.service import Verifier  # noqa: E402
from ecoverify.store import Store  # noqa: E402
from ecoverify.tests.test_verifier import NOW, FakeDetector, jpeg, photo  # noqa: E402

TTL = 120


class TokenTests(unittest.TestCase):
    def setUp(self):
        self.store = Store(":memory:")

    def tearDown(self):
        self.store.close()

    def test_token_is_single_use(self):
        t = self.store.issue_token("p1", 1000.0, TTL)
        self.assertEqual(self.store.redeem_token(t, 1010.0), "p1")
        self.assertIsNone(self.store.redeem_token(t, 1011.0))

    def test_token_expires_after_ttl(self):
        t = self.store.issue_token("p1", 1000.0, TTL)
        self.assertIsNone(self.store.redeem_token(t, 1121.0))

    def test_peek_does_not_use_the_link(self):
        t = self.store.issue_token("p1", 1000.0, TTL)
        self.assertEqual(self.store.token_player(t, 1001.0), "p1")
        self.assertEqual(self.store.redeem_token(t, 1002.0), "p1")

    def test_released_link_can_be_used_again_until_it_expires(self):
        t = self.store.issue_token("p1", 1000.0, TTL)
        self.store.redeem_token(t, 1001.0)
        self.store.release_token(t)
        self.assertEqual(self.store.redeem_token(t, 1002.0), "p1")
        self.store.release_token(t)
        self.assertIsNone(self.store.redeem_token(t, 1200.0))

    def test_unknown_token(self):
        self.assertIsNone(self.store.redeem_token("nope", 1000.0))


class PointsTests(unittest.TestCase):
    def test_points(self):
        cases = [([], 0), ([1], 10), ([3], 30), ([4], 30), ([4, 1, 2], 60)]
        for per_day, want in cases:
            with self.subTest(per_day=per_day):
                self.assertEqual(rules.points(per_day, 10, 3), want)

    def test_tunis_day_crosses_midnight(self):
        # Tunis is UTC+1 all year: the game day changes at 23:00 UTC.
        cases = [(datetime(2026, 11, 21, 22, 59, tzinfo=timezone.utc), date(2026, 11, 21)),
                 (datetime(2026, 11, 21, 23, 0, tzinfo=timezone.utc), date(2026, 11, 22))]
        for when, want in cases:
            with self.subTest(when=when):
                self.assertEqual(rules.day_key(when, config.TUNING["day_utc_offset_hours"]), want)

    def test_streak_counts_consecutive_days(self):
        streak, last = 0, None
        for day, want in [(date(2026, 11, 21), 1), (date(2026, 11, 22), 2), (date(2026, 11, 24), 1)]:
            streak, last = rules.next_streak(last, day, streak), day
            self.assertEqual(streak, want)


class ScanEndpointTests(unittest.TestCase):
    def setUp(self):
        self.store = Store(":memory:")
        self.original = service_app.verifier
        service_app.verifier = Verifier(self.store, FakeDetector("bottle"), None, config.TUNING)
        self.client = TestClient(service_app.app)
        self.pid = str(uuid.uuid4())

    def tearDown(self):
        service_app.verifier = self.original
        self.store.close()

    def start(self, **headers):
        r = self.client.post("/scan/start", json={"player_id": self.pid}, headers=headers)
        self.assertEqual(r.status_code, 200, r.text)
        return r.json()

    def upload(self, token, seed=1):
        return self.client.post("/scan/upload", data={"token": token},
                                files={"file": ("scan.jpg", jpeg(photo(seed)), "image/jpeg")})

    def test_start_gives_link_qr_and_ttl(self):
        s = self.start()
        self.assertIn(f"/scan?t={s['token']}", s["url"])
        self.assertIn(f"/scan/qr?t={s['token']}", s["qr_url"])
        self.assertEqual(s["expires_s"], config.TUNING["scan_token_ttl_s"])

    def test_link_uses_the_tunnel_address(self):
        s = self.start(**{"x-forwarded-proto": "https", "x-forwarded-host": "abc.trycloudflare.com"})
        self.assertTrue(s["url"].startswith("https://abc.trycloudflare.com/scan?t="), s["url"])

    def test_start_rejects_a_bad_player_id(self):
        r = self.client.post("/scan/start", json={"player_id": "not-a-uuid"})
        self.assertEqual(r.status_code, 400)

    def test_scan_page(self):
        token = self.start()["token"]
        ok = self.client.get(f"/scan?t={token}")
        self.assertEqual(ok.status_code, 200)
        self.assertIn('"valid": true', ok.text)
        self.assertIn('capture="environment"', ok.text)
        self.assertIn('"valid": false', self.client.get("/scan?t=bogus").text)

    def test_scan_page_escapes_the_token(self):
        page = self.client.get("/scan", params={"t": "</script><script>alert(1)</script>"}).text
        self.assertNotIn("</script><script>alert(1)", page)

    def test_qr_png(self):
        token = self.start()["token"]
        r = self.client.get(f"/scan/qr?t={token}")
        self.assertEqual((r.status_code, r.headers["content-type"]), (200, "image/png"))
        self.assertTrue(r.content.startswith(b"\x89PNG"))
        self.assertEqual(self.client.get("/scan/qr?t=bogus").status_code, 410)

    def test_upload_counts_scan_and_uses_the_link_up(self):
        token = self.start()["token"]
        r = self.upload(token)  # no EXIF in the photo: the link replaces the time check
        self.assertEqual(r.status_code, 200, r.text)
        body = r.json()
        self.assertTrue(body["verified"], body["message"])
        p = body["profile"]
        self.assertEqual((p["points"], p["streak"], p["scans_today"]), (10, 1, 1))
        self.assertEqual(p["new_skins"], ["eco_novice"])
        self.assertEqual(self.upload(token, seed=2).status_code, 410)

    def test_rejected_photo_gives_the_link_back(self):
        token = self.start()["token"]
        service_app.verifier.detector = FakeDetector("person")
        first = self.upload(token).json()
        self.assertFalse(first["verified"])
        self.assertEqual(first["profile"]["points"], 0)
        service_app.verifier.detector = FakeDetector("bottle")
        self.assertTrue(self.upload(token).json()["verified"])

    def test_expired_link_is_refused(self):
        token = self.store.issue_token(self.pid, datetime.now(timezone.utc).timestamp() - 500, TTL)
        r = self.upload(token)
        self.assertEqual(r.status_code, 410)
        self.assertIn("expired", r.json()["detail"])

    def test_profile_route_and_points_cap(self):
        v = service_app.verifier
        for seed in range(4):  # 4 different items the same day: only 3 score
            self.assertTrue(v.verify(jpeg(photo(seed + 10)), self.pid, NOW, skip_exif=True)["verified"])
        p = self.client.get(f"/profile/{self.pid}").json()
        self.assertEqual(p["points"], 30)
        self.assertEqual(p, self.client.get(f"/players/{self.pid}").json())


if __name__ == "__main__":
    unittest.main()
