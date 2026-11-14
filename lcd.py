"""Minimal driver for the Coldtears LCDSysInfo 3.5 (USB 20A0:41ED).

Protocol reconstructed statically from GOverlay 1.4.3 (class LCDsysinfo2).
Every command is a vendor control transfer on endpoint 0. Only commands that
draw or read are implemented; flash/EEPROM/firmware commands (14, 15, 16, 28)
are deliberately left out so this module cannot modify the device storage.
"""

from __future__ import annotations

import struct
from dataclasses import dataclass

import usb.core
import usb.util

VID, PID = 0x20A0, 0x41ED
WIDTH, HEIGHT = 480, 320
OUT, IN = 0x40, 0xC0
TIMEOUT_MS = 1000
TRANSPARENT_BG = 12345          # GOverlay's "no background" sentinel for flash fonts
ICON_KEY_COLOR = 0xC217         # transparency key used with command 193
EOT = "-------------------<EOT>-----------------"
FILE_TABLE_BLOCKS = (16 - 1) * 4096 // 128 - 1   # 479 blocks of 128 bytes


def rgb565(r: int, g: int, b: int) -> int:
    return ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3)


def be16(v: int) -> bytes:
    return struct.pack(">H", max(0, min(0xFFFF, int(v))))


@dataclass(frozen=True)
class FileEntry:
    sector: int
    name: str
    sectors: int
    type: str
    info: str
    md5: str
    size: int

    @property
    def transparent(self) -> bool:
        return "(T)" in self.info


def parse_file_table(text: str) -> dict[str, FileEntry]:
    """Parse '|sector|file|sectors|type|info|md5|bytes;' records."""
    text = text.replace("\r\n", "").replace("\x00", "").split(EOT)[0]
    entries: dict[str, FileEntry] = {}
    for record in text.split(";"):
        f = record.split("|")
        # A full record starts with '|', so f[0] == '' and 7 fields follow.
        if len(f) < 8 or f[1] == "UPDATES" or not f[1].isdigit():
            continue
        try:
            e = FileEntry(int(f[1]), f[2], int(f[3] or 0), f[4], f[5], f[6], int(f[7] or 0))
        except ValueError:
            continue
        entries.setdefault(e.name, e)
    return entries


class LCD:
    def __init__(self, dev: usb.core.Device):
        self.dev = dev

    @classmethod
    def open(cls) -> "LCD":
        dev = usb.core.find(idVendor=VID, idProduct=PID)
        if dev is None:
            raise RuntimeError("LCDSysInfo 3.5 not found (is GOverlay still running?)")
        try:
            dev.set_configuration()
        except usb.core.USBError:
            pass  # already configured
        return cls(dev)

    def close(self) -> None:
        usb.util.dispose_resources(self.dev)

    # -- raw transfers -----------------------------------------------------
    def _out(self, req: int, value: int, index: int, data: bytes = b"") -> None:
        self.dev.ctrl_transfer(OUT, req, value & 0xFFFF, index & 0xFFFF, data, TIMEOUT_MS)

    def _in(self, req: int, value: int, index: int, length: int) -> bytes:
        return bytes(self.dev.ctrl_transfer(IN, req, value & 0xFFFF, index & 0xFFFF, length, TIMEOUT_MS))

    # -- read-only queries -------------------------------------------------
    def serial(self) -> str:
        return "".join(str(b) for b in self._in(12, 0, 5, 8))

    def firmware(self) -> int:
        return self._in(12, 0, 1, 8)[1]

    def locked_status(self) -> int:
        return self._in(12, 0, 2, 11)[7]

    def read_file_table(self) -> dict[str, FileEntry]:
        chunks = []
        for block in range(FILE_TABLE_BLOCKS):
            data = self._in(12, block, 4, 128)
            chunks.append(data.decode("utf-8", "replace"))
            if EOT in chunks[-1] or (len(chunks) > 1 and EOT in chunks[-2] + chunks[-1]):
                break
        return parse_file_table("".join(chunks))

    # -- drawing -----------------------------------------------------------
    def brightness(self, level: int) -> None:
        level = max(0, min(255, level))
        self._out(13, level, level)

    def clear(self, color: int = 0, mask: int = 0x3F) -> None:
        """Fill horizontal bands (bit 0..5) with an RGB565 color."""
        self._out(26, mask & 0x3F, color)

    def background(self, image: int) -> None:
        self._out(30, image, 0)

    def rect(self, x1: int, y1: int, x2: int, y2: int, color: int,
             outline: bool = False, radius: int = 0) -> None:
        data = be16(y1) + be16(x1) + be16(y2) + be16(x2) + be16(color)
        if radius > 0:
            data += bytes([min(255, radius)])
            kind = 9 if outline else 10
        else:
            kind = 2 if outline else 3
        # GOverlay: UsbSetupPacket(0x40, 191, value=data length, index=shape kind, 0)
        self._out(191, len(data), kind, data)

    def text(self, x: int, y: int, s: str, color: int = 0xFFFF,
             width: int = WIDTH, small: bool = False) -> None:
        """Text in the built-in font ('regular' or 'regular_small')."""
        body = (s + "\r\n").encode("ascii", "replace")
        header = (be16(x) + be16(y) + be16(min(x + width, WIDTH)) + be16(y + 40)
                  + be16(color) + be16(0))
        buf = bytearray(len(header) + len(body) + 1)   # same layout GOverlay sends
        buf[:12] = header
        buf[8:8 + len(body)] = body
        self._out(25, len(body) + (512 if small else 0), color, bytes(buf))

    def text_font(self, x: int, y: int, s: str, font: FileEntry, color: int = 0xFFFF,
                  bg: int = TRANSPARENT_BG, width: int = WIDTH) -> None:
        """Text in a font stored in the device flash (e.g. Arial_20px.bin)."""
        body = (s + "\r\n").encode("ascii", "replace")
        header = (be16(x) + be16(y) + be16(min(x + width, WIDTH)) + be16(1)
                  + be16(color) + be16(bg))
        self._out(190, len(body), font.sector, header + body + b"\x00")

    def icon(self, x: int, y: int, entry: FileEntry) -> None:
        data = be16(y) + be16(x)
        if entry.transparent:
            self._out(193, entry.sector, ICON_KEY_COLOR, data)
        else:
            self._out(29, entry.sector, entry.sector, data)
