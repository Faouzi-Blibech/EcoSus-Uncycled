"""Fine-tune YOLOv8s-seg on the TACO dataset made by taco_prepare.py.

GPU (recommended; needs the CUDA venv, see ecoverify/README.md):
    venv-train\\Scripts\\python.exe -m ecoverify.train.taco_train
CPU (slow; smaller model, fewer epochs):
    venv\\Scripts\\python.exe -m ecoverify.train.taco_train --device cpu --model yolov8n-seg.pt --epochs 30

Writes the best weights to ecoverify/models/taco-seg.pt and their metrics to taco-seg.json.
"""
import argparse
import json
import shutil
from datetime import date
from pathlib import Path

HERE = Path(__file__).resolve().parent
DATA_YAML = HERE / "data" / "taco" / "yolo" / "data.yaml"
RUNS = HERE / "runs"
OUT = HERE.parent / "models"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", default="yolov8s-seg.pt")
    ap.add_argument("--epochs", type=int, default=80)
    ap.add_argument("--imgsz", type=int, default=640)
    ap.add_argument("--batch", type=int, default=8, help="8 fits a 6 GB laptop GPU at 640 px")
    ap.add_argument("--device", default="0")
    ap.add_argument("--patience", type=int, default=20)
    ap.add_argument("--workers", type=int, default=4)
    args = ap.parse_args()

    if not DATA_YAML.exists():
        raise SystemExit(f"{DATA_YAML} not found: run `python -m ecoverify.train.taco_prepare` first")

    from ultralytics import YOLO
    model = YOLO(args.model)
    model.train(
        data=str(DATA_YAML), epochs=args.epochs, imgsz=args.imgsz, batch=args.batch,
        device=args.device, patience=args.patience, workers=args.workers,
        project=str(RUNS), name="taco", exist_ok=True, seed=0, plots=True,
        close_mosaic=10, cos_lr=True,
    )

    best = RUNS / "taco" / "weights" / "best.pt"
    metrics = YOLO(str(best)).val(data=str(DATA_YAML), imgsz=args.imgsz, device=args.device,
                                  project=str(RUNS), name="taco_val", exist_ok=True, plots=False)
    names = metrics.names
    epochs_run = sum(1 for _ in open(RUNS / "taco" / "results.csv")) - 1  # early stopping may end sooner
    seg_maps = metrics.seg.maps  # per-class mask mAP50-95
    report = {
        "trained_on": "TACO (CC BY 4.0, http://tacodataset.org)",
        "date": date.today().isoformat(),
        "base_model": args.model,
        "epochs_max": args.epochs,
        "epochs_run": epochs_run,
        "imgsz": args.imgsz,
        "mask_map50": round(float(metrics.seg.map50), 4),
        "mask_map50_95": round(float(metrics.seg.map), 4),
        "box_map50": round(float(metrics.box.map50), 4),
        "per_class_mask_map50_95": {names[i]: round(float(seg_maps[i]), 4) for i in range(len(names))},
        "classes": [names[i] for i in range(len(names))],
    }
    OUT.mkdir(parents=True, exist_ok=True)
    shutil.copy2(best, OUT / "taco-seg.pt")
    (OUT / "taco-seg.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    print(f"\nSaved {OUT / 'taco-seg.pt'}")


if __name__ == "__main__":  # required on Windows: dataloader workers re-import this module
    main()
