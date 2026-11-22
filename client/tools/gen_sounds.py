"""Generates every EcoSus sound effect and music loop from code (no samples, no licences).

Run from the repo root:  python client/tools/gen_sounds.py
Writes WAV effects to client/assets/sounds/sfx/ and OGG music loops to client/assets/sounds/music/
(the music needs ffmpeg on the PATH; without it the loops are written as WAV).
Every sound is short, soft and "cozy-eco": wood, bubbles, leaves, chimes for the Cleaners;
squelches, rumbles and wobbly low notes for the Trashers.
"""
import os
import shutil
import subprocess
import wave

import numpy as np

SR = 44100
ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "sounds")
SFX_DIR = os.path.join(ROOT, "sfx")
MUSIC_DIR = os.path.join(ROOT, "music")
rng = np.random.default_rng(7)


# ---------- building blocks ----------
def t(d):
    return np.arange(int(SR * d)) / SR


def env(n, a=0.005, d=0.1, s=0.0, r=0.05, hold=0.0):
    """ADSR envelope of n samples (seconds for a, d, r, hold)."""
    a_n, d_n, h_n, r_n = int(a * SR), int(d * SR), int(hold * SR), int(r * SR)
    e = np.concatenate([
        np.linspace(0, 1, max(a_n, 1)),
        np.linspace(1, s, max(d_n, 1)),
        np.full(h_n, s),
        np.linspace(s, 0, max(r_n, 1)),
    ])
    if len(e) < n:
        e = np.concatenate([e, np.zeros(n - len(e))])
    return e[:n]


def exp_env(n, k):
    return np.exp(-np.arange(n) / SR * k)


def sine(f, d, phase=0.0):
    f = np.broadcast_to(f, t(d).shape) if np.ndim(f) else np.full(t(d).shape, f)
    return np.sin(2 * np.pi * np.cumsum(f) / SR + phase)


def tri(f, d):
    f = np.full(t(d).shape, f) if not np.ndim(f) else f
    p = np.cumsum(f) / SR
    return 2 * np.abs(2 * (p - np.floor(p + 0.5))) - 1


def sweep(f0, f1, d, curve="exp"):
    n = len(t(d))
    if curve == "exp":
        return f0 * (f1 / f0) ** np.linspace(0, 1, n)
    return np.linspace(f0, f1, n)


def noise(d):
    return rng.uniform(-1, 1, len(t(d)))


def lowpass(x, cutoff):
    a = np.exp(-2 * np.pi * cutoff / SR)
    y = np.zeros_like(x)
    acc = 0.0
    for i, v in enumerate(x):
        acc = (1 - a) * v + a * acc
        y[i] = acc
    return y


def highpass(x, cutoff):
    return x - lowpass(x, cutoff)


def bandpass(x, lo, hi):
    return lowpass(highpass(x, lo), hi)


def bell(f, d, k=6.0, ratio=2.76, idx=1.8):
    """Soft FM bell / chime."""
    tt = t(d)
    mod = idx * np.exp(-tt * k * 0.7) * np.sin(2 * np.pi * f * ratio * tt)
    return np.sin(2 * np.pi * f * tt + mod) * exp_env(len(tt), k)


def marimba(f, d=0.5):
    tt = t(d)
    x = np.sin(2 * np.pi * f * tt) + 0.25 * np.sin(2 * np.pi * f * 4 * tt) * exp_env(len(tt), 40)
    return x * exp_env(len(tt), 9) * env(len(tt), a=0.002, d=0.0, s=1.0, r=0.02, hold=d)


def pluck(f, d=0.6):
    """Karplus-Strong plucked string (soft guitar / ukulele)."""
    n = int(SR * d)
    period = max(int(SR / f), 2)
    buf = rng.uniform(-1, 1, period)
    out = np.zeros(n)
    for i in range(n):
        out[i] = buf[i % period]
        buf[i % period] = 0.5 * (buf[i % period] + buf[(i + 1) % period]) * 0.996
    return out * env(n, a=0.001, d=0.0, s=1.0, r=0.05, hold=d)


def place(total, part, at):
    s = int(at * SR)
    end = min(len(total), s + len(part))
    total[s:end] += part[: end - s]
    return total


def silence(d):
    return np.zeros(len(t(d)))


def norm(x, peak=0.8):
    m = np.max(np.abs(x)) or 1.0
    return x / m * peak


def fade(x, ms=8):
    n = int(SR * ms / 1000)
    x = x.copy()
    x[:n] *= np.linspace(0, 1, n)
    x[-n:] *= np.linspace(1, 0, n)
    return x


def write_wav(path, x, peak=0.8):
    x = fade(norm(x, peak))
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


NOTE = {n: 440.0 * 2 ** ((i - 9) / 12) for i, n in enumerate(
    ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"])}


def hz(name, octave=4):
    return NOTE[name] * 2 ** (octave - 4)


# ---------- sound effects ----------
def ui_click():
    d = 0.09
    x = sine(sweep(900, 420, d), d) * env(len(t(d)), a=0.001, d=0.07, r=0.01)
    x += 0.3 * bandpass(noise(d), 1500, 5000) * exp_env(len(t(d)), 120)
    return x


def ui_open():
    d = 0.28
    x = bandpass(noise(d), 600, 3500) * env(len(t(d)), a=0.06, d=0.18, r=0.04)
    x += 0.35 * sine(sweep(500, 900, d), d) * env(len(t(d)), a=0.02, d=0.2, r=0.05)
    return x


def ui_close():
    d = 0.24
    x = bandpass(noise(d), 500, 3000) * env(len(t(d)), a=0.02, d=0.18, r=0.04)
    x += 0.35 * sine(sweep(850, 450, d), d) * env(len(t(d)), a=0.01, d=0.18, r=0.05)
    return x


def step(variant):
    d = 0.12
    base = 140 if variant == 1 else 120
    x = sine(sweep(base * 1.6, base, d), d) * exp_env(len(t(d)), 45)
    x += 0.55 * bandpass(noise(d), 800, 4000) * exp_env(len(t(d)), 70)  # grass / leaf crunch
    return x


def dash():
    d = 0.42
    x = bandpass(noise(d), 400, 6000) * env(len(t(d)), a=0.04, d=0.3, r=0.08)
    x = x * (0.6 + 0.4 * np.sin(2 * np.pi * 9 * t(d)))  # leafy flutter
    x += 0.4 * sine(sweep(300, 1200, d), d) * env(len(t(d)), a=0.02, d=0.3, r=0.1)
    return x


def spot_cleaned():
    out = silence(0.9)
    place(out, 0.5 * bandpass(noise(0.18), 300, 2500) * exp_env(len(t(0.18)), 18), 0.0)  # bag rustle
    for i, n in enumerate([("E", 6), ("G#", 6), ("B", 6), ("E", 7)]):
        place(out, 0.6 * bell(hz(*n), 0.6, k=7), 0.08 + i * 0.07)
    return out


def deliver():
    out = silence(1.3)
    whirr = sine(sweep(80, 160, 0.55), 0.55) * env(len(t(0.55)), a=0.05, d=0.0, s=1.0, r=0.1, hold=0.4)
    whirr += 0.3 * bandpass(noise(0.55), 200, 900) * env(len(t(0.55)), a=0.05, d=0.0, s=1.0, r=0.1, hold=0.4)
    place(out, 0.6 * whirr, 0.0)
    place(out, 0.8 * sine(sweep(160, 60, 0.12), 0.12) * exp_env(len(t(0.12)), 30), 0.55)  # clunk
    place(out, 0.7 * bell(hz("C", 6), 0.7, k=5), 0.68)
    place(out, 0.6 * bell(hz("G", 6), 0.7, k=5), 0.8)
    return out


def upgrade_offer():
    out = silence(1.0)
    for i in range(10):
        f = hz("C", 6) * 2 ** (rng.integers(0, 12) / 12)
        place(out, 0.35 * bell(f, 0.4, k=10), i * 0.06)
    return out


def upgrade_ok():
    out = silence(0.8)
    for i, n in enumerate([("C", 5), ("E", 5), ("G", 5), ("C", 6)]):
        place(out, 0.6 * marimba(hz(*n), 0.35), i * 0.08)
    return out


def healed():
    out = silence(1.1)
    for i, n in enumerate([("A", 5), ("C#", 6), ("E", 6), ("A", 6)]):
        place(out, 0.5 * bell(hz(*n), 0.8, k=4, idx=0.8), i * 0.1)
    shimmer = sine(sweep(1500, 3000, 0.9), 0.9) * env(len(t(0.9)), a=0.2, d=0.6, r=0.1) * 0.15
    place(out, shimmer, 0.1)
    return out


def mask_used():
    d = 0.9
    breath = bandpass(noise(d), 300, 1800) * env(len(t(d)), a=0.25, d=0.0, s=1.0, r=0.35, hold=0.3)
    x = breath * (0.7 + 0.3 * np.sin(2 * np.pi * 2.2 * t(d)))
    out = silence(1.2)
    place(out, x, 0.0)
    place(out, 0.4 * bell(hz("E", 6), 0.4, k=8), 0.85)
    return out


def infected():
    d = 1.0
    f = sweep(520, 160, d) * (1 + 0.06 * np.sin(2 * np.pi * 7 * t(d)))
    x = tri(f, d) * env(len(t(d)), a=0.02, d=0.8, r=0.15) * 0.6
    gurgle = bandpass(noise(d), 150, 700) * (0.5 + 0.5 * np.sin(2 * np.pi * 13 * t(d)))
    x += 0.4 * gurgle * env(len(t(d)), a=0.1, d=0.7, r=0.15)
    return x


def heartbeat():
    out = silence(0.6)
    for at in (0.0, 0.18):
        place(out, sine(sweep(90, 50, 0.12), 0.12) * exp_env(len(t(0.12)), 25), at)
    return out


def repollute():
    out = silence(0.7)
    squelch = lowpass(noise(0.35), 900) * env(len(t(0.35)), a=0.01, d=0.3, r=0.04)
    squelch *= 0.6 + 0.4 * np.sin(2 * np.pi * sweep(30, 8, 0.35, "lin") * t(0.35))
    place(out, squelch, 0.0)
    place(out, 0.5 * bandpass(noise(0.25), 2000, 6000) * exp_env(len(t(0.25)), 20), 0.08)  # crumple
    place(out, 0.4 * sine(sweep(220, 110, 0.3), 0.3) * env(len(t(0.3)), a=0.01, d=0.25, r=0.04), 0.15)
    return out


def sabotage_machine():
    out = silence(1.2)
    for at in (0.0, 0.16, 0.3):
        clank = bell(rng.uniform(300, 480), 0.25, k=18, ratio=1.41, idx=3)
        place(out, 0.6 * clank, at)
    grind = sine(sweep(140, 70, 0.8), 0.8) * (0.5 + 0.5 * np.sign(np.sin(2 * np.pi * 24 * t(0.8))))
    grind += 0.5 * bandpass(noise(0.8), 200, 1500)
    place(out, 0.45 * grind * env(len(t(0.8)), a=0.05, d=0.6, r=0.15), 0.35)
    return out


def smog():
    d = 1.6
    rumble = lowpass(noise(d), 260) * env(len(t(d)), a=0.4, d=0.8, r=0.4) * 1.4
    hiss = bandpass(noise(d), 1500, 5000) * env(len(t(d)), a=0.6, d=0.6, r=0.4) * 0.25
    drone = sine(sweep(110, 82, d), d) * env(len(t(d)), a=0.4, d=0.8, r=0.4) * 0.5
    return rumble + hiss + drone


def sabotage_fixed():
    out = silence(0.9)
    place(out, 0.5 * sine(sweep(80, 200, 0.3), 0.3) * env(len(t(0.3)), a=0.02, d=0.25, r=0.03), 0.0)
    for i, n in enumerate([("G", 5), ("C", 6), ("E", 6)]):
        place(out, 0.55 * bell(hz(*n), 0.5, k=6), 0.2 + i * 0.08)
    return out


def ability_unlocked():
    out = silence(1.3)
    for i, n in enumerate([("C", 3), ("D#", 3), ("F#", 3), ("A", 3)]):
        f = hz(*n) * (1 + 0.01 * np.sin(2 * np.pi * 6 * t(0.5)))
        note = tri(f, 0.5) * env(len(t(0.5)), a=0.01, d=0.4, r=0.08)
        place(out, 0.5 * lowpass(note, 1800), i * 0.14)
    place(out, 0.3 * bandpass(noise(0.6), 200, 1200) * env(len(t(0.6)), a=0.2, d=0.3, r=0.1), 0.5)
    return out


def virus_planted():
    out = silence(0.5)
    for i in range(3):
        f = sweep(300 + i * 60, 900 + i * 100, 0.08)
        place(out, 0.5 * sine(f, 0.08) * env(len(t(0.08)), a=0.002, d=0.07, r=0.01), i * 0.09)
    place(out, 0.3 * lowpass(noise(0.2), 600) * exp_env(len(t(0.2)), 15), 0.25)
    return out


def gas_warning():
    out = silence(1.5)
    for i in range(4):
        beep = sine(880 if i % 2 == 0 else 660, 0.16) * env(len(t(0.16)), a=0.005, d=0.0, s=1.0, r=0.02, hold=0.12)
        place(out, 0.45 * beep, i * 0.25)
    place(out, 0.4 * bandpass(noise(0.9), 1200, 6000) * env(len(t(0.9)), a=0.3, d=0.4, r=0.2), 0.55)
    return out


def gas_done():
    d = 1.0
    return bandpass(noise(d), 800, 5000) * env(len(t(d)), a=0.02, d=0.8, r=0.15) * 0.8


def meeting_started():
    out = silence(1.4)
    for i in range(3):
        place(out, 0.7 * bell(hz("A", 5), 0.45, k=7, ratio=3.01, idx=2.5), i * 0.3)
        place(out, 0.4 * bell(hz("E", 6), 0.45, k=7, ratio=3.01, idx=2.5), i * 0.3 + 0.02)
    return out


def voting_started():
    out = silence(0.6)
    for at in (0.0, 0.22):
        knock = sine(sweep(260, 140, 0.1), 0.1) * exp_env(len(t(0.1)), 40)
        knock += 0.6 * bandpass(noise(0.1), 600, 2500) * exp_env(len(t(0.1)), 60)
        place(out, knock, at)
    return out


def vote_cast():
    d = 0.14
    x = sine(sweep(400, 200, d), d) * exp_env(len(t(d)), 30)
    x += 0.5 * bandpass(noise(d), 1000, 4000) * exp_env(len(t(d)), 80)
    return x


def chat():
    out = silence(0.18)
    place(out, 0.6 * sine(1046, 0.06) * env(len(t(0.06)), a=0.002, d=0.05, r=0.01), 0.0)
    place(out, 0.6 * sine(1318, 0.06) * env(len(t(0.06)), a=0.002, d=0.05, r=0.01), 0.07)
    return out


def ejection_trasher():  # the Trasher is purified: bright sweep of sparkles
    out = silence(1.6)
    place(out, 0.4 * bandpass(noise(1.0), 2000, 8000) * env(len(t(1.0)), a=0.4, d=0.5, r=0.1), 0.0)
    for i, n in enumerate([("C", 6), ("E", 6), ("G", 6), ("C", 7), ("E", 7)]):
        place(out, 0.5 * bell(hz(*n), 0.7, k=5), 0.25 + i * 0.09)
    return out


def ejection_innocent():  # wrong vote: sad wilting slide
    d = 1.3
    f = sweep(hz("G", 4), hz("D", 4), d) * (1 + 0.015 * np.sin(2 * np.pi * 5 * t(d)))
    x = lowpass(tri(f, d), 1500) * env(len(t(d)), a=0.05, d=1.0, r=0.2)
    return x


def you_are_out():
    out = silence(1.5)
    d = 1.2
    ghost = sine(sweep(700, 300, d) * (1 + 0.03 * np.sin(2 * np.pi * 6 * t(d))), d)
    place(out, 0.5 * ghost * env(len(t(d)), a=0.1, d=0.9, r=0.2), 0.0)
    place(out, 0.3 * bandpass(noise(1.2), 500, 3000) * env(len(t(1.2)), a=0.3, d=0.7, r=0.2), 0.1)
    return out


def role_cleaner():
    out = silence(1.4)
    for i, n in enumerate([("C", 5), ("E", 5), ("G", 5)]):
        place(out, 0.5 * marimba(hz(*n), 0.4), i * 0.1)
    for n in [("C", 5), ("E", 5), ("G", 5), ("C", 6)]:
        place(out, 0.35 * marimba(hz(*n), 0.9), 0.35)
    place(out, 0.35 * bell(hz("C", 7), 0.8, k=4), 0.35)
    return out


def role_trasher():
    out = silence(1.5)
    for i, n in enumerate([("E", 3), ("F", 3), ("E", 3), ("A#", 2)]):
        note = lowpass(tri(hz(*n), 0.35), 1200) * env(len(t(0.35)), a=0.01, d=0.3, r=0.04)
        place(out, 0.6 * note, i * 0.22)
    place(out, 0.4 * pluck(hz("A#", 2), 0.8), 0.66)
    return out


def clock_tick():
    d = 0.06
    return bandpass(noise(d), 2000, 7000) * exp_env(len(t(d)), 90) + 0.4 * sine(1800, d) * exp_env(len(t(d)), 120)


def meter_milestone():
    out = silence(0.7)
    for i, n in enumerate([("G", 5), ("C", 6)]):
        place(out, 0.5 * bell(hz(*n), 0.5, k=6), i * 0.1)
    return out


def action_rejected():
    d = 0.22
    x = tri(np.full(len(t(d)), 160.0), d) * env(len(t(d)), a=0.005, d=0.18, r=0.03)
    return lowpass(x, 1200)


def victory():
    out = silence(2.6)
    melody = [("C", 5), ("E", 5), ("G", 5), ("C", 6), ("G", 5), ("C", 6), ("E", 6)]
    times = [0.0, 0.15, 0.3, 0.45, 0.75, 0.9, 1.05]
    for n, at in zip(melody, times):
        place(out, 0.5 * marimba(hz(*n), 0.5), at)
    for n in [("C", 4), ("E", 4), ("G", 4), ("C", 5)]:
        place(out, 0.3 * pluck(hz(*n), 1.4), 1.05)
    place(out, 0.35 * bell(hz("E", 7), 1.2, k=3), 1.05)
    return out


def defeat():
    out = silence(2.6)
    for i, n in enumerate([("G", 4), ("F#", 4), ("F", 4), ("E", 4)]):
        d = 0.5 if i < 3 else 1.2
        f = hz(*n) * (1 + (0.02 * np.sin(2 * np.pi * 5 * t(d)) if i == 3 else 0))
        note = lowpass(tri(f, d), 1400) * env(len(t(d)), a=0.02, d=d - 0.1, r=0.08)
        place(out, 0.55 * note, i * 0.45)
    return out


SFX = {
    "ui_click": ui_click, "ui_open": ui_open, "ui_close": ui_close,
    "step_1": lambda: step(1), "step_2": lambda: step(2), "dash": dash,
    "spot_cleaned": spot_cleaned, "deliver": deliver,
    "upgrade_offer": upgrade_offer, "upgrade_ok": upgrade_ok,
    "healed": healed, "mask_used": mask_used, "infected": infected, "heartbeat": heartbeat,
    "repollute": repollute, "sabotage_machine": sabotage_machine, "smog": smog,
    "sabotage_fixed": sabotage_fixed, "ability_unlocked": ability_unlocked,
    "virus_planted": virus_planted, "gas_warning": gas_warning, "gas_done": gas_done,
    "meeting_started": meeting_started, "voting_started": voting_started, "vote_cast": vote_cast,
    "chat": chat, "ejection_trasher": ejection_trasher, "ejection_innocent": ejection_innocent,
    "you_are_out": you_are_out, "role_cleaner": role_cleaner, "role_trasher": role_trasher,
    "clock_tick": clock_tick, "meter_milestone": meter_milestone,
    "action_rejected": action_rejected, "victory": victory, "defeat": defeat,
}


# ---------- music loops ----------
def music_loop(bpm, chords, melody, lead, bass_oct=2, pad=True, swing=0.0):
    beat = 60.0 / bpm
    bars = len(chords)
    total = bars * 4 * beat
    out = silence(total + 1.5)
    for b, chord in enumerate(chords):
        bar_t = b * 4 * beat
        root = chord[0]
        for k in range(4):  # bass on every beat, root / fifth
            n = root if k % 2 == 0 else chord[2]
            place(out, 0.35 * lowpass(pluck(hz(n[0], bass_oct), beat * 0.9), 900), bar_t + k * beat)
        if pad:
            for n in chord:
                p = tri(hz(n[0], n[1]), 4 * beat) * env(len(t(4 * beat)), a=0.3, d=0.0, s=1.0, r=0.4, hold=4 * beat - 0.7)
                place(out, 0.06 * lowpass(p, 1500), bar_t)
        for k in range(8):  # soft offbeat shaker
            sh = bandpass(noise(0.05), 4000, 9000) * exp_env(len(t(0.05)), 80)
            place(out, 0.06 * sh, bar_t + k * beat / 2 + (swing * beat / 2 if k % 2 else 0))
    step_t = beat / 2
    for i, n in enumerate(melody):
        if n is None:
            continue
        at = i * step_t + (swing * step_t if i % 2 else 0)
        place(out, 0.3 * lead(hz(*n), step_t * 1.8), at)
    # loop seamlessly: fold the tail back onto the start
    n_total = int(total * SR)
    tail = out[n_total:]
    loop = out[:n_total].copy()
    loop[: len(tail)] += tail
    return loop


def music_menu():
    C, Am, F, G = [("C", 4), ("E", 4), ("G", 4)], [("A", 3), ("C", 4), ("E", 4)], [("F", 3), ("A", 3), ("C", 4)], [("G", 3), ("B", 3), ("D", 4)]
    chords = [C, Am, F, G, C, Am, F, G]
    mel = [("E", 5), None, ("G", 5), ("E", 5), ("D", 5), None, ("C", 5), None,
           ("C", 5), None, ("E", 5), ("A", 5), ("G", 5), None, None, None,
           ("A", 5), None, ("G", 5), ("F", 5), ("E", 5), None, ("C", 5), None,
           ("D", 5), None, ("E", 5), ("D", 5), ("B", 4), None, None, None]
    return music_loop(84, chords, mel * 2, marimba, swing=0.15)


def music_match():
    Am, F, C, G = [("A", 3), ("C", 4), ("E", 4)], [("F", 3), ("A", 3), ("C", 4)], [("C", 4), ("E", 4), ("G", 4)], [("G", 3), ("B", 3), ("D", 4)]
    chords = [Am, F, C, G, Am, F, G, G]
    mel = [("A", 4), ("C", 5), ("E", 5), ("C", 5), ("A", 4), ("C", 5), ("E", 5), ("G", 5),
           ("F", 5), ("E", 5), ("C", 5), ("A", 4), ("F", 4), ("A", 4), ("C", 5), None,
           ("E", 5), ("G", 5), ("E", 5), ("C", 5), ("G", 4), ("C", 5), ("E", 5), None,
           ("D", 5), ("B", 4), ("G", 4), ("B", 4), ("D", 5), None, ("B", 4), None]
    return music_loop(112, chords, mel * 2, lambda f, d: pluck(f, d), pad=True)


def write_music(name, x):
    wav = os.path.join(MUSIC_DIR, name + ".wav")
    write_wav(wav, lowpass(x, 7000), peak=0.6)
    if shutil.which("ffmpeg"):
        ogg = os.path.join(MUSIC_DIR, name + ".ogg")
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", wav, "-c:a", "libvorbis", "-q:a", "4", ogg], check=True)
        os.remove(wav)


def main():
    os.makedirs(SFX_DIR, exist_ok=True)
    os.makedirs(MUSIC_DIR, exist_ok=True)
    for name, fn in SFX.items():
        write_wav(os.path.join(SFX_DIR, name + ".wav"), fn())
    write_music("music_menu", music_menu())
    write_music("music_match", music_match())
    print("wrote", len(SFX), "effects and 2 music loops to", os.path.normpath(ROOT))


if __name__ == "__main__":
    main()
