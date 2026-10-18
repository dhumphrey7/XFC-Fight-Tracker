#!/usr/bin/env python3
"""
Generate the repository icon as a PNG with no external dependencies.

Dalamud's installer fetches IconUrl and renders whatever PNG is there, so the
icon has to be a real file at a stable raw URL. This writes one with a small
hand-rolled PNG encoder (zlib + struct from the standard library) so the build
does not depend on Pillow or ImageMagick being installed.

Usage:
    python3 scripts/make-icon.py --out images/icon.png
"""

from __future__ import annotations

import argparse
import struct
import zlib

# X-Fight Club palette: the site uses a red accent on near-black.
BG = (27, 29, 30)
ACCENT = (204, 12, 0)
ACCENT_DIM = (120, 8, 0)
RING = (39, 39, 39)

SUPERSAMPLE = 3  # render NxN and box-downsample for antialiasing


def write_png(path: str, width: int, height: int, pixels: list[list[tuple[int, int, int]]]) -> None:
    """Write an RGB PNG. `pixels` is row-major, top-left origin."""
    raw = bytearray()
    for row in pixels:
        raw.append(0)  # filter type 0 (None) per scanline
        for r, g, b in row:
            raw += bytes((r, g, b))

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    header = struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0)  # 8-bit RGB

    with open(path, "wb") as fh:
        fh.write(b"\x89PNG\r\n\x1a\n")
        fh.write(chunk(b"IHDR", header))
        fh.write(chunk(b"IDAT", zlib.compress(bytes(raw), 9)))
        fh.write(chunk(b"IEND", b""))


def blend(base: tuple[int, int, int], top: tuple[int, int, int], alpha: float):
    return tuple(int(round(b + (t - b) * alpha)) for b, t in zip(base, top))


def make_icon(size: int) -> list[list[tuple[int, int, int]]]:
    """
    A bold 'X' in the XFC red on a dark disc, inside a ring - readable at the
    small sizes the plugin installer renders.
    """
    hi = size * SUPERSAMPLE
    c = hi / 2.0
    radius = hi * 0.47
    ring_inner = hi * 0.435

    # Thick diagonal strokes forming the X.
    stroke = hi * 0.075
    arm = hi * 0.40  # half-length of each diagonal

    canvas = [[(0, 0, 0)] * hi for _ in range(hi)]

    for y in range(hi):
        for x in range(hi):
            dx, dy = x - c, y - c
            dist = (dx * dx + dy * dy) ** 0.5

            if dist > radius:
                canvas[y][x] = (0, 0, 0)  # outside the disc: transparent-ish black
                continue

            px = BG

            # Outer ring.
            if dist > ring_inner:
                px = RING

            # The two diagonals of the X.
            # Distance from the point to each infinite diagonal line |y-c| = |x-c|.
            d1 = abs(dy - dx) / (2 ** 0.5)
            d2 = abs(dy + dx) / (2 ** 0.5)
            inside = (dist < radius - hi * 0.02)

            if inside:
                if d1 < stroke and abs(dx) < arm and abs(dy) < arm:
                    px = ACCENT
                elif d2 < stroke and abs(dx) < arm and abs(dy) < arm:
                    px = ACCENT_DIM

            canvas[y][x] = px

    # Box-downsample to the target size for antialiasing.
    out: list[list[tuple[int, int, int]]] = []
    for y in range(size):
        row = []
        for x in range(size):
            r = g = b = 0
            for sy in range(SUPERSAMPLE):
                for sx in range(SUPERSAMPLE):
                    pr, pg, pb = canvas[y * SUPERSAMPLE + sy][x * SUPERSAMPLE + sx]
                    r += pr
                    g += pg
                    b += pb
            n = SUPERSAMPLE * SUPERSAMPLE
            row.append((r // n, g // n, b // n))
        out.append(row)

    return out


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--out", default="images/icon.png")
    parser.add_argument("--size", type=int, default=512,
                        help="output size in pixels (square)")
    args = parser.parse_args()

    import os
    os.makedirs(os.path.dirname(args.out) or ".", exist_ok=True)

    pixels = make_icon(args.size)
    write_png(args.out, args.size, args.size, pixels)

    size_bytes = os.path.getsize(args.out)
    print(f"Wrote {args.out} ({args.size}x{args.size}, {size_bytes} bytes)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
