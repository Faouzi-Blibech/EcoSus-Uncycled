"""SQLite storage for players, unlocked skins and accepted submissions.

One connection, one lock: the service holds `lock` around "check duplicates,
then record" so two uploads from the same player cannot both pass.
"""
import secrets
import sqlite3
import threading
from datetime import date, datetime

SCHEMA = """
CREATE TABLE IF NOT EXISTS players (
    id          TEXT PRIMARY KEY,
    created_at  TEXT NOT NULL,
    streak      INTEGER NOT NULL DEFAULT 0,
    best_streak INTEGER NOT NULL DEFAULT 0,
    last_day    TEXT,
    total_co2   INTEGER NOT NULL DEFAULT 0,
    total_items INTEGER NOT NULL DEFAULT 0
);
CREATE TABLE IF NOT EXISTS unlocks (
    player_id   TEXT NOT NULL,
    skin_id     TEXT NOT NULL,
    unlocked_at TEXT NOT NULL,
    PRIMARY KEY (player_id, skin_id)
);
CREATE TABLE IF NOT EXISTS submissions (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    player_id   TEXT NOT NULL,
    created_at  TEXT NOT NULL,
    day         TEXT NOT NULL,
    coco_class  TEXT NOT NULL,
    stream      TEXT NOT NULL,
    item        TEXT NOT NULL,
    kind        TEXT NOT NULL,
    co2_awarded INTEGER NOT NULL,
    phash       TEXT NOT NULL,
    dhash       TEXT NOT NULL,
    embedding   BLOB
);
CREATE INDEX IF NOT EXISTS ix_sub_player_day ON submissions (player_id, day);
CREATE TABLE IF NOT EXISTS purchases (
    player_id   TEXT NOT NULL,
    item_id     TEXT NOT NULL,
    price       INTEGER NOT NULL,
    created_at  TEXT NOT NULL,
    PRIMARY KEY (player_id, item_id)
);
CREATE TABLE IF NOT EXISTS scan_tokens (
    token       TEXT PRIMARY KEY,
    player_id   TEXT NOT NULL,
    expires_at  REAL NOT NULL,
    used        INTEGER NOT NULL DEFAULT 0
);
"""


class Store:
    def __init__(self, path: str):
        self.lock = threading.RLock()
        self._db = sqlite3.connect(path, check_same_thread=False)
        self._db.row_factory = sqlite3.Row
        self._db.executescript(SCHEMA)

    def close(self) -> None:
        self._db.close()

    # ── reads ──────────────────────────────────────────────────
    def player(self, pid: str) -> sqlite3.Row | None:
        with self.lock:
            return self._db.execute("SELECT * FROM players WHERE id = ?", (pid,)).fetchone()

    def unlocked_ids(self, pid: str) -> set[str]:
        with self.lock:
            rows = self._db.execute("SELECT skin_id FROM unlocks WHERE player_id = ?", (pid,))
            return {r["skin_id"] for r in rows}

    def day_totals(self, pid: str, day: date) -> tuple[int, int]:
        """(CO2 already credited for streak items today, depot items that earned CO2 today)."""
        with self.lock:
            row = self._db.execute(
                """SELECT COALESCE(SUM(CASE WHEN kind = 'streak' THEN co2_awarded END), 0) AS co2,
                          COALESCE(SUM(CASE WHEN kind = 'depot' AND co2_awarded > 0 THEN 1 END), 0) AS depot
                   FROM submissions WHERE player_id = ? AND day = ?""",
                (pid, day.isoformat()),
            ).fetchone()
            return int(row["co2"]), int(row["depot"])

    def scans_per_day(self, pid: str) -> dict[str, int]:
        """Accepted scans per game day, e.g. {"2026-11-22": 2}."""
        with self.lock:
            rows = self._db.execute(
                "SELECT day, COUNT(*) AS n FROM submissions WHERE player_id = ? GROUP BY day", (pid,))
            return {r["day"]: int(r["n"]) for r in rows}

    # ── shop ───────────────────────────────────────────────────
    def purchases(self, pid: str) -> dict[str, int]:
        """Bought item id -> price paid."""
        with self.lock:
            rows = self._db.execute("SELECT item_id, price FROM purchases WHERE player_id = ?", (pid,))
            return {r["item_id"]: int(r["price"]) for r in rows}

    def buy(self, pid: str, item_id: str, price: int, now_iso: str) -> None:
        with self.lock, self._db:
            self._db.execute("INSERT INTO purchases (player_id, item_id, price, created_at) VALUES (?, ?, ?, ?)",
                             (pid, item_id, price, now_iso))

    # ── scan links (single use, short-lived) ───────────────────
    def issue_token(self, pid: str, now: float, ttl_s: float) -> str:
        token = secrets.token_urlsafe(16)
        with self.lock, self._db:
            self._db.execute("DELETE FROM scan_tokens WHERE expires_at < ?", (now - 3600,))
            self._db.execute("INSERT INTO scan_tokens (token, player_id, expires_at) VALUES (?, ?, ?)",
                             (token, pid, now + ttl_s))
        return token

    def token_player(self, token: str, now: float) -> str | None:
        """The player a link belongs to, without using it up. None if unknown, expired or used."""
        with self.lock:
            row = self._db.execute(
                "SELECT player_id FROM scan_tokens WHERE token = ? AND used = 0 AND expires_at >= ?",
                (token, now)).fetchone()
            return row["player_id"] if row else None

    def redeem_token(self, token: str, now: float) -> str | None:
        """Use a link up and return its player. None if unknown, expired or already used."""
        with self.lock, self._db:
            pid = self.token_player(token, now)
            if pid is not None:
                self._db.execute("UPDATE scan_tokens SET used = 1 WHERE token = ?", (token,))
            return pid

    def release_token(self, token: str) -> None:
        """Give a link back after a photo that was not accepted, so the player can retry before it expires."""
        with self.lock, self._db:
            self._db.execute("UPDATE scan_tokens SET used = 0 WHERE token = ?", (token,))

    def candidates(self, pid: str, coco_class: str) -> list[sqlite3.Row]:
        """This player's earlier submissions of the same detected class (duplicate check input)."""
        with self.lock:
            return self._db.execute(
                "SELECT day, kind, phash, dhash, embedding FROM submissions WHERE player_id = ? AND coco_class = ?",
                (pid, coco_class),
            ).fetchall()

    def global_stats(self) -> dict:
        with self.lock:
            tot = self._db.execute(
                "SELECT COUNT(*) AS n, COALESCE(SUM(co2_awarded), 0) AS co2, COUNT(DISTINCT player_id) AS players FROM submissions"
            ).fetchone()
            streams = self._db.execute("SELECT stream, COUNT(*) AS n FROM submissions GROUP BY stream").fetchall()
            items = self._db.execute("SELECT item, COUNT(*) AS n FROM submissions GROUP BY item").fetchall()
        return {
            "items": int(tot["n"]),
            "co2": int(tot["co2"]),
            "players": int(tot["players"]),
            "streams": {r["stream"]: r["n"] for r in streams},
            "items_by_type": {r["item"]: r["n"] for r in items},
        }

    # ── writes ─────────────────────────────────────────────────
    def record(self, pid: str, now: datetime, day: date, sub: dict,
               streak: int, unlock_ids: list[str]) -> None:
        """Store an accepted submission and the player's new totals in one transaction."""
        with self.lock, self._db:
            self._db.execute(
                "INSERT OR IGNORE INTO players (id, created_at) VALUES (?, ?)", (pid, now.isoformat())
            )
            self._db.execute(
                """INSERT INTO submissions (player_id, created_at, day, coco_class, stream, item, kind,
                                            co2_awarded, phash, dhash, embedding)
                   VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)""",
                (pid, now.isoformat(), day.isoformat(), sub["coco_class"], sub["stream"], sub["item"],
                 sub["kind"], sub["co2_awarded"], sub["phash"], sub["dhash"], sub.get("embedding")),
            )
            self._db.execute(
                """UPDATE players SET streak = ?, best_streak = MAX(best_streak, ?), last_day = ?,
                                      total_co2 = total_co2 + ?, total_items = total_items + 1
                   WHERE id = ?""",
                (streak, streak, day.isoformat(), sub["co2_awarded"], pid),
            )
            self._db.executemany(
                "INSERT OR IGNORE INTO unlocks (player_id, skin_id, unlocked_at) VALUES (?, ?, ?)",
                [(pid, s, now.isoformat()) for s in unlock_ids],
            )
