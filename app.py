from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.responses import HTMLResponse
from fastapi.middleware.cors import CORSMiddleware
from ultralytics import YOLOE
from PIL import Image
from PIL.ExifTags import Base as ExifBase
import io
import imagehash
import json
import os
import asyncio
from datetime import datetime, timedelta, timezone

app = FastAPI(title="Uncycled Photo Verification API")

# ─── CORS (so teammates' frontends can reach this server) ─────
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)

# ─── 1. Load YOLOE Model & Configure Custom Classes ──────────
model = YOLOE("yoloe-26s-seg.pt")

# Zero-shot vision models match much better when prompted with natural synonyms.
# All synonyms map cleanly back to your canonical recycling categories.
CLASS_SYNONYMS = {
    # Plastic & Bottles
    "plastic bottle": "plastic bottle",
    "water bottle": "plastic bottle",
    "bottle": "plastic bottle",
    "beverage bottle": "plastic bottle",
    "plastic container": "plastic bottle",

    # Aluminum, Cans & Foil Wrappers (e.g. gum foil, soda cans)
    "aluminum can": "aluminum can",
    "soda can": "aluminum can",
    "tin can": "aluminum can",
    "beverage can": "aluminum can",
    "can": "aluminum can",
    "aluminum foil": "aluminum can",
    "foil wrapper": "aluminum can",
    "metal foil": "aluminum can",

    # Cardboard & Cartons
    "cardboard box": "cardboard box",
    "cardboard": "cardboard box",
    "carton": "cardboard box",
    "packaging box": "cardboard box",

    # Glass
    "glass jar": "glass jar",
    "glass bottle": "glass jar",
    "jar": "glass jar",

    # Paper
    "paper waste": "paper waste",
    "paper": "paper waste",
    "newspaper": "paper waste",
}
model.set_classes(list(CLASS_SYNONYMS.keys()))

# ─── 2. CO₂ Savings Per Item (grams of CO₂ avoided) ──────────
CO2_SAVINGS = {
    "plastic bottle": 82,
    "aluminum can": 170,
    "cardboard box": 390,
    "glass jar": 215,
    "paper waste": 60,
}
CO2_EQUIVALENCES = [
    {"threshold": 100, "text": "like charging your phone {} times"},
    {"threshold": 500, "text": "like skipping {} km of driving"},
    {"threshold": 1000, "text": "like saving {} plastic bags from the ocean"},
    {"threshold": 5000, "text": "like planting {} small trees"},
]

def get_co2_equivalence(grams: int) -> str:
    """Convert grams of CO₂ into a relatable human metaphor."""
    if grams < 100:
        charges = max(1, round(grams / 8))
        return f"Like charging your phone {charges} time{'s' if charges != 1 else ''}!"
    elif grams < 1000:
        km = round(grams / 120, 1)
        return f"Like taking a car off the road for {km} km!"
    elif grams < 5000:
        bags = round(grams / 33)
        return f"Like keeping {bags} plastic bags out of the ocean!"
    else:
        trees = round(grams / 2200, 1)
        return f"Like what {trees} trees absorb in a month!"

# ─── 3. Setup Hackathon-friendly Database ─────────────────────
DB_FILE = "submitted_hashes.json"
STATS_FILE = "eco_stats.json"

if os.path.exists(DB_FILE):
    with open(DB_FILE, "r") as f:
        seen_hashes = json.load(f)
else:
    seen_hashes = []

if os.path.exists(STATS_FILE):
    with open(STATS_FILE, "r") as f:
        eco_stats = json.load(f)
else:
    eco_stats = {"total_items": 0, "total_co2_grams": 0, "items_by_type": {}}

_db_lock = asyncio.Lock()

async def save_hash_to_db(new_hash: str):
    seen_hashes.append(new_hash)
    with open(DB_FILE, "w") as f:
        json.dump(seen_hashes, f)

async def update_eco_stats(item_type: str, co2_grams: int):
    eco_stats["total_items"] += 1
    eco_stats["total_co2_grams"] += co2_grams
    eco_stats["items_by_type"][item_type] = eco_stats["items_by_type"].get(item_type, 0) + 1
    with open(STATS_FILE, "w") as f:
        json.dump(eco_stats, f)


# ─── 4. EXIF Live-Capture Verification ───────────────────────
MAX_PHOTO_AGE_SECONDS = 120  # Reject images older than 2 minutes

def check_exif_timestamp(image: Image.Image) -> dict:
    """
    Checks if the image has EXIF data with a recent timestamp.
    Returns {"live": True/False, "reason": "..."}.
    A missing EXIF is suspicious (downloaded images rarely have it).
    """
    try:
        exif_data = image._getexif()
    except Exception:
        exif_data = None

    if exif_data is None:
        return {
            "live": False,
            "reason": "No EXIF metadata found — image may not be from a camera."
        }

    # ExifBase.DateTimeOriginal = 36867
    dt_original = exif_data.get(ExifBase.DateTimeOriginal) or exif_data.get(36867)
    if dt_original is None:
        # Some cameras don't write DateTimeOriginal, check DateTime (306)
        dt_original = exif_data.get(ExifBase.DateTime) or exif_data.get(306)

    if dt_original is None:
        return {
            "live": False,
            "reason": "No capture timestamp in EXIF — image may not be a fresh photo."
        }

    # Parse EXIF datetime (format: "YYYY:MM:DD HH:MM:SS")
    try:
        photo_time = datetime.strptime(dt_original, "%Y:%m:%d %H:%M:%S")
        # Treat as local time, compare with current local time
        now = datetime.now()
        age = abs((now - photo_time).total_seconds())

        if age > MAX_PHOTO_AGE_SECONDS:
            return {
                "live": False,
                "reason": f"Photo is {int(age)}s old (max {MAX_PHOTO_AGE_SECONDS}s). Please take a new photo."
            }
        return {"live": True, "reason": "Fresh capture confirmed."}
    except ValueError:
        return {"live": True, "reason": "EXIF timestamp present but unparseable — allowing."}


# ─── 5. Main Verification Endpoint ───────────────────────────
@app.post("/verify-daily-photo")
async def verify_photo(file: UploadFile = File(...), skip_exif: bool = False):
    if not file.content_type.startswith("image/"):
        raise HTTPException(status_code=400, detail="Invalid file type.")

    # ── Read & validate size ──────────────────────────────────
    image_data = await file.read()
    if len(image_data) > 10 * 1024 * 1024:
        raise HTTPException(status_code=413, detail="Image too large (max 10 MB).")

    image = Image.open(io.BytesIO(image_data)).convert("RGB")

    # ── EXIF live-capture check (can be skipped for local/dev testing) ─
    if not skip_exif:
        image_with_exif = Image.open(io.BytesIO(image_data))
        exif_check = check_exif_timestamp(image_with_exif)
        if not exif_check["live"]:
            return {
                "verified": False,
                "message": f"Live-capture check failed: {exif_check['reason']}"
            }

    # ── Run YOLOE Detection ───────────────────────────────────
    # Threshold at 0.20 allows zero-shot vision embeddings to pick up
    # real-world webcam/phone angles, lighting changes, and wrappers reliably
    results = model.predict(image, conf=0.20)
    result = results[0]

    if result.boxes is None or len(result.boxes) == 0:
        return {
            "verified": False,
            "message": "No recyclable item detected. Try again."
        }

    # ── Select Highest-Confidence Detection ───────────────────
    confidences = result.boxes.conf.tolist()
    best_idx = confidences.index(max(confidences))

    best_conf = confidences[best_idx]
    best_cls_id = int(result.boxes.cls[best_idx])
    raw_label = result.names[best_cls_id]
    best_label = CLASS_SYNONYMS.get(raw_label, raw_label)
    best_bbox = result.boxes.xyxy[best_idx].tolist()

    # ── ANTI-CHEAT: Crop to Bounding Box & Hash ──────────────
    x1, y1, x2, y2 = [int(c) for c in best_bbox]
    cropped = image.crop((x1, y1, x2, y2))
    current_hash = imagehash.phash(cropped)

    async with _db_lock:
        for stored_hash_str in seen_hashes:
            stored_hash = imagehash.hex_to_hash(stored_hash_str)
            if current_hash - stored_hash < 10:
                return {
                    "verified": False,
                    "detected_item": best_label,
                    "confidence_score": round(best_conf, 3),
                    "bbox": [x1, y1, x2, y2],
                    "message": "Anti-cheat triggered: You already recycled this exact item!"
                }

        # ── Valid & Not Duplicate → Save & Return ─────────────
        co2_grams = CO2_SAVINGS.get(best_label, 50)
        await save_hash_to_db(str(current_hash))
        await update_eco_stats(best_label, co2_grams)

    return {
        "verified": True,
        "detected_item": best_label,
        "item_detail": raw_label,
        "confidence_score": round(best_conf, 3),
        "bbox": [x1, y1, x2, y2],
        "co2_saved_grams": co2_grams,
        "co2_equivalence": get_co2_equivalence(co2_grams),
        "message": f"Eco-streak validated! You saved ~{co2_grams}g of CO₂ 🌍"
    }


# ─── 6. Eco Impact Stats Endpoint (for leaderboard/game) ─────
@app.get("/eco-stats")
async def get_eco_stats():
    return {
        "total_items_recycled": eco_stats["total_items"],
        "total_co2_saved_grams": eco_stats["total_co2_grams"],
        "total_co2_saved_kg": round(eco_stats["total_co2_grams"] / 1000, 2),
        "co2_equivalence": get_co2_equivalence(eco_stats["total_co2_grams"]),
        "items_by_type": eco_stats["items_by_type"],
    }


# ─── 7. Interactive In-Browser Tester ────────────────────────
@app.get("/", response_class=HTMLResponse)
async def web_tester():
    return """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Uncycled — Eco Verification Tester</title>
  <style>
    * { box-sizing: border-box; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
    body { background: #0f172a; color: #f8fafc; margin: 0; padding: 20px; display: flex; flex-direction: column; align-items: center; }
    .card { background: #1e293b; padding: 24px; border-radius: 16px; width: 100%; max-width: 520px; box-shadow: 0 10px 25px rgba(0,0,0,0.5); }
    h1 { margin-top: 0; color: #22c55e; font-size: 22px; text-align: center; }
    video, #preview { width: 100%; border-radius: 12px; background: #000; margin-bottom: 12px; aspect-ratio: 4/3; object-fit: cover; }
    .row { display: flex; gap: 8px; margin-bottom: 12px; }
    button { flex: 1; padding: 12px; font-weight: 600; border: none; border-radius: 8px; cursor: pointer; transition: 0.2s; }
    .btn-green { background: #22c55e; color: #000; }
    .btn-green:hover { background: #16a34a; }
    .btn-blue { background: #3b82f6; color: #fff; }
    .btn-blue:hover { background: #2563eb; }
    .opt-box { font-size: 13px; color: #94a3b8; margin-bottom: 14px; display: flex; align-items: center; gap: 8px; }
    #resultBox { margin-top: 16px; padding: 16px; border-radius: 10px; display: none; font-size: 14px; line-height: 1.6; }
    .success { background: #14532d; border: 1px solid #22c55e; color: #bbf7d0; }
    .fail { background: #7f1d1d; border: 1px solid #ef4444; color: #fecaca; }
    pre { background: #0f172a; padding: 10px; border-radius: 6px; overflow-x: auto; font-size: 12px; margin-top: 8px; color: #e2e8f0; }
  </style>
</head>
<body>
  <div class="card">
    <h1>♻️ Uncycled AI Tester</h1>
    <video id="video" autoplay playsinline></video>
    <img id="preview" style="display:none;" />
    
    <div class="row">
      <button class="btn-green" id="snapBtn">📸 Take Photo & Verify</button>
      <button class="btn-blue" id="switchBtn">🔄 Switch / Restart Cam</button>
    </div>

    <div class="opt-box">
      <input type="checkbox" id="skipExif" checked />
      <label for="skipExif">Bypass EXIF timestamp check (recommended for web test)</label>
    </div>

    <div style="margin-top: 10px;">
      <label style="font-size: 13px; color: #94a3b8;">Or test an image file from your PC:</label>
      <input type="file" id="fileInput" accept="image/*" style="margin-top: 6px; width: 100%; font-size: 13px; color: #cbd5e1;" />
    </div>

    <div id="resultBox"></div>
  </div>

  <canvas id="canvas" style="display:none;"></canvas>

  <script>
    const video = document.getElementById('video');
    const canvas = document.getElementById('canvas');
    const resultBox = document.getElementById('resultBox');
    const skipExifCheck = document.getElementById('skipExif');
    let stream = null;

    async function initCamera() {
      try {
        if (stream) stream.getTracks().forEach(t => t.stop());
        stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: "environment" }, audio: false });
        video.srcObject = stream;
        video.style.display = 'block';
        document.getElementById('preview').style.display = 'none';
      } catch (err) {
        resultBox.style.display = 'block';
        resultBox.className = 'fail';
        resultBox.innerHTML = '⚠️ Camera permission denied or not available. You can still test using file upload below.';
      }
    }
    initCamera();
    document.getElementById('switchBtn').onclick = initCamera;

    async function sendImage(blob) {
      resultBox.style.display = 'block';
      resultBox.className = '';
      resultBox.style.background = '#334155';
      resultBox.style.color = '#fff';
      resultBox.innerHTML = '⏳ Analyzing with YOLOE + Anti-Cheat Hash...';

      const formData = new FormData();
      formData.append('file', blob, 'capture.jpg');
      const skip = skipExifCheck.checked ? '?skip_exif=true' : '';

      try {
        const resp = await fetch('/verify-daily-photo' + skip, { method: 'POST', body: formData });
        const data = await resp.json();
        
        resultBox.className = data.verified ? 'success' : 'fail';
        resultBox.innerHTML = (data.verified ? '✅ <b>VALID RECYCLING!</b>' : '❌ <b>VERIFICATION REJECTED</b>')
          + '<br><b>Message:</b> ' + data.message
          + (data.detected_item ? '<br><b>Detected Item:</b> ' + data.detected_item : '')
          + (data.confidence_score ? '<br><b>Confidence:</b> ' + (data.confidence_score * 100).toFixed(1) + '%' : '')
          + (data.co2_saved_grams ? '<br><b>CO₂ Saved:</b> -' + data.co2_saved_grams + 'g (' + data.co2_equivalence + ')' : '')
          + '<pre>' + JSON.stringify(data, null, 2) + '</pre>';
      } catch (err) {
        resultBox.className = 'fail';
        resultBox.innerHTML = '⚠️ Error contacting server: ' + err.message;
      }
    }

    document.getElementById('snapBtn').onclick = () => {
      canvas.width = video.videoWidth || 640;
      canvas.height = video.videoHeight || 480;
      const ctx = canvas.getContext('2d');
      ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
      
      canvas.toBlob((blob) => {
        document.getElementById('preview').src = URL.createObjectURL(blob);
        document.getElementById('preview').style.display = 'block';
        video.style.display = 'none';
        sendImage(blob);
      }, 'image/jpeg', 0.9);
    };

    document.getElementById('fileInput').onchange = (e) => {
      const file = e.target.files[0];
      if (file) {
        document.getElementById('preview').src = URL.createObjectURL(file);
        document.getElementById('preview').style.display = 'block';
        video.style.display = 'none';
        sendImage(file);
      }
    };
  </script>
</body>
</html>
"""

