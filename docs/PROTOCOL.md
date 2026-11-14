# LCDSysInfo 3.5 (USB 20A0:41ED): reconstructed protocol

*[Deutsche Fassung](PROTOCOL.de.md)*

Source: static analysis of `GOverlay.exe` 1.4.3
(SHA-256 `14df5442…bd4e767`, MVID `71548391-6036-4f9d-80ee-efb6c3aac153`)
with REA `inspect_managed_members`, class `GOverlay.LCDsysinfo2`. Method names are not obfuscated.

Everything here is derived from the IL code. "Observed" means it is written that way in the code; "inferred" means
it is an interpretation.

**Confirmed on the device (firmware 251):**
- Read commands: serial number, firmware, lock status, file table (134 entries), pixel read (136).
- Drawing commands: fill (26), built-in font text (25), rectangles (191), icons with and without transparency
  (29/193), flash font text (190).

Not yet tested on the device: brightness (13), background image (30).

## Transport

- Library used by GOverlay: LibUsbDotNet (`UsbDevice.ControlTransfer`, `UsbSetupPacket`),
  driver libusb-win32 (`libusb0.sys`).
- **All commands are control transfers on endpoint 0.** There are no bulk or interrupt endpoints for drawing.
  - Write: `bmRequestType = 0x40` (vendor, host→device)
  - Read: `bmRequestType = 0xC0` (vendor, device→host)
- Multi-byte values in the data stage are **big-endian** (`hi = v / 256`, `lo = v % 256`).
- Colours are **RGB565** (16 bit), e.g. `65535` = white.
- Display: **480 × 320** pixels (bounds checks in `Draw_Text_Font`).
- The code passes 0 as `wLength` in the setup packet; the actual length comes from the buffer handed to
  `ControlTransfer`. Inferred: the library sets `wLength` from the buffer length.

## Commands (bRequest)

| bRequest | Dir | wValue | wIndex | Data | Meaning (method) |
|---|---|---|---|---|---|
| 12 (0x0C) | IN | 0 | 5 | read 8 bytes | serial number (`init`) |
| 12 | IN | 0 | 1 | read 8 bytes, byte 1 | firmware version (`init`) |
| 12 | IN | 0 | 2 | read 11 bytes, byte 7 | lock status, 137 = "blacklisted" (`GetLockedStatus`) |
| 12 | IN | counter | 4 | 128 bytes per call | file table from flash (`readTable`) |
| 12 | IN | … | 3 | … | read a screenshot from the device (`readScreenFromDevice`) |
| 13 (0x0D) | OUT | level | level | – | brightness 0…255 (`brightness`) |
| 14 (0x0E) | OUT | image no. | 4 | 5 bytes | show flash image (`Flash_Screen`) |
| 15 (0x0F) | OUT | arg1 | arg2 | – | flash command (`Send_Command_to_flash`) |
| 16 (0x10) | OUT | 0 | arg | … | copy to flash (`CopyToFlash1`) |
| 25 (0x19) | OUT | text length (+512 = small font) | colour | header + ASCII | text in built-in font (`Draw_Text_Font`) |
| 26 (0x1A) | OUT | band mask | colour | – | fill bands with a colour (`ClearLines`, `ClearScreen`) |
| 28 (0x1C) | OUT | 514 | level | – | idle brightness (`brightnessidle`) |
| 28 | OUT | … | … | … | write EEPROM / firmware mode (`WriteEprom`, `putInFirmwareMode`): **do not use** |
| 29 (0x1D) | OUT | sector | sector | 4-byte position | draw icon from flash (`Draw_Icon`) |
| 30 (0x1E) | OUT | image no. | 0 | – | set background image (`SetBackground`) |
| 33 (0x21) | OUT | … | … | ≥13 bytes | progress bar (`Draw_progress_bar`) |
| 136 (0x88) | IN | x | y | – | read pixel, 2 bytes RGB565 (`getpixel`) |
| 189 (0xBD) | OUT | spacing | 1 | – | font character spacing (`SetCharacterSpaceFont`) |
| 190 (0xBE) | OUT | text length | font sector | header + ASCII | text in a flash font (`Draw_Text_Font`) |
| 191 (0xBF) | OUT | data length | shape kind | 10–11 bytes | rectangle, circle, triangle, lines, pixels (`Draw_*`) |
| 192 (0xC0) | OUT | count | 0 | list | several icons at once (`Draw_Icons`) |
| 193 (0xC1) | OUT | sector | 0xC217 | 4-byte position | icon with transparency, key colour 0xC217 (`Draw_Icon`) |

### Details of the most important commands

**Fill (26):** the method takes six flags that form a mask (`wValue`, bits 0…5 = 1, 2, 4, 8, 16, 32). `wIndex` is
the RGB565 colour. `ClearScreen(c)` sets all six bits, i.e. mask `0x3F`. Inferred: six horizontal bands of 320/6 px.

**Text, built-in font (25):**
- Fonts: `regular` (`wValue = length`) or `regular_small` (`wValue = length + 512`).
- `wIndex` = text colour (RGB565).
- Data: `x(2) y(2) x_end(2) y+40(2)`, then the ASCII text plus `"\r\n"`, then a zero byte.
- `x_end = min(x + width, 480)`.
- The code builds a 12-byte header but copies the text starting at byte 8, so the colour fields are overwritten and
  only 8 header bytes take effect.

**Text, flash font (190):**
- `wValue` = text length, `wIndex` = flash sector of the font (from the file table).
- Data: `x(2) y(2) x_end(2) 1(2) colour(2) background(2)`, then text + `"\r\n"` + zero byte.
- Background `12345` means "transparent". The device clips text at `x_end`.
- Character widths for alignment are in the font's `info` field of the file table: field 2 is `' '`, field *k* is
  the character `0x1E + k` (a `#` prefix is ignored, empty fields are missing characters).

**Icon (29 / 193):**
- Data: `y(2) x(2)`, in this order.
- The sector comes from the file table via the icon's file name.
- 193 draws with transparency key colour 0xC217. The icon bitmaps use 197,65,189 or 206,65,198 (RGB) for it.

**Rectangle (191):** resolved from the 9 call sites in `LCDSysInfo2_Do.DoLCDSys`.
- Signature: `Draw_Rectangle(x1, y1, x2, y2, colour, outline, radius)`.
- x2/y2 are **end coordinates**. Example from the code: `(x, y, x+width, y+height, color_bg, 0, 0)`.
- Data: `y1(2) x1(2) y2(2) x2(2) colour(2) [radius(1)]` – 10 bytes, 11 with radius > 0.
- `wValue` = data length (10 or 11). `wIndex` = shape kind:

  | | no radius | with radius |
  |---|---|---|
  | filled (`outline = 0`, `color_bg`) | 3 | 10 |
  | outline only (`outline = 1`, `color_border`) | 2 | 9 |

- **Confirmed on the device:** filled, outline and rounded, verified by reading back pixels with command 136.

**File table (12, wIndex 4):** resolved from `LCDSys2_StoreFileTableOnDevice` and `LCDSys2_getFilesTable`.
- Flash: 2048 sectors of 4096 bytes (8 MiB). The first 16 sectors (`sectors_filetable`) hold the table.
- Read: `wValue` = block number 0, 1, 2 …, 128 bytes each, at most 479 blocks, until the line
  `-------------------<EOT>-----------------`.
- Then remove `\r\n` and zero bytes and cut at the EOT marker.
- Content is ASCII. Records are separated by `;`, fields by `|`; every record starts with `|`:
  - used: `|<sector>|<file>|<sector_count>|<type>|<info>|<md5>|<bytes>;`
  - reference (same file as another sector): `|<sector>|<ref_sector>;`
  - at the end: `|UPDATES|<list>;`
- Icons and fonts are looked up by `<file>` (e.g. `icon_hdd_04_on.bmp`, `Arial_14px.bin`); commands 29/193 and 190
  use its `<sector>`. `<type>` is numeric (1 = image, 3 = font).
- `trans` = 1 if `<info>` contains `(T)`; such icons are drawn with 193 instead of 29.
- Reading is harmless. Writing (`StoreFileTableOnDevice`) uses flash commands 15/16 and is not needed for a display app.

## What this means for a lean replacement app

The device renders by itself; the host only sends tiny commands:

- text: about 20–40 bytes
- icon: 4 bytes
- fill: 0 data bytes

Icons, backgrounds and fonts are already stored in flash. One update per second with 10–20 values is a few hundred
bytes.

Minimal flow:

1. Open the device (VID 0x20A0, PID 0x41ED).
2. Read the file table once (name → sector) and cache it.
3. `SetBackground` or `ClearScreen`.
4. Per tick only changed values: paint over the old area with 191/26, then text 25/190 or icon 29/193.

## Open points / not verified

- Still open: command 33 (progress bar, replaceable by two rectangles), the other shape kinds of 191 (circle,
  triangle, lines), screenshot read (wIndex 3). None of these is needed by LCDSysInfo Mini.
- Commands 15, 16 and 28 (flash, EEPROM, firmware mode) can modify the device. A display app does not need them and
  should avoid them.
