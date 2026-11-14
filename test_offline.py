"""Offline checks: packets match the layout reconstructed from GOverlay."""
from lcd import LCD, parse_file_table, rgb565, FileEntry

class Fake:
    def __init__(self): self.log = []
    def ctrl_transfer(self, t, r, v, i, d, timeout):
        self.log.append((t, r, v, i, bytes(d) if not isinstance(d, int) else d)); return b""

f = Fake(); lcd = LCD(f)
lcd.clear(0xFFFF);                 assert f.log[-1] == (0x40, 26, 0x3F, 0xFFFF, b"")
lcd.brightness(300);               assert f.log[-1] == (0x40, 13, 255, 255, b"")
lcd.rect(10, 20, 110, 60, 0x1234); assert f.log[-1] == (0x40, 191, 10, 3, bytes([0,20,0,10,0,60,0,110,0x12,0x34]))
lcd.rect(10, 20, 110, 60, 1, outline=True); assert f.log[-1][3] == 2
lcd.rect(10, 20, 110, 60, 1, radius=5);     assert f.log[-1][2:4] == (11, 10) and f.log[-1][4][-1] == 5
lcd.rect(10, 20, 110, 60, 1, outline=True, radius=5); assert f.log[-1][2:4] == (11, 9)
lcd.text(300, 100, "AB", 0xF800, width=500)
t, r, v, i, d = f.log[-1]
assert (r, v, i) == (25, 4, 0xF800)                       # len("AB\r\n") = 4
assert d == bytes([1,44, 0,100, 1,224, 0,140]) + b"AB\r\n" + bytes(5) and len(d) == 12 + 4 + 1
lcd.text(0, 0, "x", small=True);   assert f.log[-1][2] == 3 + 512
font = FileEntry(77, "Arial_20px.bin", 3, "font", "", "", 0)
lcd.text_font(5, 6, "Hi", font, color=0x07E0)
t, r, v, i, d = f.log[-1]
assert (r, v, i) == (190, 4, 77) and d == bytes([0,5,0,6,1,0xE0,0,1,0x07,0xE0,0x30,0x39]) + b"Hi\r\n\x00"
icon = FileEntry(200, "icon_hdd_04_on.bmp", 1, "image", "x (T)", "", 0)
lcd.icon(3, 4, icon);              assert f.log[-1] == (0x40, 193, 200, 0xC217, bytes([0,4,0,3]))
lcd.icon(3, 4, FileEntry(201, "a.bmp", 1, "image", "", "", 0)); assert f.log[-1] == (0x40, 29, 201, 201, bytes([0,4,0,3]))
tbl = parse_file_table("|16|splash.bmp|75|image|480x320|abc|307200;|91|icon_mic_01_on.bmp|1|image|32x32 (T)|def|2048;|92|16;|UPDATES|,;-------------------<EOT>-----------------\x00\x00")
assert set(tbl) == {"splash.bmp", "icon_mic_01_on.bmp"} and tbl["icon_mic_01_on.bmp"].transparent and tbl["splash.bmp"].sector == 16
assert rgb565(255, 255, 255) == 0xFFFF and rgb565(255, 0, 0) == 0xF800
print("alle Offline-Tests bestanden")
