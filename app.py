"""Uncycled eco photo verification API.

Run: uvicorn app:app --host 0.0.0.0 --port 8000
The pipeline lives in ecoverify/; tuning, skins and taxonomy in content/eco_verify.json.
"""
import os
import uuid
from datetime import datetime, timezone
from pathlib import Path

from fastapi import FastAPI, File, Header, HTTPException, UploadFile
from fastapi.concurrency import run_in_threadpool
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import HTMLResponse

from ecoverify import config
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
TESTER_HTML =(Path(__file__).parent / "ecoverify" / "tester.html").read_text(encoding="utf-8")


def _player_id(value: str | None) -> str:
    """Players are identified by a random UUID the app creates on first launch."""
    try:
        return str(uuid.UUID(value or ""))
    except ValueError:
        raise HTTPException(status_code=400, detail="Missing or invalid X-Player-Id header (expected a UUID).")


def _now() -> datetime:
    return datetime.now(timezone.utc)


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
    if file.content_type and not file.content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="Invalid file type.")
    data = await file.read(T["max_upload_bytes"] + 1)
    if len(data) > T["max_upload_bytes"]:
        raise HTTPException(status_code=413, detail="Image too large (max 10 MB).")
    return await run_in_threadpool(verifier.verify, data, pid, _now(), x_tz_offset_minutes, SKIP_EXIF)


@app.get("/players/{player_id}")
async def player_profile(player_id: str):
    """Streak, totals and unlocked skins. The phone and (later) the game server read this."""
    return verifier.profile(_player_id(player_id), _now())


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
    s = store.global_stats()
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
