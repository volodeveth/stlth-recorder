#!/usr/bin/env python3
"""Генерує іконки трею без жодних сторонніх пакетів.

Дві іконки, і різниця між ними навмисно груба: у спокої — сірий мікрофон, під час
запису — суцільне **червоне** коло. Не бурштинове й не помаранчеве: на an earlier build
бурштинова іконка зливалася з системним індикатором мікрофона, і стан запису було
просто не видно. Стан, який не помітно, — це стан, якого немає.

Форми задаються знаковою відстанню в дизайнерській сітці 32×32, а згладжування
рахується вже в пікселях — інакше край, розмитий на 32 px, на 256 px перетворюється
на восьмипіксельну кашу.

Використання:
    python tools/make_icons.py src/Stlth.App/Resources
"""

from __future__ import annotations

import struct
import sys
import zlib
from pathlib import Path

SIZES = (16, 20, 24, 32, 40, 48, 64, 256)

IDLE = (0x9A, 0x9A, 0x9E)
RECORDING = (0xE0, 0x28, 0x28)

GRID = 32.0


def png(width: int, height: int, pixels: list[list[tuple[int, int, int, int]]]) -> bytes:
    raw = b"".join(
        b"\x00" + b"".join(struct.pack("BBBB", *pixel) for pixel in row)
        for row in pixels
    )

    def chunk(tag: bytes, payload: bytes) -> bytes:
        return (
            struct.pack(">I", len(payload))
            + tag
            + payload
            + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)
        )

    return (
        b"\x89PNG\r\n\x1a\n"
        + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0))
        + chunk(b"IDAT", zlib.compress(raw, 9))
        + chunk(b"IEND", b"")
    )


def disc(x: float, y: float, cx: float, cy: float, radius: float) -> float:
    """Знакова відстань до кола: додатна всередині."""
    return radius - (((x - cx) ** 2 + (y - cy) ** 2) ** 0.5)


def capsule(x: float, y: float, cx: float, top: float, bottom: float, half: float) -> float:
    """Капсула — тіло мікрофона."""
    if y < top:
        return disc(x, y, cx, top, half)
    if y > bottom:
        return disc(x, y, cx, bottom, half)
    return half - abs(x - cx)


def ring(x: float, y: float, cx: float, cy: float, radius: float, half: float) -> float:
    """Дуга-підставка: кільце завтовшки 2·half."""
    return half - abs((((x - cx) ** 2 + (y - cy) ** 2) ** 0.5) - radius)


def bar(x: float, y: float, cx: float, top: float, bottom: float, half: float) -> float:
    """Ніжка мікрофона."""
    if y < top or y > bottom:
        return -1.0
    return half - abs(x - cx)


def render(size: int, shape, colour: tuple[int, int, int]) -> list[list[tuple[int, int, int, int]]]:
    scale = size / GRID
    rows = []
    for py in range(size):
        row = []
        for px in range(size):
            x, y = (px + 0.5) / scale, (py + 0.5) / scale
            # Знакова відстань у дизайнерських одиницях → альфа в пікселях.
            # Саме множення на scale тримає край завтовшки рівно один піксель.
            alpha = max(0.0, min(1.0, (shape(x, y) * scale) + 0.5))
            row.append((*colour, int(round(alpha * 255))))
        rows.append(row)
    return rows


def microphone(x: float, y: float) -> float:
    body = capsule(x, y, 16, 9.5, 15.5, 4.4)
    stand = ring(x, y, 16, 13.5, 8.0, 1.1) if y > 13.5 else -1.0
    stem = bar(x, y, 16, 21.5, 25.5, 1.1)
    return max(body, stand, stem)


def dot(x: float, y: float) -> float:
    return disc(x, y, 16, 16, 11)


def ico(images: list[tuple[int, bytes]]) -> bytes:
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries, payload = b"", b""

    for size, data in images:
        entries += struct.pack(
            "<BBBBHHII",
            0 if size >= 256 else size,
            0 if size >= 256 else size,
            0, 0, 1, 32, len(data), offset,
        )
        payload += data
        offset += len(data)

    return header + entries + payload


def build(target: Path, name: str, shape, colour: tuple[int, int, int]) -> None:
    images = [(size, png(size, size, render(size, shape, colour))) for size in SIZES]
    path = target / f"{name}.ico"
    path.write_bytes(ico(images))
    print(f"{path}: {len(SIZES)} розмірів, {path.stat().st_size:,} Б")


def main() -> None:
    target = Path(sys.argv[1] if len(sys.argv) > 1 else "src/Stlth.App/Resources")
    target.mkdir(parents=True, exist_ok=True)
    build(target, "idle", microphone, IDLE)
    build(target, "recording", dot, RECORDING)


if __name__ == "__main__":
    main()
