"""Read back pixels (command 136, read-only) to check the demo screen."""
from lcd import LCD, rgb565
import lcdmini as m
lcd = LCD.open()
def px(x, y):
    d = lcd._in(136, x, y, 2); return d[0] * 256 + d[1]
checks = [("Hintergrund", 470, 10, m.BG), ("gefuellt Mitte", 150, 185, m.ACCENT),
          ("gefuellt Ecke", 12, 170, m.ACCENT), ("Umriss links", 12, 235, m.FG),
          ("Umriss oben", 150, 220, m.FG), ("Umriss innen", 150, 235, m.BG),
          ("abgerundet Mitte", 390, 210, rgb565(255, 80, 0)),
          ("abgerundet Eckpixel", 320, 170, None), ("zwischen", 310, 185, m.BG)]
for name, x, y, exp in checks:
    v = px(x, y)
    res = "" if exp is None else ("OK" if v == exp else f"ABWEICHUNG (erwartet 0x{exp:04X})")
    print(f"{name:20s} ({x:3d},{y:3d}) = 0x{v:04X} {res}")
lcd.close()
