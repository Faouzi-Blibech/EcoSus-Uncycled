import requests
import json
from PIL import Image
import piexif
from datetime import datetime, timedelta

API_URL = "http://localhost:8000"

print("--- 1. Testing image without EXIF (should fail live-capture check) ---")
img_no_exif = Image.new("RGB", (300, 300), color=(100, 150, 200))
img_no_exif.save("no_exif.jpg", "JPEG")
with open("no_exif.jpg", "rb") as f:
    resp = requests.post(f"{API_URL}/verify-daily-photo", files={"file": ("no_exif.jpg", f, "image/jpeg")})
    print(resp.json())

print("\n--- 2. Testing image with expired EXIF (10 minutes old) ---")
ten_mins_ago = (datetime.now() - timedelta(minutes=10)).strftime("%Y:%m:%d %H:%M:%S")
exif_dict = {
    "0th": {piexif.ImageIFD.Make: b"TestCamera"},
    "Exif": {piexif.ExifIFD.DateTimeOriginal: ten_mins_ago.encode()}
}
exif_bytes = piexif.dump(exif_dict)
img_old_exif = Image.new("RGB", (300, 300), color=(120, 160, 210))
img_old_exif.save("old_exif.jpg", "JPEG", exif=exif_bytes)
with open("old_exif.jpg", "rb") as f:
    resp = requests.post(f"{API_URL}/verify-daily-photo", files={"file": ("old_exif.jpg", f, "image/jpeg")})
    print(resp.json())
