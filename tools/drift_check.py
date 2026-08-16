#!/usr/bin/env python3
"""Міряє розсинхрон каналів сесії за клік-треком.

У обидва тракти йде однаковий тест-сигнал із кліками через рівні проміжки, кліки
знаходяться в кожному каналі, пари зіставляються, і через зсуви проводиться пряма.
Нахил прямої — це дрейф.

**Чому пряма, а не «перший клік проти останнього».** Оцінка по двох точках не є
вимірюванням: на 70-секундному прогоні та сама регресія давала −14.5 ± 117 мс/год —
формально «пройдено», фактично не доведено нічого. Тому тут завжди друкується довірчий
інтервал, і якщо він ширший за сам ефект, це видно одразу.

**Що потрібно для роботи.** Клік-трек має потрапити в обидва канали. Системний канал
отримує його прямо (грайте файл на пристрої відтворення), мікрофонний — через
акустичний зв'язок або віртуальний кабель на дев-стенді. Якщо мікрофонного тракту
замкнути нічим, темп годинників можна зміряти без сигналу взагалі:
`stlth-cli drift 3600 30`.

Використання:
    python tools/drift_check.py <тека_сесії>
"""

from __future__ import annotations

import struct
import sys
import wave
from pathlib import Path

SAMPLE_RATE = 48_000

# Клік має різкий фронт; поріг у частках від піку каналу, а не в абсолютних одиницях,
# бо рівні двох трактів відрізняються на порядки.
THRESHOLD = 0.35

# Два піки ближче за це — один клік, а не два.
MIN_GAP_SECONDS = 1.0


def read_mono(path: Path) -> list[float]:
    """Доріжка як моно в діапазоні [-1, 1]."""
    with wave.open(str(path), "rb") as handle:
        channels = handle.getnchannels()
        frames = handle.getnframes()
        raw = handle.readframes(frames)

    count = len(raw) // 2
    samples = struct.unpack(f"<{count}h", raw[: count * 2])

    if channels == 1:
        return [value / 32768.0 for value in samples]

    return [
        sum(samples[i : i + channels]) / channels / 32768.0
        for i in range(0, len(samples) - channels + 1, channels)
    ]


def find_clicks(samples: list[float]) -> list[int]:
    """Позиції кліків у кадрах."""
    peak = max((abs(value) for value in samples), default=0.0)
    if peak <= 0:
        return []

    level = peak * THRESHOLD
    gap = int(MIN_GAP_SECONDS * SAMPLE_RATE)

    clicks: list[int] = []
    index = 0
    while index < len(samples):
        if abs(samples[index]) >= level:
            clicks.append(index)
            index += gap
        else:
            index += 1

    return clicks


def regression(points: list[tuple[float, float]]) -> tuple[float, float]:
    """Нахил у мс/год і півширина 95% довірчого інтервалу."""
    n = len(points)
    if n < 3:
        return 0.0, float("inf")

    mean_x = sum(x for x, _ in points) / n
    mean_y = sum(y for _, y in points) / n
    sxx = sum((x - mean_x) ** 2 for x, _ in points)
    if sxx <= 0:
        return 0.0, float("inf")

    sxy = sum((x - mean_x) * (y - mean_y) for x, y in points)
    slope = sxy / sxx
    intercept = mean_y - slope * mean_x

    residual = sum((y - (intercept + slope * x)) ** 2 for x, y in points)
    standard_error = (residual / (n - 2) / sxx) ** 0.5

    return slope * 3600.0, 1.96 * standard_error * 3600.0


def main() -> None:
    if len(sys.argv) < 2:
        print(__doc__)
        raise SystemExit(1)

    session = Path(sys.argv[1])
    mic_path = session / "mic.wav"
    system_path = session / "system.wav"

    for path in (mic_path, system_path):
        if not path.exists():
            print(f"немає {path}")
            raise SystemExit(1)

    mic = read_mono(mic_path)
    system = read_mono(system_path)

    print(f"mic.wav:    {len(mic):,} кадрів ({len(mic) / SAMPLE_RATE:.3f} с)")
    print(f"system.wav: {len(system):,} кадрів ({len(system) / SAMPLE_RATE:.3f} с)")
    print(f"Різниця довжин: {len(mic) - len(system)} кадрів")
    print()

    mic_clicks = find_clicks(mic)
    system_clicks = find_clicks(system)
    print(f"Кліків знайдено: mic {len(mic_clicks)}, system {len(system_clicks)}")

    if len(mic_clicks) < 3 or len(system_clicks) < 3:
        print()
        print("Замало кліків для регресії. Клік-трек має бути чутний в обох каналах;")
        print("якщо мікрофонний тракт замкнути нічим — міряйте темп годинників:")
        print("    stlth-cli drift 3600 30")
        raise SystemExit(2)

    pairs = min(len(mic_clicks), len(system_clicks))
    points = [
        (mic_clicks[i] / SAMPLE_RATE,
         (mic_clicks[i] - system_clicks[i]) / SAMPLE_RATE)
        for i in range(pairs)
    ]

    offsets_ms = [offset * 1000 for _, offset in points]
    slope, half_width = regression(points)

    print(f"Зсув першої пари:  {offsets_ms[0]:+.1f} мс")
    print(f"Зсув останньої:    {offsets_ms[-1]:+.1f} мс")
    print(f"Розмах зсувів:     {max(offsets_ms) - min(offsets_ms):.1f} мс")
    print()
    print(f"ДРЕЙФ: {slope * 1000:+.1f} мс/год (95% ДІ ±{half_width * 1000:.1f}), пар {pairs}")
    print(f"Поріг вимоги: 300.0 мс/год")

    if abs(slope * 1000) + half_width * 1000 < 300:
        print("→ вимога виконується із запасом")
    else:
        print("→ ВИМОГА ПІД ПИТАННЯМ або база замала — потрібен довший прогін")


if __name__ == "__main__":
    main()
