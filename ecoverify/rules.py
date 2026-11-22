"""Pure rules: no I/O, no clocks (the caller passes 'now'). Unit-tested in tests/test_rules.py."""
from datetime import date, datetime, timedelta, timezone


def day_key(now_utc: datetime, utc_offset_hours: float) -> date:
    """The game's calendar day. Decided by the server, never by the phone."""
    return (now_utc.astimezone(timezone.utc) + timedelta(hours=utc_offset_hours)).date()


def next_streak(last_day: date | None, today: date, streak: int) -> int:
    """Streak after a verified photo today."""
    if last_day == today:
        return streak
    if last_day == today - timedelta(days=1):
        return streak + 1
    return 1


def live_streak(last_day: date | None, today: date, streak: int) -> int:
    """Streak as shown right now: 0 once a whole day was missed."""
    if last_day is None or last_day < today - timedelta(days=1):
        return 0
    return streak


def new_unlocks(streak: int, unlocked_ids: set[str], skins: list[dict]) -> list[dict]:
    return [s for s in skins if s.get("required_streak") is not None
            and s["required_streak"] <= streak and s["id"] not in unlocked_ids]


def next_skin(unlocked_ids: set[str], skins: list[dict]) -> dict | None:
    """The next skin the streak unlocks (shop items are bought, not unlocked)."""
    return next((s for s in skins if s.get("required_streak") is not None and s["id"] not in unlocked_ids), None)


def points(scans_per_day: list[int], per_scan: int, max_per_day: int) -> int:
    """Game points: per_scan for each verified scan, at most max_per_day scans count per day."""
    return per_scan * sum(min(n, max_per_day) for n in scans_per_day)


def award_co2(kind: str, item_co2: int, streak_co2_today: int, depot_count_today: int,
              daily_cap: int, depot_max_per_day: int) -> tuple[int, str | None]:
    """CO2 credited for one verified item, and a note when it was reduced.

    streak items share a daily cap; depot items are not capped but only
    depot_max_per_day of them earn CO2 per day.
    """
    if kind == "depot":
        if depot_count_today >= depot_max_per_day:
            return 0, f"Drop-off items earn CO₂ {depot_max_per_day}x per day. Streak still counted."
        return item_co2, None
    left = max(0, daily_cap - streak_co2_today)
    if left == 0:
        return 0, f"Daily CO₂ limit reached ({daily_cap} g). Streak still counted."
    if item_co2 > left:
        return left, f"Capped at the daily limit ({daily_cap} g)."
    return item_co2, None


def can_buy(item: dict | None, owned: set[str], currency: int) -> str | None:
    """None when the purchase is allowed, else the reason: unknown, owned or not_enough."""
    if item is None:
        return "unknown"
    if item["id"] in owned:
        return "owned"
    if currency < int(item["price"]):
        return "not_enough"
    return None
