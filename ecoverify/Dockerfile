# Eco photo verifier (FastAPI + YOLOv8-seg + CLIP), CPU only.
# Build from the repository root so content/ is in the context:
#   docker build -f ecoverify/Dockerfile -t uncycled-verifier .
# or: docker compose up --build
FROM python:3.14-slim

# libGL/glib: needed by OpenCV, which Ultralytics pulls in
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgl1 libglib2.0-0 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
ENV PIP_NO_CACHE_DIR=1 \
    PYTHONUNBUFFERED=1 \
    HF_HOME=/models/hf \
    YOLO_CONFIG_DIR=/tmp/ultralytics

# CPU-only torch first: the default wheel bundles CUDA and is several GB bigger.
RUN pip install --index-url https://download.pytorch.org/whl/cpu torch==2.15.0 torchvision==0.29.2
COPY requirements.txt .
RUN pip install -r requirements.txt

# Bake the model weights into the image so the container works offline (venue wifi).
# The verifier itself never downloads (CLIP is loaded with local_files_only).
RUN python -c "from ultralytics import YOLO; YOLO('yolov8s-seg.pt')" \
 && python -c "from transformers import CLIPModel, CLIPProcessor as P; m='openai/clip-vit-base-patch32'; CLIPModel.from_pretrained(m); P.from_pretrained(m)"

ENV HF_HUB_OFFLINE=1 \
    TRANSFORMERS_OFFLINE=1

COPY content/eco_verify.json content/taco_classes.json content/
# Includes ecoverify/models/taco-seg.pt (the TACO litter model) when it has been trained or copied
# there; without it the verifier runs on COCO + CLIP only (see /health).
COPY ecoverify/ ecoverify/
COPY app.py .

# The SQLite database (players, streaks, submissions) lives on a volume.
ENV ECO_DB=/data/eco_verify.db
VOLUME /data

EXPOSE 8000
HEALTHCHECK --interval=30s --timeout=5s --start-period=90s \
  CMD python -c "import urllib.request; urllib.request.urlopen('http://localhost:8000/health', timeout=4)"
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "8000"]
