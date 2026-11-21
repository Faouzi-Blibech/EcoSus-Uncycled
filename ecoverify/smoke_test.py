"""End-to-end check against a running verifier (local uvicorn or Docker).

    python -m ecoverify.smoke_test                       # http://localhost:8000
    python -m ecoverify.smoke_test http://192.168.1.20:8000
    docker compose exec verifier python -m ecoverify.smoke_test

Uses the sample photos that ship with Ultralytics, and a fresh random player
each run. Exits 1 if any check fails.
"""
import io
import json
import sys
import urllib.error
import urllib.request
import uuid
from pathlib import Path

from PIL import Image


def sample(name: str) -> bytes:
    import ultralytics
    return (Path(ultralytics.__file__).parent / "assets" / name).read_bytes()


def paper_photo() -> bytes:
    """A printed sheet on a wooden table: an item COCO has no class for."""
    import numpy as np
    from PIL import ImageDraw
    rng = np.random.default_rng(0)
    bg = np.full((720, 960, 3), (139, 94, 60), np.int16) + rng.integers(-18, 18, (720, 960, 3))
    table = Image.fromarray(bg.clip(0, 255).astype("uint8"))
    sheet = Image.new("RGB", (420, 590), (245, 245, 240))
    d = ImageDraw.Draw(sheet)
    d.text((40, 40), "INVOICE  No. 2026-1121", fill=(20, 20, 20))
    for i in range(28):
        d.line((40, 90 + i * 17, 380 - (i * 37 % 120), 90 + i * 17), fill=(60, 60, 60), width=3)
    sheet = sheet.rotate(8, expand=True)
    table.paste(sheet, (260, 40), sheet.convert("L").point(lambda v: 255 if v > 10 else 0))
    buf = io.BytesIO()
    table.save(buf, "JPEG", quality=92)
    return buf.getvalue()


def post(base: str, data: bytes, player: str | None, skip_exif: bool = True) -> tuple[int, dict]:
    boundary = uuid.uuid4().hex
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"p.jpg\"\r\n"
            f"Content-Type: image/jpeg\r\n\r\n").encode() + data + f"\r\n--{boundary}--\r\n".encode()
    headers = {"Content-Type": f"multipart/form-data; boundary={boundary}"}
    if player:
        headers["X-Player-Id"] = player
    url = f"{base}/verify-daily-photo" + ("?skip_exif=true" if skip_exif else "")
    return _send(urllib.request.Request(url, data=body, headers=headers, method="POST"))


def get(base: str, path: str) -> tuple[int, dict]:
    return _send(urllib.request.Request(base + path))


def _send(req) -> tuple[int, dict]:
    try:
        with urllib.request.urlopen(req, timeout=120) as r:
            return r.status, json.loads(r.read())
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read() or b"{}")


def main() -> int:
    base = (sys.argv[1] if len(sys.argv) > 1 else "http://localhost:8000").rstrip("/")
    player = str(uuid.uuid4())
    plain = io.BytesIO()
    Image.new("RGB", (64, 64)).save(plain, "JPEG")
    failures = 0

    def check(name: str, ok: bool, detail: object = "") -> None:
        nonlocal failures
        failures += not ok
        print(f"{'PASS' if ok else 'FAIL'}  {name}  {detail}")

    status, h = get(base, "/health")
    check("health", status == 200 and h.get("ok"), h)
    check("segmentation model loaded", h.get("model") == "yolov8s-seg.pt", h.get("model"))
    check("CLIP available", h.get("clip") is True, h.get("clip_error") or "")
    if h.get("taco_model"):
        # regression: the Docker image once shipped the model without content/taco_classes.json
        check("TACO model has trusted classes", h.get("taco_classes", 0) > 0, f"{h.get('taco_classes')} classes")
    else:
        print("INFO  TACO litter model not loaded (ecoverify/models/taco-seg.pt missing)")

    status, r = post(base, sample("zidane.jpg"), None)
    check("missing player id -> 400", status == 400, status)

    status, r = post(base, plain.getvalue(), player, skip_exif=False)
    check("no EXIF -> rejected", r.get("reason") == "exif", r.get("reason"))

    status, r = post(base, sample("bus.jpg"), player)
    check("people/bus -> not_recyclable", r.get("reason") == "not_recyclable", r.get("detected_class"))
    check("rejection still returns an outline", len(r.get("contour") or []) >= 3, f"{len(r.get('contour') or [])} pts")

    status, r = post(base, sample("zidane.jpg"), player)
    check("tie -> accepted (depot)", r.get("verified") and r.get("kind") == "depot",
          f"{r.get('detected_class')} -> {r.get('detected_item')} {r.get('co2_saved_grams')} g")
    check("streak = 1, first skin unlocked",
          (r.get("player") or {}).get("streak") == 1 and [s["id"] for s in r["player"]["new_unlocks"]] == ["eco_novice"])

    status, r = post(base, sample("zidane.jpg"), player)
    check("same photo again -> duplicate", r.get("reason") == "duplicate", r.get("message"))

    flipped = io.BytesIO()
    Image.open(io.BytesIO(sample("zidane.jpg"))).transpose(Image.FLIP_LEFT_RIGHT).save(flipped, "JPEG")
    status, r = post(base, flipped.getvalue(), player)
    check("mirrored photo -> duplicate", r.get("reason") == "duplicate", r.get("reason"))

    status, r = post(base, paper_photo(), player)
    check("sheet of paper (no COCO class) -> accepted", r.get("verified") and r.get("stream") == "PAPER & CARDBOARD",
          f"{r.get('detected_class')} {r.get('confidence_score')} -> {r.get('detected_item')}")
    check("paper gets an outline", len(r.get("contour") or []) >= 3, f"{len(r.get('contour') or [])} pts")

    status, p = get(base, f"/players/{player}")
    check("profile kept on server", p.get("streak") == 1 and p.get("total_items") == 2,
          f"streak={p.get('streak')} items={p.get('total_items')} co2={p.get('total_co2_grams')}")

    print(f"\n{'All checks passed' if not failures else f'{failures} check(s) failed'} (player {player})")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
