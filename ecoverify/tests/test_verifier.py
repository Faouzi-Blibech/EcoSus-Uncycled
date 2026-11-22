"""Pipeline tests with a fake detector/embedder: no model weights needed."""
import io
import unittest
import uuid
from datetime import datetime, timedelta, timezone

import numpy as np
from PIL import Image, ImageDraw

from ecoverify import config
from ecoverify.exif import check_live
from ecoverify.service import Verifier
from ecoverify.store import Store
from ecoverify.vision import Detection, hash_variants, hashes, same_photo

NOW = datetime(2026, 11, 21, 10, 0, tzinfo=timezone.utc)
BOX = (100, 60, 300, 380)


def photo(seed: int = 1, size=(400, 440)) -> Image.Image:
    """A random-texture 'item' so different seeds hash differently."""
    rng = np.random.default_rng(seed)
    img = Image.new("RGB", size, (230, 230, 230))
    patch = Image.fromarray(rng.integers(0, 255, (BOX[3] - BOX[1], BOX[2] - BOX[0], 3), dtype=np.uint8))
    img.paste(patch.resize((BOX[2] - BOX[0], BOX[3] - BOX[1])), BOX[:2])
    ImageDraw.Draw(img).ellipse((150, 100, 250, 200), fill=(seed * 40 % 255, 0, 0))
    return img


def jpeg(img: Image.Image) -> bytes:
    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=95)
    return buf.getvalue()


class FakeDetector:
    def __init__(self, cls="bottle", conf=0.9, box=BOX):
        self.cls, self.conf, self.box = cls, conf, box

    def detect(self, image, conf):
        if self.cls is None:
            return []
        contour = [[self.box[0], self.box[1]], [self.box[2], self.box[1]], [self.box[2], self.box[3]], [self.box[0], self.box[3]]]
        return [Detection(self.cls, self.conf, self.box, contour)]


class FakeEmbedder:
    """Returns a fixed embedding (same item) and picks subtype index `pick`."""
    def __init__(self, vec=None, pick=0, prob=0.9):
        self.vec = vec if vec is not None else np.eye(512, dtype=np.float32)[0]
        self.pick, self.prob = pick, prob

    def analyse(self, crop, labels=None):
        return self.vec, ((self.pick, self.prob) if labels else None)


_OPEN: list[Store] = []


def make(detector=None, embedder=None, taco=None, **tuning):
    t = dict(config.TUNING, **tuning)
    _OPEN.append(Store(":memory:"))
    return Verifier(_OPEN[-1], detector or FakeDetector(), embedder, t, taco=taco)


def pid():
    return str(uuid.uuid4())


def unlocked_at(streak: int) -> list[str]:
    """Skin ids a streak of exactly `streak` unlocks, from content/skins.json (data, not test code)."""
    return [s["id"] for s in config.SKINS if streak - 1 < s["required_streak"] <= streak]


class VerifyTests(unittest.TestCase):
    def tearDown(self):
        while _OPEN:
            _OPEN.pop().close()

    def verify(self, v, player, img, now=NOW):
        return v.verify(jpeg(img), player, now, skip_exif=True)

    def test_accepts_and_counts_streak_on_server(self):
        v, p = make(), pid()
        r = self.verify(v, p, photo(1))
        self.assertTrue(r["verified"], r)
        self.assertEqual(r["player"]["streak"], 1)
        self.assertEqual([s["id"] for s in r["player"]["new_unlocks"]], unlocked_at(1))
        self.assertEqual(len(r["contour"]), 4)
        for x, y in r["contour"]:
            self.assertTrue(0 <= x <= 1 and 0 <= y <= 1)

    def test_streak_grows_over_days_and_unlocks(self):
        v, p = make(), pid()
        for day in range(3):
            r = self.verify(v, p, photo(10 + day), NOW + timedelta(days=day))
            self.assertTrue(r["verified"], r)
        self.assertEqual(r["player"]["streak"], 3)
        self.assertEqual([s["id"] for s in r["player"]["new_unlocks"]], unlocked_at(3))

    def test_same_photo_rejected_even_rotated(self):
        v, p = make(), pid()
        self.assertTrue(self.verify(v, p, photo(1))["verified"])
        self.assertEqual(self.verify(v, p, photo(1))["reason"], "duplicate")
        # the stored hash must also match a rotated or mirrored crop of the same item
        ph, dh = hashes(photo(1).crop(BOX))
        for turned in (photo(1).crop(BOX).rotate(90, expand=True), photo(1).crop(BOX).transpose(Image.FLIP_LEFT_RIGHT)):
            self.assertTrue(same_photo(hash_variants(turned), ph, dh, 10))
        self.assertFalse(same_photo(hash_variants(photo(5).crop(BOX)), ph, dh, 10))

    def test_duplicates_are_per_player(self):
        v = make()
        self.assertTrue(self.verify(v, pid(), photo(1))["verified"])
        self.assertTrue(self.verify(v, pid(), photo(1))["verified"])

    def test_same_item_other_angle_rejected_same_day_only(self):
        v, p = make(embedder=FakeEmbedder()), pid()
        self.assertTrue(self.verify(v, p, photo(1))["verified"])
        self.assertEqual(self.verify(v, p, photo(2))["reason"], "duplicate")
        # next day the same-brand bottle is a new bottle
        self.assertTrue(self.verify(v, p, photo(3), NOW + timedelta(days=1))["verified"])

    def test_depot_item_counts_once_forever(self):
        v, p = make(detector=FakeDetector("laptop"), embedder=FakeEmbedder()), pid()
        r = self.verify(v, p, photo(1))
        self.assertTrue(r["verified"])
        self.assertEqual(r["co2_saved_grams"], 15000)
        r = self.verify(v, p, photo(2), NOW + timedelta(days=20))
        self.assertEqual(r["reason"], "duplicate")

    def test_daily_cap_for_streak_items(self):
        v, p = make(streak_daily_co2_cap_grams=100), pid()
        a = self.verify(v, p, photo(1))
        b = self.verify(v, p, photo(2))
        self.assertEqual((a["co2_saved_grams"], b["co2_saved_grams"]), (82, 18))
        c = self.verify(v, p, photo(3))
        self.assertTrue(c["verified"])
        self.assertEqual(c["co2_saved_grams"], 0)
        self.assertEqual(c["player"]["total_co2_grams"], 100)

    def test_low_confidence_rejected_with_outline(self):
        r = self.verify(make(detector=FakeDetector(conf=0.3)), pid(), photo(1))
        self.assertEqual(r["reason"], "low_confidence")
        self.assertIn("contour", r)

    def test_non_recyclable(self):
        r = self.verify(make(detector=FakeDetector("person")), pid(), photo(1))
        self.assertEqual(r["reason"], "not_recyclable")

    def test_subtype_reclassifies_bottle_as_can(self):
        labels = [s["label"] for s in config.SUBTYPES["bottle"]]
        can = next(i for i, l in enumerate(labels) if "aluminum" in l)
        r = self.verify(make(embedder=FakeEmbedder(pick=can)), pid(), photo(1))
        self.assertEqual((r["stream"], r["detected_item"]), ("METALS", "Aluminum drink cans"))

    def test_subtype_rejects_reusable_mug(self):
        labels = [s["label"] for s in config.SUBTYPES["cup"]]
        mug = next(i for i, l in enumerate(labels) if "mug" in l)
        r = self.verify(make(detector=FakeDetector("cup"), embedder=FakeEmbedder(pick=mug)), pid(), photo(1))
        self.assertEqual(r["reason"], "not_waste")

    def test_unsure_subtype_keeps_yolo_mapping(self):
        r = self.verify(make(embedder=FakeEmbedder(pick=2, prob=0.2)), pid(), photo(1))
        self.assertEqual(r["stream"], "PLASTICS")

    def test_exif_checked_unless_the_server_skips_it(self):
        self.assertEqual(make().verify(jpeg(photo(1)), pid(), NOW)["reason"], "exif")

    def test_clients_cannot_skip_exif(self):
        """The bypass is a server setting (UNCYCLED_DEV_SKIP_EXIF), never a request option."""
        import os
        os.environ.setdefault("ECO_DB", ":memory:")
        os.environ.setdefault("ECO_DISABLE_CLIP", "1")
        import app
        op = app.app.openapi()["paths"]["/verify-daily-photo"]["post"]
        self.assertNotIn("skip_exif", [p["name"] for p in op.get("parameters", [])])

    def test_exif_orientation_is_applied(self):
        """A photo stored sideways with Orientation=6 is detected upright (image_size swaps)."""
        img = photo(1)
        exif = Image.Exif()
        exif[274] = 6
        buf = io.BytesIO()
        img.save(buf, "JPEG", exif=exif.tobytes())
        r = make().verify(buf.getvalue(), pid(), NOW, skip_exif=True)
        self.assertEqual(r["image_size"], [img.height, img.width])


def fallback_index(text: str) -> int:
    return next(i for i, f in enumerate(config.FALLBACK) if text in f["label"])


class FallbackTests(unittest.TestCase):
    """Items COCO has no class for (regression: circuit board seen as 'clock', paper seen as nothing)."""

    def tearDown(self):
        while _OPEN:
            _OPEN.pop().close()

    def run_case(self, yolo_cls, label, prob=0.8):
        v = make(detector=FakeDetector(yolo_cls), embedder=FakeEmbedder(pick=fallback_index(label), prob=prob))
        return v.verify(jpeg(photo(1)), pid(), NOW, skip_exif=True)

    def test_cases(self):
        cases = [
            # yolo sees, clip label,                  prob, verified, reason,           stream
            (None,    "a sheet of paper",             0.8,  True,  "ok",             "PAPER & CARDBOARD"),
            ("clock", "a printed circuit board",      0.8,  True,  "ok",             "ELECTRONICS (E-WASTE)"),
            (None,    "a cardboard box",              0.8,  True,  "ok",             "PAPER & CARDBOARD"),
            ("person", "a person",                    0.9,  False, "not_recyclable", None),
            (None,    "a sheet of paper",             0.3,  False, "not_recyclable", None),  # CLIP unsure
            ("bottle", "a sheet of paper",            0.8,  True,  "ok",             "PLASTICS"),  # YOLO sure: YOLO wins
        ]
        for yolo, label, prob, verified, reason, stream in cases:
            with self.subTest(yolo=yolo, label=label, prob=prob):
                r = self.run_case(yolo, label, prob)
                self.assertEqual((r["verified"], r["reason"]), (verified, reason), r["message"])
                if stream:
                    self.assertEqual(r["stream"], stream)

    def test_yolo_message_kept_for_generic_negative(self):
        r = self.run_case("person", "a person")
        self.assertIn("(person)", r["message"])

    def test_screen_photo_gets_its_own_message(self):
        r = self.run_case(None, "screen or TV showing a picture")
        self.assertFalse(r["verified"])
        self.assertIn("screen", r["message"])

    def test_fallback_items_dedupe_by_item(self):
        v, p = make(detector=FakeDetector(None), embedder=FakeEmbedder(pick=fallback_index("a sheet of paper"))), pid()
        self.assertTrue(v.verify(jpeg(photo(1)), p, NOW, skip_exif=True)["verified"])
        self.assertEqual(v.verify(jpeg(photo(1)), p, NOW, skip_exif=True)["reason"], "duplicate")

    def test_without_clip_old_behaviour(self):
        r = make(detector=FakeDetector(None)).verify(jpeg(photo(1)), pid(), NOW, skip_exif=True)
        self.assertEqual(r["reason"], "no_object")


class TacoMergeTests(unittest.TestCase):
    """The litter model (TACO) and COCO run together; these are the rules for picking one."""
    OTHER_BOX = (10, 10, 90, 90)  # far from BOX

    def tearDown(self):
        while _OPEN:
            _OPEN.pop().close()

    def run_case(self, coco, taco, embedder=None, img=None, v=None, player=None):
        v = v or make(detector=FakeDetector(*coco) if coco else FakeDetector(None),
                      taco=FakeDetector(*taco) if taco else None, embedder=embedder)
        return v.verify(jpeg(img or photo(1)), player or pid(), NOW, skip_exif=True)

    def test_cases(self):
        cases = [
            # coco (cls, conf, box),            taco (cls, conf, box),              reason,      stream / detected
            (("cup", 0.9, BOX),                 ("drink_can", 0.8, BOX),            "ok",        "drink_can"),  # same object: TACO is more specific
            (("laptop", 0.95, self.OTHER_BOX),  ("drink_can", 0.5, BOX),            "ok",        "laptop"),     # different objects: higher confidence wins
            (("laptop", 0.6, self.OTHER_BOX),   ("drink_can", 0.8, BOX),            "ok",        "drink_can"),
            (None,                              ("wrapper", 0.6, BOX),              "ok",        "wrapper"),    # COCO has no wrapper class
            (None,                              ("drink_can", 0.3, BOX),            "no_object", None),         # below taco_accept_conf
            (("person", 0.9, BOX),              None,                               "not_recyclable", None),    # no litter model: unchanged
        ]
        for coco, taco, reason, detected in cases:
            with self.subTest(coco=coco, taco=taco):
                r = self.run_case(coco, taco)
                self.assertEqual(r["reason"], reason, r["message"])
                if detected:
                    self.assertEqual(r["detected_class"], detected)

    def test_wrapper_maps_to_plastic_film(self):
        r = self.run_case(None, ("wrapper", 0.7, BOX))
        self.assertEqual((r["stream"], r["detected_item"]), ("PLASTICS", "Plastic bags and shrink wrap"))

    def test_same_can_seen_by_both_models_is_one_item(self):
        labels = [s["label"] for s in config.SUBTYPES["cup"]]
        can = next(i for i, l in enumerate(labels) if "aluminum" in l)
        p, store_v = pid(), make(detector=FakeDetector(None), taco=FakeDetector("drink_can", 0.8, BOX))
        self.assertTrue(self.run_case(None, None, v=store_v, player=p)["verified"])
        # Same photo, now only COCO sees it (as a cup that CLIP calls a can): still a duplicate.
        store_v.detector, store_v.taco = FakeDetector("cup", 0.9, BOX), None
        store_v.embedder = FakeEmbedder(pick=can)
        r = self.run_case(None, None, v=store_v, player=p)
        self.assertEqual(r["reason"], "duplicate")


class ExifTests(unittest.TestCase):
    def shot(self, local: datetime) -> Image.Image:
        exif = Image.Exif()
        exif.get_ifd(0x8769)[36867] = local.strftime("%Y:%m:%d %H:%M:%S")
        buf = io.BytesIO()
        Image.new("RGB", (20, 20)).save(buf, "JPEG", exif=exif.tobytes())
        return Image.open(io.BytesIO(buf.getvalue()))

    def test_phone_timezone_header(self):
        # Phone in UTC+1 took the photo 30 s ago: its local clock reads 11:00 - 30 s.
        local = datetime(2026, 11, 21, 10, 59, 30)
        for offset, want in [(-60, True), (0, False), (60, False)]:
            with self.subTest(offset=offset):
                ok, _ = check_live(self.shot(local), NOW, 120, offset)
                self.assertEqual(ok, want)

    def test_old_photo_rejected(self):
        ok, why = check_live(self.shot(datetime(2026, 11, 21, 10, 50)), NOW, 120, -60)
        self.assertFalse(ok)
        self.assertIn("old", why)

    def test_no_exif_rejected(self):
        buf = io.BytesIO()
        Image.new("RGB", (20, 20)).save(buf, "JPEG")
        ok, _ = check_live(Image.open(io.BytesIO(buf.getvalue())), NOW, 120, None)
        self.assertFalse(ok)


if __name__ == "__main__":
    unittest.main()
