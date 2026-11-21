"""
Quick test script for the Uncycled API.
Downloads a sample image of a plastic bottle and sends it to the endpoint.
Run this AFTER starting the server with: uvicorn app:app --host 0.0.0.0 --port 8000
"""
import requests
import json
import sys
from PIL import Image
from PIL.ExifTags import Base as ExifBase
import piexif
from io import BytesIO
from datetime import datetime

API_URL = "http://localhost:8000"
HEADERS = {"X-Player-Id": "00000000-0000-4000-8000-000000000001"}  # fixed test player

def create_test_image_with_exif(filename="test_bottle.jpg"):
    """Create a simple test image with valid EXIF timestamp."""
    img = Image.new("RGB", (640, 480), color=(200, 220, 255))
    
    # Add EXIF with current timestamp
    now = datetime.now().strftime("%Y:%m:%d %H:%M:%S")
    exif_dict = {
        "0th": {
            piexif.ImageIFD.Make: b"TestCamera",
            piexif.ImageIFD.DateTime: now.encode(),
        },
        "Exif": {
            piexif.ExifIFD.DateTimeOriginal: now.encode(),
        },
    }
    exif_bytes = piexif.dump(exif_dict)
    img.save(filename, "JPEG", exif=exif_bytes)
    print(f"[+] Created test image with EXIF timestamp: {now}")
    return filename

def test_verify(image_path, label=""):
    """POST an image to /verify-daily-photo and print the result."""
    print(f"\n{'='*50}")
    print(f"TEST: {label or image_path}")
    print(f"{'='*50}")
    
    with open(image_path, "rb") as f:
        files = {"file": (image_path, f, "image/jpeg")}
        try:
            resp = requests.post(f"{API_URL}/verify-daily-photo", headers=HEADERS, files=files, timeout=60)
            data = resp.json()
            print(json.dumps(data, indent=2))
            return data
        except requests.ConnectionError:
            print("[!] Can't reach server. Is uvicorn running?")
            sys.exit(1)
        except Exception as e:
            print(f"[!] Error: {e}")
            return None

def test_eco_stats():
    """GET /eco-stats"""
    print(f"\n{'='*50}")
    print("TEST: GET /eco-stats")
    print(f"{'='*50}")
    try:
        resp = requests.get(f"{API_URL}/eco-stats", timeout=10)
        print(json.dumps(resp.json(), indent=2))
    except Exception as e:
        print(f"[!] Error: {e}")

if __name__ == "__main__":
    print("Uncycled API Test Suite")
    print("Make sure the server is running: uvicorn app:app --port 8000\n")
    
    # Test 1: Image with valid EXIF (should pass EXIF check, may fail detection since it's blank)
    img_path = create_test_image_with_exif()
    result = test_verify(img_path, "Blank image with valid EXIF → expect 'No recyclable item detected'")
    
    # Test 2: Check eco stats
    test_eco_stats()
    
    print("\n" + "="*50)
    print("MANUAL TEST: To fully test detection, take a real photo of a")
    print("plastic bottle with your phone and save it as 'real_bottle.jpg'")
    print("in this folder, then run:")
    print('  python test_api.py real_bottle.jpg')
    print("="*50)
    
    # If a custom image path was passed as argument
    if len(sys.argv) > 1:
        test_verify(sys.argv[1], f"Custom image: {sys.argv[1]}")
        test_eco_stats()
