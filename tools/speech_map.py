#!/usr/bin/env python3
"""Показує, де в доріжці є мова, а де тиша — по секундах.

Потрібен для питання, на яке транскрипт сам не відповідає: чи модель пропустила
частину розмови, чи там справді нічого не було. Порожній транскрипт після
тридцятої секунди означає різні речі залежно від того, чи говорив хтось на
сороковій.

Використання:
    python tools/speech_map.py <файл.wav> [поріг_dBFS]
"""

from __future__ import annotations

import struct
import sys
import wave
from pathlib import Path


def main() -> None:
    if len(sys.argv) < 2:
        print(__doc__)
        raise SystemExit(1)

    path = Path(sys.argv[1])
    threshold_db = float(sys.argv[2]) if len(sys.argv) > 2 else -45.0
    threshold = 10 ** (threshold_db / 20)

    with wave.open(str(path), "rb") as handle:
        channels = handle.getnchannels()
        rate = handle.getframerate()
        frames = handle.getnframes()
        raw = handle.readframes(frames)

    count = len(raw) // 2
    samples = struct.unpack(f"<{count}h", raw[: count * 2])

    window = rate * channels  # одна секунда
    seconds = len(samples) // window

    print(f"{path.name}: {frames / rate:.1f} с, поріг {threshold_db:g} dBFS")
    print()

    speaking = []
    for index in range(seconds):
        chunk = samples[index * window : (index + 1) * window]
        peak = max(abs(value) for value in chunk) / 32768 if chunk else 0.0
        energy = (sum(v * v for v in chunk) / len(chunk)) ** 0.5 / 32768 if chunk else 0.0

        loud = peak >= threshold
        if loud:
            speaking.append(index)

        bar = "#" * min(40, int(energy * 400))
        print(f"{index:4d} с  пік {peak:6.3f}  {bar}")

    print()
    if not speaking:
        print("мовлення не знайдено взагалі")
        return

    # Злиті проміжки, щоб було видно, де саме людина говорила.
    spans = []
    start = previous = speaking[0]
    for index in speaking[1:]:
        if index - previous > 2:
            spans.append((start, previous))
            start = index
        previous = index
    spans.append((start, previous))

    print("Проміжки з мовленням:")
    for begin, end in spans:
        print(f"  {begin:3d}–{end:3d} с")
    print(f"\nВсього секунд із сигналом: {len(speaking)} з {seconds}")


if __name__ == "__main__":
    main()
