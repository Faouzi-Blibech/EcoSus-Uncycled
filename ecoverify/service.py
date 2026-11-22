"""The verification pipeline. Synchronous on purpose: app.py runs it in a worker thread.

Every response has `verified` (bool), `reason` (code below) and `message`.
reason: ok | exif | no_object | not_recyclable | low_confidence | not_waste | duplicate
"""
import io
from datetime import datetime, timedelta

from PIL import Image

from . import clip, config, rules, vision
from .exif import check_live
from .store import Store


class Verifier:
    def __init__(self, store: Store, detector, embedder=None, tuning: dict | None = None, taco=None):
        self.store = store
        self.detector = detector      # COCO model: everyday objects, rejects people/furniture
        self.taco = taco              # optional litter model trained on TACO
        self.embedder = embedder
        self.t = tuning or config.TUNING

    def _detect(self, detector, image: Image.Image) -> list:
        if detector is None:
            return []
        t = self.t
        return [d for d in detector.detect(image, t["detect_conf"])
                if d.bbox[2] - d.bbox[0] >= t["min_box_px"] and d.bbox[3] - d.bbox[1] >= t["min_box_px"]]

    # ── profile ───────────────────────────────────────────────
    def profile(self, pid: str, now_utc: datetime, new_unlock_ids: list[str] | None = None) -> dict:
        today = rules.day_key(now_utc, self.t["day_utc_offset_hours"])
        row = self.store.player(pid)
        unlocked = self.store.unlocked_ids(pid)
        streak_co2_today, _ = self.store.day_totals(pid, today)
        last_day = datetime.fromisoformat(row["last_day"]).date() if row and row["last_day"] else None
        total = int(row["total_co2"]) if row else 0
        new_ids = set(new_unlock_ids or [])
        per_day = self.store.scans_per_day(pid)
        bought = self.store.purchases(pid)
        unlocked = unlocked | set(bought)
        points = rules.points(list(per_day.values()), self.t["points_per_scan"], self.t["max_scored_scans_per_day"])
        return {
            "player_id": pid,
            # game fields: points earned by scans (points_per_scan each, max_scored_scans_per_day a day count);
            # currency = points not spent in the shop
            "points": points,
            "currency": points - sum(bought.values()),
            "bought": sorted(bought),
            "scans_today": per_day.get(today.isoformat(), 0),
            "skins": [s["id"] for s in config.SKINS if s["id"] in unlocked],
            "new_skins": [s["id"] for s in config.SKINS if s["id"] in new_ids],
            "streak": rules.live_streak(last_day, today, int(row["streak"]) if row else 0),
            "best_streak": int(row["best_streak"]) if row else 0,
            "verified_today": last_day == today,
            "total_co2_grams": total,
            "total_co2_kg": round(total / 1000, 2),
            "total_items": int(row["total_items"]) if row else 0,
            "streak_co2_today": streak_co2_today,
            "streak_co2_cap": self.t["streak_daily_co2_cap_grams"],
            "unlocked_skins": [s for s in config.SKINS if s["id"] in unlocked],
            "new_unlocks": [s for s in config.SKINS if s["id"] in new_ids],
            "next_skin": rules.next_skin(unlocked, config.SKINS),
        }

    # ── verify ────────────────────────────────────────────────
    def verify(self, data: bytes, pid: str, now_utc: datetime,
               tz_offset_minutes: int | None = None, skip_exif: bool = False) -> dict:
        t = self.t
        try:
            raw = Image.open(io.BytesIO(data))
            raw.load()
        except Exception:
            return _fail("no_object", "Could not read that image.")

        if not skip_exif:  # set only by the server (UNCYCLED_DEV_SKIP_EXIF), never by a request
            ok, why = check_live(raw, now_utc, t["max_photo_age_seconds"], tz_offset_minutes)
            if not ok:
                return _fail("exif", f"Live-capture check failed: {why}")

        image = vision.upright(raw)
        size = image.size
        dets = self._detect(self.detector, image)
        cand = next((d for d in dets if d.coco_class in config.DETECTIONS), None)
        litter = next((d for d in self._detect(self.taco, image)
                       if d.coco_class in config.TACO and d.conf >= t["taco_accept_conf"]), None)
        coco_ok = cand is not None and cand.conf >= t["accept_conf"]
        # Same object seen by both models: the litter model is more specific (a can, not a "cup").
        if litter is not None and coco_ok and vision.iou(litter.bbox, cand.bbox) < 0.3 and cand.conf > litter.conf:
            litter = None

        if litter is not None:
            # ── litter model (TACO) knows the item ──
            cand, entry, subtype_prob = litter, config.TACO[litter.coco_class], None
            crop = vision.crop_item(image, cand.bbox, t["crop_pad_ratio"])
            res = self.embedder.analyse(crop, None) if self.embedder is not None else None
            embedding = res[0] if res is not None else None
        elif coco_ok:
            # ── YOLO knows the item; CLIP refines the material on the crop ──
            crop = vision.crop_item(image, cand.bbox, t["crop_pad_ratio"])
            entry = config.DETECTIONS[cand.coco_class]
            subtypes = config.SUBTYPES.get(cand.coco_class, [])
            embedding, subtype_prob = None, None
            if self.embedder is not None:
                res = self.embedder.analyse(crop, [s["label"] for s in subtypes] or None)
                if res is not None:
                    embedding, choice = res
                    if choice and choice[1] >= t["clip_min_subtype_prob"]:
                        sub, subtype_prob = subtypes[choice[0]], round(choice[1], 3)
                        if "reject_message" in sub:
                            return _fail("not_waste", sub["reject_message"], **_seen(cand, size, t))
                        entry = sub
        else:
            # ── YOLO has no class for it (paper, cardboard, cans, circuit boards...) ──
            fb = self._fallback(image)
            if fb is None or fb["entry"] is None:
                return self._rejection(dets, cand, fb, size)
            cand, crop, entry, embedding = fb["det"], fb["crop"], fb["entry"], fb["embedding"]
            subtype_prob = round(cand.conf, 3)

        stream, item, kind = entry["stream"], entry["item"], entry["kind"]
        # Duplicates are looked for within the material stream, so the same can seen once by
        # the litter model and once by COCO (as a "cup") is still one item.
        group = stream
        item_co2 = config.item_co2(stream, item)
        seen = _seen(cand, size, t)
        seen.update(stream=stream, detected_item=item, kind=kind, subtype_confidence=subtype_prob)

        variants = vision.hash_variants(crop)
        p_hash, d_hash = vision.hashes(crop)
        today = rules.day_key(now_utc, t["day_utc_offset_hours"])
        hash_since = (today - timedelta(days=t["hash_window_days"])).isoformat()

        with self.store.lock:
            for row in self.store.candidates(pid, group):
                forever = row["kind"] == "depot"
                if (forever or row["day"] >= hash_since) and \
                        vision.same_photo(variants, row["phash"], row["dhash"], t["hash_max_distance"]):
                    return _fail("duplicate", "Anti-cheat triggered: you already scanned this exact item!", **seen)
                if embedding is not None and row["embedding"] and (forever or row["day"] == today.isoformat()) \
                        and clip.cosine(embedding, row["embedding"]) >= t["embed_same_item_cosine"]:
                    return _fail("duplicate",
                                 "Anti-cheat triggered: this looks like the same item from another angle!", **seen)

            streak_co2, depot_n = self.store.day_totals(pid, today)
            awarded, note = rules.award_co2(kind, item_co2, streak_co2, depot_n,
                                            t["streak_daily_co2_cap_grams"], t["depot_max_per_day"])
            row = self.store.player(pid)
            last_day = datetime.fromisoformat(row["last_day"]).date() if row and row["last_day"] else None
            streak = rules.next_streak(last_day, today, int(row["streak"]) if row else 0)
            unlocks = [s["id"] for s in rules.new_unlocks(streak, self.store.unlocked_ids(pid), config.SKINS)]
            self.store.record(pid, now_utc, today, {
                "coco_class": group, "stream": stream, "item": item, "kind": kind,
                "co2_awarded": awarded, "phash": p_hash, "dhash": d_hash,
                "embedding": clip.to_blob(embedding) if embedding is not None else None,
            }, streak, unlocks)

        stream_meta = config.TAXONOMY[stream]
        return {
            "verified": True,
            "reason": "ok",
            **seen,
            "item_detail": entry.get("detail", ""),
            "disposal_tip": config.item_disposal(stream, item),
            "bin": stream_meta["bin"],
            "color": stream_meta["color"],
            "co2_item_grams": item_co2,
            "co2_saved_grams": awarded,
            "co2_note": note,
            "co2_equivalence": co2_equivalence(awarded) if awarded else None,
            "player": self.profile(pid, now_utc, unlocks),
            "message": (f"Eco-streak validated! You saved ~{awarded}g of CO₂ 🌍" if awarded
                        else "Eco-streak validated! 🌍"),
        }


    # ── fallback for items COCO has no class for ──────────────
    def _fallback(self, image: Image.Image) -> dict | None:
        """GrabCut finds the main object, CLIP names it from the closed list in content (with
        'not waste' labels). Returns None when CLIP is unavailable; entry is None when rejected."""
        if self.embedder is None or not config.FALLBACK:
            return None
        t = self.t
        det = vision.main_object(image, t["grabcut_max_side"])
        if det is None:  # no clear object: judge the whole photo, no outline
            det = vision.Detection("", 0.0, (0, 0, image.width, image.height), None)
        crop = vision.crop_item(image, det.bbox, t["crop_pad_ratio"])
        res = self.embedder.analyse(crop, [f["label"] for f in config.FALLBACK])
        if res is None:
            return None
        embedding, (idx, prob) = res
        label = config.FALLBACK[idx]
        det.coco_class = label["label"].removeprefix("a photo of ")
        det.conf = prob
        out = {"det": det, "crop": crop, "embedding": embedding, "entry": None, "reject": None}
        if "reject_message" in label:
            out["reject"] = label["reject_message"] or None
        elif prob >= t["fallback_min_prob"]:
            out["entry"] = label
        return out

    def _rejection(self, dets: list, cand, fb: dict | None, size) -> dict:
        """Explain a photo with no accepted item, preferring a specific CLIP reason (screen, blur)."""
        t, special = self.t, fb["reject"] if fb else None
        if cand is not None:
            return _fail("low_confidence", special or
                         f"Not sure this is a {cand.coco_class} ({round(cand.conf * 100)}%). "
                         "Get closer, fill the frame with the item, and use good light.", **_seen(cand, size, t))
        if dets:
            return _fail("not_recyclable", special or
                         f"Non-recyclable object detected ({dets[0].coco_class}). Scan a recyclable item "
                         "(bottle, can, carton, paper, e-waste, food scraps)!", **_seen(dets[0], size, t))
        if fb is not None:
            seen = _seen(fb["det"], size, t)
            return _fail("not_recyclable", special or
                         "No recyclable item recognised. Fill the frame with the item, in good light.", **seen)
        return _fail("no_object", "No object detected. Hold your recyclable item clearly in front of the camera.")


def _fail(reason: str, message: str, **extra) -> dict:
    return {"verified": False, "reason": reason, "message": message, **extra}


def _seen(det: vision.Detection, size: tuple[int, int], t: dict) -> dict:
    """What the model saw, so the phone can draw the outline on the photo."""
    whole_photo = det.contour is None and tuple(det.bbox) == (0, 0, *size)
    return {
        "detected_class": det.coco_class,
        "confidence_score": round(det.conf, 3),
        "bbox": list(det.bbox),
        "image_size": list(size),
        "contour": [] if whole_photo else vision.outline(det, size, t["contour_max_points"]),
    }


def co2_equivalence(grams: int) -> str:
    """Grams of CO2 as a relatable comparison."""
    if grams < 100:
        charges = max(1, round(grams / 8))
        return f"Like charging your phone {charges} time{'s' if charges != 1 else ''}!"
    if grams < 1000:
        return f"Like taking a gasoline car off the road for {round(grams / 120, 1)} km!"
    if grams < 5000:
        return f"Like keeping {max(1, round(grams / 33))} plastic bags out of the Mediterranean!"
    if grams < 15000:
        return f"Like avoiding {round(grams / 120)} km of car exhaust emissions!"
    return f"Like the carbon absorbed by {round(grams / 2200, 1)} mature trees over a whole month!"
