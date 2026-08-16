#!/usr/bin/env python3
"""Перевіряє сам вимірювач дрейфу на синтетичному аудіо з відомими зсувами.

Числам у звіті можна довіряти лише після того, як перевірено інструмент, який їх
дає. Тут будуються пари доріжок із наперед відомим зсувом, і вимірювач має його
відтворити; заодно друкується його власна точність.

Використання:
    python tools/selftest_drift.py
"""

from __future__ import annotations

import math
import struct
import sys
import tempfile
import wave
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))

from drift_check import SAMPLE_RATE, find_clicks, read_mono, regression  # noqa: E402


def write(path: Path, samples: list[int], channels: int) -> None:
    with wave.open(str(path), "wb") as handle:
        handle.setnchannels(channels)
        handle.setsampwidth(2)
        handle.setframerate(SAMPLE_RATE)
        if channels == 1:
            frames = b"".join(struct.pack("<h", value) for value in samples)
        else:
            frames = b"".join(struct.pack("<h", value) * channels for value in samples)
        handle.writeframes(frames)


def clicks_track(duration: float, interval: float, offset_ms_per_hour: float) -> list[int]:
    """Кліки, що поступово розходяться з еталоном на заданий дрейф."""
    total = int(duration * SAMPLE_RATE)
    samples = [0] * total
    click_len = int(0.005 * SAMPLE_RATE)

    position = 0.0
    while position < duration:
        # Дрейф накопичується пропорційно часу — саме те, що має побачити регресія.
        shift = position * (offset_ms_per_hour / 1000.0) / 3600.0
        start = int((position + shift) * SAMPLE_RATE)

        for i in range(click_len):
            if start + i >= total:
                break
            envelope = 1.0 - (i / click_len)
            value = math.sin(2 * math.pi * 1000 * i / SAMPLE_RATE) * envelope * envelope
            samples[start + i] = int(value * 22000)

        position += interval

    return samples


def measure(directory: Path, duration: float, drift_ms_per_hour: float) -> tuple[float, float]:
    mic = clicks_track(duration, 5.0, drift_ms_per_hour)
    system = clicks_track(duration, 5.0, 0.0)

    write(directory / "mic.wav", mic, 1)
    write(directory / "system.wav", system, 2)

    mic_clicks = find_clicks(read_mono(directory / "mic.wav"))
    system_clicks = find_clicks(read_mono(directory / "system.wav"))
    pairs = min(len(mic_clicks), len(system_clicks))

    points = [
        (mic_clicks[i] / SAMPLE_RATE, (mic_clicks[i] - system_clicks[i]) / SAMPLE_RATE)
        for i in range(pairs)
    ]

    slope, half_width = regression(points)
    return slope * 1000, half_width * 1000


def main() -> None:
    cases = [0.0, 150.0, 400.0, -250.0]
    duration = 600.0

    print(f"Синтетична база {duration / 60:.0f} хв, кліки кожні 5 с.\n")
    print(f"{'задано':>10} {'виміряно':>12} {'похибка':>10} {'95% ДІ':>10}")
    print("-" * 46)

    worst = 0.0
    with tempfile.TemporaryDirectory() as temporary:
        directory = Path(temporary)
        for expected in cases:
            measured, half_width = measure(directory, duration, expected)
            error = abs(measured - expected)
            worst = max(worst, error)
            print(f"{expected:>10.1f} {measured:>12.1f} {error:>10.1f} {half_width:>10.1f}")

    print()
    print(f"Найбільша похибка інструмента: {worst:.1f} мс/год при порозі 300.")

    if worst > 30:
        print("→ вимірювач НЕ придатний, числам зі звіту вірити не можна")
        raise SystemExit(1)

    print("→ вимірювач придатний")


if __name__ == "__main__":
    main()
