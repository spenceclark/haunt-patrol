"""Find every game sound in a recording, so each take gets an event table without hunting through frames.

    python events.py ../C1.mkv [more files...]

The game plays the WAVs in HauntPatrol/Assets/Audio unaltered, so each one is matched against the take's audio
by normalised cross-correlation. Prints one line per hit: time, sound, match strength.
"""
import subprocess
import sys
from pathlib import Path

import numpy as np

SR = 16000
SOUNDS = Path(__file__).resolve().parents[2] / "HauntPatrol" / "Assets" / "Audio"
THRESHOLD = 0.4


def load(path):
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", str(path), "-vn", "-ac", "1", "-ar", str(SR),
                          "-f", "s16le", "-"], capture_output=True, check=True).stdout
    return np.frombuffer(raw, np.int16).astype(np.float64) / 32768


def ncc(x, t):
    """Normalised cross-correlation of template t at every offset of x."""
    m = len(t)
    t = t - t.mean()
    n = 1 << int(np.ceil(np.log2(len(x) + m)))
    num = np.fft.irfft(np.fft.rfft(x, n) * np.conj(np.fft.rfft(t, n)), n)[: len(x) - m + 1]
    c = np.concatenate([[0], np.cumsum(x)])
    c2 = np.concatenate([[0], np.cumsum(x * x)])
    s, s2 = c[m:] - c[:-m], c2[m:] - c2[:-m]
    var = np.maximum(s2 - s * s / m, 1e-12)
    return num / (np.sqrt(var) * np.linalg.norm(t))


def peaks(r, min_gap):
    """Local maxima above THRESHOLD, at least min_gap samples apart, strongest first."""
    idx = np.flatnonzero(r > THRESHOLD)
    found = []
    for i in idx[np.argsort(-r[idx])]:
        if all(abs(i - j) >= min_gap for j in found):
            found.append(i)
    return sorted(found)


def table(path):
    x = load(path)
    hits = []
    for wav in sorted(SOUNDS.glob("*.wav")):
        t = load(wav)
        if len(t) > len(x):
            continue
        r = ncc(x, t)
        hits += [(i / SR, wav.stem, r[i]) for i in peaks(r, len(t) // 2)]
    return sorted(hits)


if __name__ == "__main__":
    for f in sys.argv[1:]:
        print(f"== {f}")
        for t, name, score in table(f):
            print(f"{t:8.3f}  {name:<10} {score:.2f}")
