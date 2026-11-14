"""Read the display back pixel by pixel (command 136, read-only) and save a PNG.

Usage: python screenshot.py out.png [step]   (step 2 = half resolution, faster)
"""

import struct
import sys
import time
import zlib

import usb.core

from lcd import HEIGHT, LCD, PID, VID, WIDTH


def write_png(path: str, w: int, h: int, rows: list[bytes]) -> None:
    def chunk(tag: bytes, data: bytes) -> bytes:
        return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data))
    raw = b"".join(b"\x00" + r for r in rows)
    png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
           + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
    with open(path, "wb") as fh:
        fh.write(png)


def main() -> None:
    out = sys.argv[1] if len(sys.argv) > 1 else "screen.png"
    step = int(sys.argv[2]) if len(sys.argv) > 2 else 1
    # Open without set_configuration so a running display loop is not disturbed.
    lcd = LCD(usb.core.find(idVendor=VID, idProduct=PID))
    t0 = time.time()
    rows = []
    for y in range(0, HEIGHT, step):
        row = bytearray()
        for x in range(0, WIDTH, step):
            d = lcd._in(136, x, y, 2)
            v = d[0] << 8 | d[1]
            r, g, b = (v >> 11) & 0x1F, (v >> 5) & 0x3F, v & 0x1F
            row += bytes(((r << 3) | (r >> 2), (g << 2) | (g >> 4), (b << 3) | (b >> 2)))
        rows.append(bytes(row))
    write_png(out, WIDTH // step, HEIGHT // step, rows)
    print(f"{out}: {WIDTH // step}x{HEIGHT // step} in {time.time() - t0:.0f} s")


if __name__ == "__main__":
    main()
