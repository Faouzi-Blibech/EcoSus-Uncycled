# Eco photo verifier

FastAPI service that checks a daily recycling photo, keeps each player's eco-streak,
CO₂ total and unlocked skins, and returns the item's outline for the phone to draw.

- Routes: `app.py`. Pipeline: `ecoverify/service.py`. Tuning, skins, taxonomy: `content/eco_verify.json`.
- Models: YOLOv8s-seg (COCO) finds the item and its outline; CLIP picks the material of a
  bottle/cup crop (plastic, glass, can, carton...) and spots the same item from another angle.
- Fallback: COCO has no paper, cardboard, cans or circuit boards. When YOLO finds no recyclable
  it is sure of, GrabCut cuts out the main object and CLIP names it from the `fallback` list in
  `content/eco_verify.json`. That list includes "not waste" labels (person, room, a photo of a
  screen...) so it rejects instead of inventing an item. Threshold: `fallback_min_prob`.
- Storage: SQLite (`ECO_DB`, default `eco_verify.db`).

- Litter model (optional): YOLOv8s-seg fine-tuned on TACO, at `ecoverify/models/taco-seg.pt`.
  Runs next to the COCO model and wins when both see the same object (it knows cans, wrappers,
  cartons, foil, styrofoam...). Classes: `content/taco_classes.json`. Off when the file is missing
  or with `ECO_DISABLE_TACO=1`; `/health` shows whether it is loaded.

## Train the litter model (TACO)

The weights are not in git (`*.pt` is ignored). Either copy `taco-seg.pt` from a teammate into
`ecoverify/models/`, or train it:

```powershell
venv\Scripts\python.exe -m ecoverify.train.taco_prepare
```
Downloads TACO (1500 photos, 640 px copies from Flickr, plus annotations from GitHub) and writes a
YOLO-seg dataset to `ecoverify/train/data/taco/yolo/`. Classes with fewer than 15 examples are dropped.

GPU training needs CUDA torch, kept in its own venv so the server venv stays small:
```powershell
py -3.14 -m venv venv-train
venv-train\Scripts\python.exe -m pip install torch==2.14.1 torchvision --index-url https://download.pytorch.org/whl/cu130
venv-train\Scripts\python.exe -m pip install ultralytics==8.4.215
venv-train\Scripts\python.exe -m ecoverify.train.taco_train
```
That takes ~1–2 h on an RTX 4050 laptop GPU and writes `ecoverify/models/taco-seg.pt` and
`taco-seg.json` (metrics). Without a GPU, use
`venv\Scripts\python.exe -m ecoverify.train.taco_train --device cpu --model yolov8n-seg.pt --epochs 30`
(several hours, weaker model).

Check it on close-ups (what players send) before shipping new weights:
```powershell
venv\Scripts\python.exe -m ecoverify.train.closeup_eval
```
Current model (64 epochs, mask mAP50 0.248 on TACO val): on 264 held-out close-ups the right
material is accepted 49% of the time instead of 41% without it, with no more wrong materials
(19%), using `taco_accept_conf` 0.6 and only the classes not marked `"trust": false`.

## Run

Local (Windows, from the repo root):

```powershell
$env:UNCYCLED_DEV_SKIP_EXIF = "1"   # local tests only: lets the browser tester through
venv\Scripts\python.exe -m uvicorn app:app --host 0.0.0.0 --port 8000
```

Docker (from the repo root; the first build downloads torch and the models, ~2 GB, then runs offline):

```powershell
docker compose up --build
```

Open http://localhost:8000 for the browser tester (webcam or file upload, draws the outline).

## Test

| What | Command |
|---|---|
| Unit tests (rules, anti-cheat, pipeline with fake models) | `venv\Scripts\python.exe -m unittest discover -s ecoverify/tests -t .` |
| Same, inside Docker | `docker compose run --rm verifier python -m unittest discover -s ecoverify/tests -t .` |
| End-to-end against a running server (real models) | `venv\Scripts\python.exe -m ecoverify.smoke_test http://localhost:8000` |
| Same, inside Docker | `docker compose exec verifier python -m ecoverify.smoke_test` |

Neither needs test photos: the unit tests generate their own images and the smoke test uses
the sample photos bundled with Ultralytics. The smoke test makes a new random player each run.

## Pitch demo (Android)

The game is an Android game: "Scan an item" opens the camera page on the phone itself (no QR code).

1. **Before the pitch, once:** build the images (`docker compose build`, ~2 GB to download the first time).
2. Laptop and phone on the **same Wi-Fi**, or the laptop on the phone's **hotspot** (venue Wi-Fi often
   keeps devices apart). Docker Desktop running.
3. On the laptop: `.\start-demo.ps1`. It prints the laptop's address, e.g. `192.168.1.118`.
4. On the phone, in the game: **Settings -> Server** `ws://192.168.1.118:5000/ws`, Save. The scan
   service is found on the same laptop automatically (or type `http://192.168.1.118:8000` in
   **Scan service address**).
5. Menu -> **Scan & earn** -> **Scan an item**: the camera page opens -> **Open the camera** -> photo of a
   bottle, can or paper -> the item, bin, CO2 and points appear -> phone **Back** -> the menu shows
   "Scan saved!" and the new points and streak. The first scan unlocks a character for the lobby.

Talking points: real photo, detected on our own server (YOLO + CLIP + a model we fine-tuned on TACO
litter photos), item outline, CO2 saved, anti-cheat (same item, other angle, old photo), daily streak
that unlocks characters in the game.

## Phone

Easiest (no app to install): the browser scan page.

1. Phone and PC on the same Wi-Fi (or the PC on the phone's hotspot).
2. Start the service (`docker compose up -d`) and find the PC's Wi-Fi address: `ipconfig`, line
   "IPv4 Address" under the Wi-Fi adapter (e.g. `172.20.10.3`).
3. On the PC, open `http://<that address>:8000` (not `localhost`: the phone cannot reach that).
4. "📱 Test on my phone" -> "Make a scan link for my phone", then point the phone's camera at the QR code
   and open the link. Take a photo of the item on the page that opens.
5. If the phone cannot open the page, allow Docker / port 8000 through Windows Firewall on private networks.

The link works once and expires after 2 minutes. The browser tester runs with
`UNCYCLED_DEV_SKIP_EXIF=1` in `docker-compose.yml`; the scan page does not need it.

Mobile app (Expo): set the PC address in `mobile/services/api.js` (`API_BASE_URL`), then
`npx expo start` and scan its QR code with Expo Go.

## Scan links (the game's scan button)

1. The game calls `POST /scan/start` with its player id and opens the returned `url` (web: same tab;
   Android: browser) or shows `qr_url` (Windows) for a phone to scan.
2. The phone page uploads one photo with the token. Only a verified item uses the link up and counts:
   10 points per scan, at most 3 scans a day score (`points_per_scan`, `max_scored_scans_per_day`).
3. The game refreshes `GET /profile/{id}` to show points, streak and skins (`content/skins.json`).

Links point at this service's public address: set `ECO_PUBLIC_URL` (for example the `cloudflared` tunnel
URL), or the service uses the forwarded host and scheme the tunnel sends.

## API

| Method | Path | Notes |
|---|---|---|
| POST | `/verify-daily-photo` | multipart `file`; headers `X-Player-Id` (UUID, required), `X-Tz-Offset-Minutes` (JS `getTimezoneOffset()`); no client option can skip the EXIF check |
| GET | `/players/{id}`, `/profile/{id}` | points, streak, scans today, totals, unlocked skins (`skins` ids and full objects), next skin |
| POST | `/scan/start` | JSON `{"player_id": "<uuid>"}` -> `{token, url, qr_url, expires_s}`: a single-use scan link valid 120 s |
| GET | `/scan?t=` | phone page: take a photo, see the outline, points, streak and new skins, "Back to game" |
| POST | `/scan/upload` | form `token`, `file`: the link replaces the EXIF check; a rejected photo gives the link back for a retry; 410 when expired or used |
| GET | `/scan/qr?t=` | PNG QR code of the scan link (for the Windows build) |
| GET | `/eco-stats` | totals across all players |
| GET | `/recycling-guide` | taxonomy and skins |
| GET | `/health` | model loaded, CLIP available |

Every verify response has `verified`, `reason` (`ok`, `exif`, `no_object`, `not_recyclable`,
`low_confidence`, `not_waste`, `duplicate`) and `message`. When an item was seen it also has
`detected_class`, `confidence_score`, `bbox`, `image_size` `[w, h]` and `contour`
(`[[x, y], ...]`, 0..1 of the upright photo).

The EXIF freshness check can only be turned off on the server, for local tests:
`UNCYCLED_DEV_SKIP_EXIF=1` (the browser tester needs it, since webcam shots carry no EXIF).
`docker-compose.yml` sets it for local use; never set it on the demo server.
