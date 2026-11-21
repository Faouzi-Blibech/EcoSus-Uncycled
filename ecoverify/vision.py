"""Object detection (YOLOv8 segmentation), item crop, outline and perceptual hashes."""
import threading
from dataclasses import dataclass

import cv2
import imagehash
import numpy as np
from PIL import Image, ImageOps


@dataclass
class Detection:
    coco_class: str
    conf: float
    bbox: tuple[int, int, int, int]          # x1, y1, x2, y2 in pixels of the upright image
    contour: list[list[float]] | None = None  # [[x, y], ...] in pixels, None if the model has no masks


class Detector:
    """Wraps an Ultralytics model. Loads lazily; predict is serialised (models are not thread-safe)."""

    def __init__(self, model_name: str, fallback_name: str | None = None):
        self._names = [n for n in (model_name, fallback_name) if n]
        self._model = None
        self._lock = threading.Lock()
        self.loaded_name: str | None = None

    def _load(self):
        from ultralytics import YOLO
        last_error = None
        for name in self._names:
            try:
                self._model = YOLO(name)
                self.loaded_name = name
                return
            except Exception as e:  # missing weights, no network, ...
                last_error = e
        raise RuntimeError(f"No detection model could be loaded: {last_error}")

    def detect(self, image: Image.Image, conf: float) -> list[Detection]:
        with self._lock:
            if self._model is None:
                self._load()
            result = self._model.predict(image, conf=conf, verbose=False)[0]
        if result.boxes is None or len(result.boxes) == 0:
            return []
        polygons = result.masks.xy if result.masks is not None else None
        out = []
        for i in range(len(result.boxes)):
            x1, y1, x2, y2 = (int(v) for v in result.boxes.xyxy[i].tolist())
            contour = None
            if polygons is not None and len(polygons[i]) >= 3:
                contour = polygons[i].tolist()
            out.append(Detection(
                coco_class=result.names[int(result.boxes.cls[i])],
                conf=float(result.boxes.conf[i]),
                bbox=(x1, y1, x2, y2),
                contour=contour,
            ))
        out.sort(key=lambda d: d.conf, reverse=True)
        return out


def main_object(image: Image.Image, max_side: int) -> Detection | None:
    """Cut the main object out of the background with GrabCut (for items YOLO has no class for).

    Assumes the item is roughly centred and the frame border is background. Returns a
    Detection with coco_class '' (the caller names it), or None if no sensible region was found.
    """
    w, h = image.size
    scale = min(1.0, max_side / max(w, h))
    small = np.array(image.resize((max(1, int(w * scale)), max(1, int(h * scale)))))[:, :, ::-1].copy()
    sh, sw = small.shape[:2]
    mx, my = max(1, int(sw * 0.03)), max(1, int(sh * 0.03))
    mask = np.zeros((sh, sw), np.uint8)
    bg, fg = np.zeros((1, 65), np.float64), np.zeros((1, 65), np.float64)
    try:
        cv2.grabCut(small, mask, (mx, my, sw - 2 * mx, sh - 2 * my), bg, fg, 3, cv2.GC_INIT_WITH_RECT)
    except cv2.error:
        return None
    obj = np.where((mask == cv2.GC_FGD) | (mask == cv2.GC_PR_FGD), 255, 0).astype(np.uint8)
    obj = cv2.morphologyEx(obj, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
    contours, _ = cv2.findContours(obj, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    if not contours:
        return None
    big = max(contours, key=cv2.contourArea)
    if not 0.02 <= cv2.contourArea(big) / (sw * sh) <= 0.98:
        return None
    x, y, bw, bh = cv2.boundingRect(big)
    pts = (big.reshape(-1, 2) / scale).tolist()
    return Detection("", 0.0, (int(x / scale), int(y / scale), int((x + bw) / scale), int((y + bh) / scale)), pts)


def iou(a: tuple[int, int, int, int], b: tuple[int, int, int, int]) -> float:
    ix = max(0, min(a[2], b[2]) - max(a[0], b[0]))
    iy = max(0, min(a[3], b[3]) - max(a[1], b[1]))
    inter = ix * iy
    union = (a[2] - a[0]) * (a[3] - a[1]) + (b[2] - b[0]) * (b[3] - b[1]) - inter
    return inter / union if union else 0.0


def upright(raw: Image.Image) -> Image.Image:
    """Apply the EXIF orientation so a phone photo is upright before detection and hashing."""
    return ImageOps.exif_transpose(raw).convert("RGB")


def crop_item(image: Image.Image, bbox: tuple[int, int, int, int], pad_ratio: float) -> Image.Image:
    x1, y1, x2, y2 = bbox
    pad_x, pad_y = int((x2 - x1) * pad_ratio), int((y2 - y1) * pad_ratio)
    w, h = image.size
    return image.crop((max(0, x1 - pad_x), max(0, y1 - pad_y), min(w, x2 + pad_x), min(h, y2 + pad_y)))


def outline(det: Detection, size: tuple[int, int], max_points: int) -> list[list[float]]:
    """Item outline as [[x, y], ...] normalised to 0..1, simplified to at most max_points.

    Falls back to the box corners when the model gave no mask.
    """
    w, h = size
    if det.contour:
        pts = np.array(det.contour, dtype=np.float32).reshape(-1, 1, 2)
        eps = 0.002 * cv2.arcLength(pts, True)
        simple = cv2.approxPolyDP(pts, eps, True)
        while len(simple) > max_points:
            eps *= 1.5
            simple = cv2.approxPolyDP(pts, eps, True)
        raw = simple.reshape(-1, 2).tolist()
    else:
        x1, y1, x2, y2 = det.bbox
        raw = [[x1, y1], [x2, y1], [x2, y2], [x1, y2]]
    return [[round(x / w, 4), round(y / h, 4)] for x, y in raw]


# ── Perceptual hashes ─────────────────────────────────────────
def hashes(crop: Image.Image) -> tuple[str, str]:
    return str(imagehash.phash(crop)), str(imagehash.dhash(crop))


def hash_variants(crop: Image.Image) -> list[tuple[imagehash.ImageHash, imagehash.ImageHash]]:
    """Hashes of the crop under all 8 rotations/mirrors, so turning the phone does not dodge the check."""
    out = []
    for mirrored in (crop, ImageOps.mirror(crop)):
        for angle in (0, 90, 180, 270):
            img = mirrored.rotate(angle, expand=True) if angle else mirrored
            out.append((imagehash.phash(img), imagehash.dhash(img)))
    return out


def same_photo(variants, stored_p: str, stored_d: str, max_distance: int) -> bool:
    sp, sd = imagehash.hex_to_hash(stored_p), imagehash.hex_to_hash(stored_d)
    return any(p - sp < max_distance and d - sd < max_distance for p, d in variants)
