"""Loads content/eco_verify.json once. Everything tunable lives there, not in code."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONTENT_FILE = ROOT / "content" / "eco_verify.json"
TACO_FILE = ROOT / "content" / "taco_classes.json"
SKINS_FILE = ROOT / "content" / "skins.json"


def load(path: Path = CONTENT_FILE, taco_path: Path = TACO_FILE, skins_path: Path = SKINS_FILE) -> dict:
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    with open(skins_path, "r", encoding="utf-8") as f:
        # skins.json uses "streak" (shared with the game server); the mobile app reads "required_streak"
        data["skins"] = [dict(s, required_streak=s["streak"]) for s in json.load(f)["skins"]]
    data["taco"] = {}
    if taco_path.exists():
        with open(taco_path, "r", encoding="utf-8") as f:
            data["taco"] = json.load(f)["classes"]
    _check(data)
    return data


def _check(data: dict) -> None:
    """Fail at startup (not mid-request) if a detection points at a missing taxonomy item."""
    taxonomy = data["taxonomy"]
    entries = list(data["detections"].items())
    for coco, subs in data.get("subtypes", {}).items():
        entries += [(f"{coco}/{s['label']}", s) for s in subs if "reject_message" not in s]
    entries += [(f"fallback/{s['label']}", s) for s in data.get("fallback", []) if "reject_message" not in s]
    entries += [(f"taco/{name}", c) for name, c in data.get("taco", {}).items()]
    for name, e in entries:
        items = taxonomy.get(e["stream"], {}).get("items", {})
        if e["item"] not in items:
            raise ValueError(f"eco_verify.json: '{name}' -> unknown item '{e['stream']} / {e['item']}'")
        if e["kind"] not in ("streak", "depot"):
            raise ValueError(f"eco_verify.json: '{name}' has kind '{e['kind']}'")


CONFIG = load()
TUNING: dict = CONFIG["tuning"]
SKINS: list = sorted(CONFIG["skins"], key=lambda s: s["required_streak"])
DETECTIONS: dict = CONFIG["detections"]
SUBTYPES: dict = CONFIG.get("subtypes", {})
FALLBACK: list = CONFIG.get("fallback", [])
# Litter-model class -> taxonomy entry, only for classes trusted at run time.
TACO: dict = {name: c for name, c in CONFIG["taco"].items() if c.get("trust", True)}
TAXONOMY: dict = CONFIG["taxonomy"]


def item_co2(stream: str, item: str) -> int:
    return int(TAXONOMY[stream]["items"][item]["co2_grams"])


def item_disposal(stream: str, item: str) -> str:
    return TAXONOMY[stream]["items"][item]["disposal"]
