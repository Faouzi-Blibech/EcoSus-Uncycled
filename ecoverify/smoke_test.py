"""End-to-end check against a running verifier (local uvicorn or Docker).

    python -m ecoverify.smoke_test                       # http://localhost:8000
    python -m ecoverify.smoke_test http://192.168.1.20:8000
    docker compose exec verifier python -m ecoverify.smoke_test

Uses the sample photos that ship with Ultralytics, and a fresh random player
each run. Every photo gets a fresh EXIF capture time, so it works with the
freshness check on (clients cannot skip it). Exits 1 if any check fails.
"""
import io
import json
import sys
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image


def sample(name: str) -> bytes:
    import ultralytics
    return (Path(ultralytics.__file__).parent / "assets" / name).read_bytes()


def paper_photo(seed: int = 0) -> bytes:
    """A printed sheet on a wooden table: an item COCO has no class for."""
    import numpy as np
    from PIL import ImageDraw
    rng = np.random.default_rng(seed)
    bg = np.full((720, 960, 3), (139, 94, 60), np.int16) + rng.integers(-18, 18, (720, 960, 3))
    table = Image.fromarray(bg.clip(0, 255).astype("uint8"))
    sheet = Image.new("RGB", (420, 590), (245, 245, 240))
    d = ImageDraw.Draw(sheet)
    d.text((40, 40), "INVOICE  No. 2026-1121", fill=(20, 20, 20))
    for i in range(28):
        d.line((40, 90 + i * 17, 380 - ((i + seed * 5) * 37 % 120), 90 + i * 17), fill=(60, 60, 60), width=3)
    sheet = sheet.rotate(8, expand=True)
    table.paste(sheet, (260, 40), sheet.convert("L").point(lambda v: 255 if v > 10 else 0))
    buf = io.BytesIO()
    table.save(buf, "JPEG", quality=92)
    return buf.getvalue()


def fresh(data: bytes) -> bytes:
    """Re-save a photo with EXIF DateTimeOriginal = now (UTC; sent with X-Tz-Offset-Minutes: 0)."""
    img = Image.open(io.BytesIO(data)).convert("RGB")
    exif = Image.Exif()
    exif.get_ifd(0x8769)[36867] = datetime.now(timezone.utc).strftime("%Y:%m:%d %H:%M:%S")
    buf = io.BytesIO()
    img.save(buf, "JPEG", quality=92, exif=exif.tobytes())
    return buf.getvalue()


def post(base: str, data: bytes, player: str | None, with_exif: bool = True) -> tuple[int, dict]:
    if with_exif:
        data = fresh(data)
    boundary = uuid.uuid4().hex
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"p.jpg\"\r\n"
            f"Content-Type: image/jpeg\r\n\r\n").encode() + data + f"\r\n--{boundary}--\r\n".encode()
    headers = {"Content-Type": f"multipart/form-data; boundary={boundary}", "X-Tz-Offset-Minutes": "0"}
    if player:
        headers["X-Player-Id"] = player
    url = f"{base}/verify-daily-photo"
    return _send(urllib.request.Request(url, data=body, headers=headers, method="POST"))


def upload_scan(base: str, token: str, data: bytes) -> tuple[int, dict]:
    """POST /scan/upload as the scan page does (form fields token + file, no EXIF needed)."""
    boundary = uuid.uuid4().hex
    body = (f"--{boundary}\r\nContent-Disposition: form-data; name=\"token\"\r\n\r\n{token}\r\n"
            f"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; filename=\"scan.jpg\"\r\n"
            f"Content-Type: image/jpeg\r\n\r\n").encode() + data + f"\r\n--{boundary}--\r\n".encode()
    req = urllib.request.Request(f"{base}/scan/upload", data=body, method="POST",
                                 headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    return _send(req)


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

    if h.get("dev_skip_exif"):
        print("INFO  EXIF check is off on this server (UNCYCLED_DEV_SKIP_EXIF=1): skipping the no-EXIF check")
    else:
        status, r = post(base, plain.getvalue(), player, with_exif=False)
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

    # ── scan links (game -> phone page), with their own player so earlier photos are not duplicates ──
    scan_player = str(uuid.uuid4())
    req = urllib.request.Request(f"{base}/scan/start", data=json.dumps({"player_id": scan_player}).encode(),
                                 headers={"Content-Type": "application/json"}, method="POST")
    status, s = _send(req)
    check("scan link issued", status == 200 and "/scan?t=" in s.get("url", ""), s.get("url"))
    token = s.get("token", "")
    with urllib.request.urlopen(f"{base}/scan?t={token}", timeout=30) as r:
        check("scan page served", r.status == 200 and b'"valid": true' in r.read())
    with urllib.request.urlopen(f"{base}/scan/qr?t={token}", timeout=30) as r:
        check("QR code is a PNG", r.read(4) == b"\x89PNG")
    status, r = upload_scan(base, token, paper_photo(seed=1))
    check("scan upload accepted (no EXIF needed)", status == 200 and r.get("verified"), r.get("message"))
    check("scan earns 10 points", (r.get("profile") or {}).get("points") == 10, (r.get("profile") or {}).get("points"))
    status, r = upload_scan(base, token, paper_photo(seed=2))
    check("used scan link -> 410", status == 410, status)

    status, p = get(base, f"/players/{player}")
    check("profile kept on server", p.get("streak") == 1 and p.get("total_items") == 2,
          f"streak={p.get('streak')} items={p.get('total_items')} co2={p.get('total_co2_grams')}")

    print(f"\n{'All checks passed' if not failures else f'{failures} check(s) failed'} (player {player})")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
