"""Live-capture check from EXIF capture time.

EXIF DateTimeOriginal is the phone's local time with no timezone. We turn it
into UTC with, in order: the phone's offset header, the EXIF OffsetTimeOriginal
tag, or the server's own timezone as a last resort.
"""
from datetime import datetime, timedelta, timezone

from PIL import Image

TAG_DATETIME = 306
TAG_EXIF_IFD = 0x8769
TAG_DATETIME_ORIGINAL = 36867
TAG_OFFSET_TIME_ORIGINAL = 36881


def _offset_from_tag(value: str) -> timedelta | None:
    """'+01:00' -> 1 h."""
    try:
        sign = -1 if value.strip().startswith("-") else 1
        hh, mm = value.strip().lstrip("+-").split(":")
        return sign * timedelta(hours=int(hh), minutes=int(mm))
    except (ValueError, AttributeError):
        return None


def check_live(raw: Image.Image, now_utc: datetime, max_age_s: int,
               tz_offset_minutes: int | None = None) -> tuple[bool, str]:
    """tz_offset_minutes uses the JS Date.getTimezoneOffset() sign (UTC+1 -> -60)."""
    exif = raw.getexif()
    if not exif:
        return False, "No EXIF metadata found — image may not be from a camera."
    sub = exif.get_ifd(TAG_EXIF_IFD)
    stamp = sub.get(TAG_DATETIME_ORIGINAL) or exif.get(TAG_DATETIME)
    if not stamp:
        return False, "No capture timestamp in EXIF — image may not be a fresh photo."
    try:
        local = datetime.strptime(str(stamp).strip("\x00 "), "%Y:%m:%d %H:%M:%S")
    except ValueError:
        return False, "Capture timestamp is unreadable — please take a fresh photo."

    if tz_offset_minutes is not None:
        offset = timedelta(minutes=-tz_offset_minutes)
    else:
        offset = _offset_from_tag(sub.get(TAG_OFFSET_TIME_ORIGINAL, "")) \
            or datetime.now().astimezone().utcoffset()
    taken_utc = (local - offset).replace(tzinfo=timezone.utc)

    age = abs((now_utc - taken_utc).total_seconds())
    if age > max_age_s:
        return False, f"Photo is {int(age)}s old (max {max_age_s}s). Please take a fresh photo."
    return True, "Fresh capture confirmed."
