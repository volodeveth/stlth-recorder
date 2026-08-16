#!/usr/bin/env python3
"""Генератор тест-сигналу для вимірювання розсинхрону каналів.

Кліки — короткі різкі імпульси через рівні проміжки. Вони потрібні саме такими:
крос-кореляція шукає момент удару, а не форму звуку, тож чим коротший фронт, тим
точніше вимірювання. Між кліками — тиша, щоб сусідні не змазували один одного.

Використання:
    python tools/gen_clicks.py --out clicks.wav --duration 3600 --interval 5
    python tools/gen_clicks.py --out tone.wav --duration 30 --tone 440
"""

from __future__ import annotations

import argparse
import math
import struct
import wave

SAMPLE_RATE = 48_000


def write_wav(path: str, samples: list[int], channels: int = 1) -> None:
    with wave.open(path, "wb") as handle:
        handle.setnchannels(channels)
        handle.setsampwidth(2)
        handle.setframerate(SAMPLE_RATE)
        frames = b"".join(struct.pack("<h", value) for value in samples)
        handle.writeframes(frames * channels if channels > 1 else frames)


def clicks(duration: float, interval: float, click_ms: float, amplitude: float) -> list[int]:
    total = int(duration * SAMPLE_RATE)
    samples = [0] * total
    click_len = int(click_ms / 1000 * SAMPLE_RATE)
    peak = int(amplitude * 32767)

    position = 0
    while position < total:
        for i in range(min(click_len, total - position)):
            # Згасаюча синусоїда 1 кГц: різкий фронт і швидкий спад дають чіткий
            # максимум кореляції.
            envelope = 1.0 - (i / click_len)
            value = math.sin(2 * math.pi * 1000 * i / SAMPLE_RATE) * envelope * envelope
            samples[position + i] = int(value * peak)
        position += int(interval * SAMPLE_RATE)

    return samples


def tone(duration: float, frequency: float, amplitude: float) -> list[int]:
    total = int(duration * SAMPLE_RATE)
    peak = int(amplitude * 32767)
    return [
        int(math.sin(2 * math.pi * frequency * i / SAMPLE_RATE) * peak)
        for i in range(total)
    ]


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True)
    parser.add_argument("--duration", type=float, default=60.0, help="секунд")
    parser.add_argument("--interval", type=float, default=5.0, help="секунд між кліками")
    parser.add_argument("--click-ms", type=float, default=5.0)
    parser.add_argument("--amplitude", type=float, default=0.7)
    parser.add_argument("--tone", type=float, default=None,
                        help="замість кліків — рівний тон вказаної частоти")
    parser.add_argument("--channels", type=int, default=1)
    args = parser.parse_args()

    if args.tone is not None:
        samples = tone(args.duration, args.tone, args.amplitude)
        kind = f"тон {args.tone:.0f} Гц"
    else:
        samples = clicks(args.duration, args.interval, args.click_ms, args.amplitude)
        kind = f"кліки кожні {args.interval:g} с"

    write_wav(args.out, samples, args.channels)
    print(f"{args.out}: {kind}, {args.duration:g} с, {SAMPLE_RATE} Гц, {args.channels} кан.")


if __name__ == "__main__":
    main()
