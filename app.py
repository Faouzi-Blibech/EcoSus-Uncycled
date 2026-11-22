"""Uncycled eco photo verification API.

Run: uvicorn app:app --host 0.0.0.0 --port 8000
The pipeline lives in ecoverify/; tuning and taxonomy in content/eco_verify.json, skins in content/skins.json.
"""
import io
import json
import os
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path

from fastapi import FastAPI, File, Form, Header, HTTPException, Query, Request, UploadFile
from fastapi.concurrency import run_in_threadpool
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import HTMLResponse, Response
from pydantic import BaseModel

from ecoverify import config, rules
from ecoverify.clip import ClipEmbedder
from ecoverify.service import Verifier, co2_equivalence
from ecoverify.store import Store
from ecoverify.vision import Detector

app = FastAPI(title="Uncycled Photo Verification API", version="3.0.0")
app.add_middleware(CORSMiddleware, allow_origins=["*"], allow_methods=["*"], allow_headers=["*"])

T = config.TUNING
store = Store(os.environ.get("ECO_DB", "eco_verify.db"))
detector = Detector(T["model"], T.get("fallback_model"))
embedder = ClipEmbedder(T["clip_model"]) if os.environ.get("ECO_DISABLE_CLIP") != "1" else None
# Litter model fine-tuned on TACO (ecoverify/train/). Optional: off until the weights exist.
TACO_PATH = Path(__file__).parent / T["taco_model"]
taco = Detector(str(TACO_PATH)) if TACO_PATH.exists() and os.environ.get("ECO_DISABLE_TACO") != "1" else None
verifier = Verifier(store, detector, embedder, taco=taco)
# Server-side switch for local tests only (browser webcam shots carry no EXIF);
# clients can never turn the freshness check off.
SKIP_EXIF = os.environ.get("UNCYCLED_DEV_SKIP_EXIF") == "1"
TESTER_HTML = (Path(__file__).parent / "ecoverify" / "tester.html").read_text(encoding="utf-8")
SCAN_HTML = (Path(__file__).parent / "ecoverify" / "scan.html").read_text(encoding="utf-8")
LINK_GONE = "This scan link has expired or was already used. Start a new scan from the game."


def _player_id(value: str | None) -> str:
    """Players are identified by a random UUID the app creates on first launch."""
    try:
        return str(uuid.UUID(value or ""))
    except ValueError:
        raise HTTPException(status_code=400, detail="Missing or invalid X-Player-Id header (expected a UUID).")


def _now() -> datetime:
    return datetime.now(timezone.utc)


def _base_url(request: Request) -> str:
    """Public address of this service, for the links and QR codes the game shows.

    ECO_PUBLIC_URL wins (set it to the tunnel/host URL); otherwise use the forwarded host and
    scheme a tunnel or proxy sends (cloudflared does), otherwise the address the request came in on.
    """
    if os.environ.get("ECO_PUBLIC_URL"):
        return os.environ["ECO_PUBLIC_URL"].rstrip("/")
    host = request.headers.get("x-forwarded-host") or request.headers.get("host") or request.url.netloc
    proto = request.headers.get("x-forwarded-proto") or request.url.scheme
    return f"{proto}://{host}"


async def _read_image(file: UploadFile) -> bytes:
    if file.content_type and not file.content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="Invalid file type.")
    data = await file.read(T["max_upload_bytes"] + 1)
    if len(data) > T["max_upload_bytes"]:
        raise HTTPException(status_code=413, detail="Image too large (max 10 MB).")
    return data


@app.on_event("startup")
async def warm_up():
    """Load the models before the first photo so the demo's first scan is fast."""
    from PIL import Image
    await run_in_threadpool(detector.detect, Image.new("RGB", (64, 64)), 0.9)
    if taco is not None:
        await run_in_threadpool(taco.detect, Image.new("RGB", (64, 64)), 0.9)
    if embedder is not None:
        await run_in_threadpool(embedder.analyse, Image.new("RGB", (64, 64)), None)


@app.get("/health")
async def health():
    return {
        "ok": True,
        "model": detector.loaded_name,
        "taco_model": Path(taco.loaded_name).name if taco is not None and taco.loaded_name else None,
        "taco_classes": len(config.TACO) if taco is not None else 0,
        "dev_skip_exif": SKIP_EXIF,
        "clip": embedder is not None and embedder.available,
        "clip_error": embedder.error if embedder is not None else "disabled",
    }


@app.post("/verify-daily-photo")
async def verify_photo(
    file: UploadFile = File(...),
    x_player_id: str | None = Header(default=None),
    x_tz_offset_minutes: int | None = Header(default=None),
):
    pid = _player_id(x_player_id)
    data = await _read_image(file)
    return await run_in_threadpool(verifier.verify, data, pid, _now(), x_tz_offset_minutes, SKIP_EXIF)


@app.get("/players/{player_id}")
@app.get("/profile/{player_id}")
async def player_profile(player_id: str):
    """Points, streak, totals and unlocked skins. The game, the phone app and the scan page read this."""
    return verifier.profile(_player_id(player_id), _now())


# ── Scan links: the game asks for a link, the player opens it on a phone and photographs an item ──
class ScanStart(BaseModel):
    player_id: str


@app.post("/scan/start")
async def scan_start(body: ScanStart, request: Request):
    """A single-use link (and its QR code) valid for scan_token_ttl_s seconds."""
    pid = _player_id(body.player_id)
    ttl = T["scan_token_ttl_s"]
    token = verifier.store.issue_token(pid, time.time(), ttl)
    base = _base_url(request)
    return {"token": token, "url": f"{base}/scan?t={token}", "qr_url": f"{base}/scan/qr?t={token}",
            "expires_s": ttl}


@app.get("/scan", response_class=HTMLResponse)
async def scan_page(t: str = Query(default="")):
    """Phone page: take a photo of the item, see the result, go back to the game."""
    state = {"token": t, "valid": verifier.store.token_player(t, time.time()) is not None,
             "gone_message": LINK_GONE}
    # json.dumps output is safe inside <script> once "</" is escaped
    return SCAN_HTML.replace("/*__STATE__*/null", json.dumps(state).replace("</", "<\\/"))


@app.post("/scan/upload")
async def scan_upload(token: str = Form(...), file: UploadFile = File(...)):
    """The scan link replaces the EXIF time check: only a fresh, unused link is accepted.

    A photo that is not accepted gives the link back, so the player can retry until it expires.
    """
    data = await _read_image(file)
    pid = verifier.store.redeem_token(token, time.time())
    if pid is None:
        raise HTTPException(status_code=410, detail=LINK_GONE)
    points_before = verifier.profile(pid, _now())["points"]
    result = await run_in_threadpool(verifier.verify, data, pid, _now(), None, True)
    if not result["verified"]:
        verifier.store.release_token(token)
    # On success the verify result already has the profile with this scan's new skins.
    result["profile"] = result.get("player") or verifier.profile(pid, _now())
    result["points_gained"] = result["profile"]["points"] - points_before
    return result


# ── Shop: skins with a price in content/skins.json, paid with scan points ──
class ShopBuy(BaseModel):
    player_id: str
    item: str


@app.get("/shop")
async def shop():
    return {"items": [{"id": s["id"], "price": s["price"], "name": s.get("name", s["id"]),
                       "emoji": s.get("emoji", ""), "kind": "skin"} for s in config.SHOP.values()]}


@app.post("/shop/buy")
async def shop_buy(body: ShopBuy):
    """409 when already owned, 402 when the player has too few points, 404 for an unknown item."""
    pid = _player_id(body.player_id)
    with verifier.store.lock:
        profile = verifier.profile(pid, _now())
        item = config.SHOP.get(body.item)
        why = rules.can_buy(item, set(profile["skins"]), profile["currency"])
        if why is not None:
            code = {"unknown": 404, "owned": 409, "not_enough": 402}[why]
            raise HTTPException(status_code=code, detail=why)
        verifier.store.buy(pid, item["id"], int(item["price"]), _now().isoformat())
    return {"ok": True, "item": item["id"], "profile": verifier.profile(pid, _now())}


@app.get("/scan/qr")
async def scan_qr(request: Request, t: str = Query(default="")):
    """PNG QR code of the scan link (the Windows build shows it for a phone to scan)."""
    if verifier.store.token_player(t, time.time()) is None:
        raise HTTPException(status_code=410, detail=LINK_GONE)
    import qrcode
    img = qrcode.make(f"{_base_url(request)}/scan?t={t}", box_size=8, border=2)
    buf = io.BytesIO()
    img.save(buf, format="PNG")
    return Response(content=buf.getvalue(), media_type="image/png", headers={"Cache-Control": "no-store"})


@app.get("/recycling-guide")
async def recycling_guide():
    return {
        "total_streams": len(config.TAXONOMY),
        "total_items_supported": sum(len(s["items"]) for s in config.TAXONOMY.values()),
        "streams": config.TAXONOMY,
        "skins": config.SKINS,
    }


@app.get("/eco-stats")
async def eco_stats():
    s = verifier.store.global_stats()
    return {
        "total_items_recycled": s["items"],
        "total_players": s["players"],
        "total_co2_saved_grams": s["co2"],
        "total_co2_saved_kg": round(s["co2"] / 1000, 2),
        "co2_equivalence": co2_equivalence(s["co2"]) if s["co2"] else None,
        "streams": s["streams"],
        "items_by_type": s["items_by_type"],
    }


@app.get("/", response_class=HTMLResponse)
async def web_tester():
    return TESTER_HTML
