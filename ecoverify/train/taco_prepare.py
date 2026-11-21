"""Download TACO and convert it to an Ultralytics YOLO-seg dataset.

    venv\\Scripts\\python.exe -m ecoverify.train.taco_prepare            # download + convert
    venv\\Scripts\\python.exe -m ecoverify.train.taco_prepare --no-download

TACO: Trash Annotations in Context (Proença & Simões, 2020), CC BY 4.0,
https://github.com/pedropro/TACO. ~1500 photos of litter, 60 categories, polygon masks.
Classes are merged with content/taco_classes.json. Output: ecoverify/train/data/taco/yolo/
"""
import argparse
import json
import random
import time
import urllib.request
from collections import Counter
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parents[2]
CLASS_FILE = ROOT / "content" / "taco_classes.json"
DATA = Path(__file__).resolve().parent / "data" / "taco"
ANN_URL = "https://raw.githubusercontent.com/pedropro/TACO/master/data/annotations.json"
UA = {"User-Agent": "Mozilla/5.0 (dataset download script)"}
MAX_SIDE = 1024


def fetch(url: str, dest: Path, tries: int = 3) -> bool:
    for attempt in range(tries):
        try:
            req = urllib.request.Request(url, headers=UA)
            with urllib.request.urlopen(req, timeout=60) as r:
                data = r.read()
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(data)
            return True
        except Exception:
            time.sleep(1 + attempt * 2)
    return False


def download(anns: dict, workers: int) -> None:
    raw = DATA / "raw"

    def one(img: dict) -> tuple[str, bool]:
        dest = raw / img["file_name"]
        if dest.exists() and dest.stat().st_size > 0:
            return img["file_name"], True
        urls = [u for u in (img.get("flickr_640_url"), img.get("flickr_url")) if u]
        return img["file_name"], any(fetch(u, dest) for u in urls)

    imgs = anns["images"]
    done, failed = 0, []
    with ThreadPoolExecutor(workers) as pool:
        for name, ok in pool.map(one, imgs):
            done += 1
            if not ok:
                failed.append(name)
            if done % 100 == 0 or done == len(imgs):
                print(f"  downloaded {done}/{len(imgs)} ({len(failed)} failed)", flush=True)
    if failed:
        print(f"  {len(failed)} images could not be downloaded; they are skipped")


def largest_polygon(seg) -> list[float] | None:
    if not isinstance(seg, list) or not seg:
        return None  # RLE masks are not used in TACO's polygon annotations
    polys = [p for p in seg if len(p) >= 6]
    if not polys:
        return None

    def area(p):
        xs, ys = p[0::2], p[1::2]
        return abs(sum(xs[i] * ys[i - 1] - xs[i - 1] * ys[i] for i in range(len(xs)))) / 2

    return max(polys, key=area)


def convert(anns: dict, classes: dict, min_instances: int, val_ratio: float, seed: int) -> None:
    taco_to_ours = {t: ours for ours, c in classes.items() for t in c["taco"]}
    cat_names = {c["id"]: c["name"] for c in anns["categories"]}
    unknown = sorted(set(taco_to_ours) - set(cat_names.values()))
    if unknown:
        raise SystemExit(f"taco_classes.json names not in TACO: {unknown}")

    by_image: dict[int, list] = {}
    for a in anns["annotations"]:
        ours = taco_to_ours.get(cat_names[a["category_id"]])
        if ours:
            by_image.setdefault(a["image_id"], []).append((ours, a["segmentation"]))

    counts = Counter(ours for items in by_image.values() for ours, _ in items)
    kept = [c for c in classes if counts[c] >= min_instances]
    dropped = {c: counts[c] for c in classes if c not in kept}
    print("  instances per class:", dict(sorted(counts.items(), key=lambda kv: -kv[1])))
    if dropped:
        print(f"  dropped (< {min_instances} instances):", dropped)
    class_id = {c: i for i, c in enumerate(kept)}

    out = DATA / "yolo"
    images = [i for i in anns["images"] if i["id"] in by_image]
    random.Random(seed).shuffle(images)
    n_val = int(len(images) * val_ratio)
    skipped = Counter()
    written = Counter()
    for k, img in enumerate(images):
        split = "val" if k < n_val else "train"
        src = DATA / "raw" / img["file_name"]
        if not src.exists():
            skipped["missing"] += 1
            continue
        try:
            im = ImageOps.exif_transpose(Image.open(src)).convert("RGB")
        except Exception:
            skipped["unreadable"] += 1
            continue
        w0, h0 = img["width"], img["height"]
        if abs(im.width / im.height - w0 / h0) > 0.03:
            skipped["orientation mismatch"] += 1  # annotation was made on a differently rotated image
            continue
        lines = []
        for ours, seg in by_image[img["id"]]:
            poly = largest_polygon(seg)
            if ours not in class_id or poly is None:
                continue
            xy = [min(1.0, max(0.0, v / (w0 if i % 2 == 0 else h0))) for i, v in enumerate(poly)]
            lines.append(f"{class_id[ours]} " + " ".join(f"{v:.5f}" for v in xy))
        if not lines:
            skipped["no kept class"] += 1
            continue
        if max(im.size) > MAX_SIDE:
            im.thumbnail((MAX_SIDE, MAX_SIDE))
        stem = img["file_name"].replace("/", "_").rsplit(".", 1)[0]
        (out / "images" / split).mkdir(parents=True, exist_ok=True)
        (out / "labels" / split).mkdir(parents=True, exist_ok=True)
        im.save(out / "images" / split / f"{stem}.jpg", quality=92)
        (out / "labels" / split / f"{stem}.txt").write_text("\n".join(lines) + "\n")
        written[split] += 1

    yaml = [f"path: {out.as_posix()}", "train: images/train", "val: images/val", "names:"]
    yaml += [f"  {i}: {c}" for c, i in class_id.items()]
    (out / "data.yaml").write_text("\n".join(yaml) + "\n")
    print(f"  written: {dict(written)}  skipped: {dict(skipped)}")
    print(f"  dataset: {out / 'data.yaml'}")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-download", action="store_true")
    ap.add_argument("--workers", type=int, default=12)
    ap.add_argument("--val", type=float, default=0.15)
    ap.add_argument("--seed", type=int, default=7)
    args = ap.parse_args()

    spec = json.loads(CLASS_FILE.read_text(encoding="utf-8"))
    ann_file = DATA / "annotations.json"
    if not ann_file.exists():
        print("Downloading TACO annotations...")
        if not fetch(ANN_URL, ann_file):
            raise SystemExit(f"Could not download {ANN_URL}")
    anns = json.loads(ann_file.read_text(encoding="utf-8"))
    print(f"TACO: {len(anns['images'])} images, {len(anns['annotations'])} annotations, {len(anns['categories'])} categories")
    if not args.no_download:
        print("Downloading images (640 px copies)...")
        download(anns, args.workers)
    print("Converting to YOLO-seg...")
    convert(anns, spec["classes"], spec["min_instances"], args.val, args.seed)


if __name__ == "__main__":
    main()
