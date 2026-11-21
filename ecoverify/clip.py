"""CLIP on the item crop: picks a sub-type from a short fixed list, and returns an
embedding used to spot the same physical item photographed from another angle.

Optional: if the weights are not available locally, `available` is False and the
verifier carries on with YOLO's class alone. Never downloads (local_files_only).
"""
import threading

import numpy as np
from PIL import Image


def _as_tensor(out):
    """transformers 4.x returns a tensor, 5.x may return a model output."""
    if hasattr(out, "image_embeds") and out.image_embeds is not None:
        return out.image_embeds
    if hasattr(out, "text_embeds") and out.text_embeds is not None:
        return out.text_embeds
    if hasattr(out, "pooler_output"):
        return out.pooler_output
    return out


class ClipEmbedder:
    def __init__(self, model_name: str):
        self._name = model_name
        self._lock = threading.Lock()
        self._model = None
        self._proc = None
        self._text_cache: dict[tuple[str, ...], np.ndarray] = {}
        self.available = True
        self.error: str | None = None

    def _load(self) -> bool:
        if self._model is not None:
            return True
        if not self.available:
            return False
        try:
            from transformers import CLIPModel, CLIPProcessor
            self._model = CLIPModel.from_pretrained(self._name, local_files_only=True).eval()
            self._proc = CLIPProcessor.from_pretrained(self._name, local_files_only=True)
            return True
        except Exception as e:
            self.available, self.error = False, str(e)
            return False

    def _texts(self, labels: tuple[str, ...]) -> np.ndarray:
        if labels not in self._text_cache:
            import torch
            inputs = self._proc(text=list(labels), return_tensors="pt", padding=True)
            with torch.no_grad():
                t = _as_tensor(self._model.get_text_features(**inputs))
            t = t / t.norm(dim=-1, keepdim=True)
            self._text_cache[labels] = t.numpy()
        return self._text_cache[labels]

    def analyse(self, crop: Image.Image, labels: list[str] | None = None
                ) -> tuple[np.ndarray, tuple[int, float] | None] | None:
        """Returns (unit embedding, (best label index, probability) or None), or None if CLIP is unavailable."""
        with self._lock:
            if not self._load():
                return None
            import torch
            inputs = self._proc(images=crop, return_tensors="pt")
            with torch.no_grad():
                img = _as_tensor(self._model.get_image_features(**inputs))
            img = (img / img.norm(dim=-1, keepdim=True)).numpy()[0].astype(np.float32)
            choice = None
            if labels:
                logits = self._texts(tuple(labels)) @ img * self._model.logit_scale.exp().item()
                probs = np.exp(logits - logits.max())
                probs /= probs.sum()
                best = int(probs.argmax())
                choice = (best, float(probs[best]))
            return img, choice


def to_blob(vec: np.ndarray) -> bytes:
    return vec.astype(np.float32).tobytes()


def cosine(vec: np.ndarray, blob: bytes) -> float:
    other = np.frombuffer(blob, dtype=np.float32)
    return float(vec @ other) if other.shape == vec.shape else 0.0
