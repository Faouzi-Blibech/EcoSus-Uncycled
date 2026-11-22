"""Does the litter model help on close-up photos (what players actually send)?

Crops each held-out TACO validation item so it fills ~45% of the frame, then runs the whole
verifier with and without the litter model and counts how often the right material stream is
accepted. Also lists what the litter model fires on in photos that are not litter.

    venv\\Scripts\\python.exe -m ecoverify.train.closeup_eval
"""
import io
import json
import sys
import uuid
from datetime import datetime, timezone
from pathlib import Path

from PIL import Image

from ecoverify import config
from ecoverify.clip import ClipEmbedder
from ecoverify.service import Verifier
from ecoverify.store import Store
from ecoverify.vision import Detector

HERE = Path(__file__).resolve().parent
YOLO_DIR = HERE / "data" / "taco" / "yolo"
WEIGHTS = HERE.parent / "models" / "taco-seg.pt"


def closeups(min_area: float = 0.004, fill: float = 0.45):
    names = [l.split(": ", 1)[1] for l in (YOLO_DIR / "data.yaml").read_text().splitlines() if l.startswith("  ")]
    for lab in sorted((YOLO_DIR / "labels" / "val").glob("*.txt")):
        im = Image.open(YOLO_DIR / "images" / "val" / f"{lab.stem}.jpg").convert("RGB")
        w, h = im.size
        for line in lab.read_text().splitlines():
            v = line.split()
            xs, ys = [float(x) * w for x in v[1::2]], [float(y) * h for y in v[2::2]]
            x1, y1, x2, y2 = min(xs), min(ys), max(xs), max(ys)
            if (x2 - x1) * (y2 - y1) < min_area * w * h:
                continue  # too small to be a fair close-up
            side = max(x2 - x1, y2 - y1) / fill
            cx, cy = (x1 + x2) / 2, (y1 + y2) / 2
            box = (max(0, cx - side / 2), max(0, cy - side / 2), min(w, cx + side / 2), min(h, cy + side / 2))
            crop = im.crop(tuple(int(b) for b in box))
            crop.thumbnail((640, 640))
            yield names[int(v[0])], crop


def jpeg(im: Image.Image) -> bytes:
    buf = io.BytesIO()
    im.save(buf, "JPEG", quality=92)
    return buf.getvalue()


def main() -> None:
    if not WEIGHTS.exists():
        raise SystemExit(f"{WEIGHTS} not found: train first")
    coco = Detector(config.TUNING["model"])
    taco = Detector(str(WEIGHTS))
    clip = ClipEmbedder(config.TUNING["clip_model"])
    now = datetime.now(timezone.utc)
    pipelines = {
        "COCO+CLIP (before)": Verifier(Store(":memory:"), coco, clip),
        "COCO+CLIP+TACO (now)": Verifier(Store(":memory:"), coco, clip, taco=taco),
    }
    samples = list(closeups())
    print(f"{len(samples)} close-ups from held-out validation photos\n")
    tally = {k: {"right": 0, "wrong": 0, "rejected": 0} for k in pipelines}
    per_class = {}
    for truth, img in samples:
        want = config.TACO[truth]["stream"]
        data = jpeg(img)
        for name, v in pipelines.items():
            r = v.verify(data, str(uuid.uuid4()), now, skip_exif=True)
            outcome = "rejected" if not r["verified"] else ("right" if r["stream"] == want else "wrong")
            tally[name][outcome] += 1
            per_class.setdefault(truth, {}).setdefault(name, {"right": 0, "n": 0})
            per_class[truth][name]["n"] += 1
            per_class[truth][name]["right"] += outcome == "right"

    n = len(samples)
    for name, t in tally.items():
        print(f"{name:22s} right stream {t['right'] / n:6.1%}   wrong stream {t['wrong'] / n:6.1%}   rejected {t['rejected'] / n:6.1%}")
    print("\nright-stream rate per class (before -> now):")
    for cls, d in sorted(per_class.items(), key=lambda kv: -next(iter(kv[1].values()))["n"]):
        a, b = d["COCO+CLIP (before)"], d["COCO+CLIP+TACO (now)"]
        print(f"  {cls:18s} n={a['n']:3d}  {a['right'] / a['n']:5.0%} -> {b['right'] / b['n']:5.0%}")

    print("\nlitter model on photos that are not litter (detections >= taco_accept_conf):")
    assets = Path(sys.modules["ultralytics"].__file__).parent / "assets"
    extra = [Path(p) for p in sys.argv[1:]]
    for p in [assets / "bus.jpg", assets / "zidane.jpg", *extra]:
        dets = [(d.coco_class, round(d.conf, 2)) for d in taco.detect(Image.open(p).convert("RGB"), 0.25)
                if d.conf >= config.TUNING["taco_accept_conf"]]
        print(f"  {p.name:12s} {dets or 'nothing'}")


if __name__ == "__main__":
    main()
