#!/usr/bin/env python3
"""Швидкий погляд усередину доріжки: чи там справді сигнал, а не тиша.

Потрібен рівно для одного питання, яке не можна закрити довжиною файлу: доріжка
може бути правильної тривалості й повністю порожньою. Різницю видно лише за
енергією.

Використання:
    python tools/wav_stats.py <тека_сесії|файл.wav> [...]
"""

from __future__ import annotations

import struct
import sys
import wave
from pathlib import Path


def stats(path: Path) -> str:
    with wave.open(str(path), "rb") as handle:
        channels = handle.getnchannels()
        rate = handle.getframerate()
        frames = handle.getnframes()
        raw = handle.readframes(frames)

    count = len(raw) // 2
    samples = struct.unpack(f"<{count}h", raw[: count * 2])

    if not samples:
        return f"{path.name}: порожній"

    peak = max(abs(value) for value in samples)
    energy = sum(value * value for value in samples) / len(samples)
    rms = energy ** 0.5

    # Скільки часу доріжка справді мовчала: вікна по 100 мс нижче -60 dBFS.
    window = rate // 10 * channels
    silent_windows = 0
    total_windows = 0
    for start in range(0, len(samples) - window, window):
        chunk = samples[start : start + window]
        total_windows += 1
        if max(abs(value) for value in chunk) < 33:  # ≈ -60 dBFS
            silent_windows += 1

    silence = silent_windows / total_windows * 100 if total_windows else 0

    return (
        f"{path.name}: {frames:,} кадрів ({frames / rate:.3f} с), {channels} кан., "
        f"пік {peak / 32768:.3f}, RMS {rms / 32768:.4f}, тиші {silence:.0f}%"
    )


def main() -> None:
    if len(sys.argv) < 2:
        print(__doc__)
        raise SystemExit(1)

    for argument in sys.argv[1:]:
        target = Path(argument)
        files = sorted(target.glob("*.wav")) if target.is_dir() else [target]
        for path in files:
            print(stats(path))


if __name__ == "__main__":
    main()
