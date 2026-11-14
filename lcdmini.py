"""Tiny status display for the LCDSysInfo 3.5.

Usage:
  python lcdmini.py info   # read-only: serial, firmware, file table
  python lcdmini.py demo   # draw a test screen once
  python lcdmini.py run    # live status, redraws only changed values
  python lcdmini.py gov [vorlage.gov] [--once]
                           # GOverlay-Vorlage mit LibreHardwareMonitor-Werten

GOverlay must not be running: only one program can open the device.
"""

from __future__ import annotations

import mmap
import re
import sys
import time

import psutil
import usb.core

from lcd import HEIGHT, LCD, WIDTH, rgb565

BG = rgb565(0, 0, 0)
FG = rgb565(255, 255, 255)
DIM = rgb565(120, 120, 120)
ACCENT = rgb565(0, 170, 255)
BAR_BG = rgb565(40, 40, 40)

ROW_H = 52
LABEL_X, VALUE_X, BAR_X, BAR_W = 12, 150, 290, 178


# -- sensors ---------------------------------------------------------------
def aida64_values() -> dict[str, str]:
    """Read AIDA64 shared memory if 'Enable shared memory' is on, else {}."""
    try:
        mm = mmap.mmap(-1, 64 * 1024, "AIDA64_SensorValues", access=mmap.ACCESS_READ)
    except OSError:
        return {}
    try:
        raw = mm.read().split(b"\x00", 1)[0].decode("latin-1")
    finally:
        mm.close()
    return dict(re.findall(r"<id>([^<]+)</id><label>[^<]*</label><value>([^<]*)</value>", raw))


def read_sensors() -> list[tuple[str, str, float | None]]:
    """Return (label, text, bar fraction or None) rows."""
    a = aida64_values()
    cpu = psutil.cpu_percent(interval=None)
    mem = psutil.virtual_memory().percent
    rows = [("CPU", f"{cpu:3.0f} %", cpu / 100), ("RAM", f"{mem:3.0f} %", mem / 100)]
    for key, label in (("TCPU", "CPU Temp"), ("TGPU1", "GPU Temp")):
        if key in a:
            t = float(a[key])
            rows.append((label, f"{t:3.0f} C", min(t, 100) / 100))
    if "SGPU1UTI" in a:
        g = float(a["SGPU1UTI"])
        rows.append(("GPU", f"{g:3.0f} %", g / 100))
    rows.append(("Zeit", time.strftime("%H:%M:%S"), None))
    return rows[: HEIGHT // ROW_H]


# -- screen ----------------------------------------------------------------
class Screen:
    def __init__(self, lcd: LCD):
        self.lcd = lcd
        self.shown: dict[int, tuple[str, str, int]] = {}

    def reset(self) -> None:
        self.lcd.clear(BG)
        self.shown.clear()

    def draw(self, rows) -> None:
        for i, (label, text, frac) in enumerate(rows):
            y = 8 + i * ROW_H
            bar = -1 if frac is None else round(max(0.0, min(1.0, frac)) * BAR_W)
            old = self.shown.get(i)
            if old and old[0] != label:
                old = None
            if old is None:
                self.lcd.rect(0, y, WIDTH - 1, y + ROW_H - 4, BG)
                self.lcd.text(LABEL_X, y, label, DIM, width=VALUE_X - LABEL_X)
            if old is None or old[1] != text:
                self.lcd.rect(VALUE_X, y, BAR_X - 1, y + 36, BG)
                self.lcd.text(VALUE_X, y, text, FG, width=BAR_X - VALUE_X)
            if bar >= 0 and (old is None or old[2] != bar):
                top, bottom = y + 10, y + 30
                if bar > 0:
                    self.lcd.rect(BAR_X, top, BAR_X + bar, bottom, ACCENT)
                if bar < BAR_W:
                    self.lcd.rect(BAR_X + bar, top, BAR_X + BAR_W, bottom, BAR_BG)
            self.shown[i] = (label, text, bar)


# -- commands --------------------------------------------------------------
def cmd_info() -> None:
    lcd = LCD.open()
    try:
        print("Seriennummer:", lcd.serial())
        print("Firmware:    ", lcd.firmware())
        print("Sperrstatus: ", lcd.locked_status(), "(137 = gesperrt)")
        table = lcd.read_file_table()
        print(f"Dateitabelle: {len(table)} Einträge")
        for e in sorted(table.values(), key=lambda e: e.sector):
            t = " (T)" if e.transparent else ""
            print(f"  {e.sector:5d}  {e.type:6s} {e.name}{t}  [{e.info}]")
    finally:
        lcd.close()


def cmd_demo() -> None:
    lcd = LCD.open()
    try:
        lcd.clear(BG)
        lcd.text(12, 8, "lcdsysinfo-mini", ACCENT)
        lcd.text(12, 60, "Hallo vom Prototyp", FG)
        lcd.text(12, 112, "kleine Schrift", DIM, small=True)
        lcd.rect(12, 170, 300, 200, ACCENT)
        lcd.rect(12, 220, 300, 250, FG, outline=True)
        lcd.rect(320, 170, 460, 250, rgb565(255, 80, 0), radius=12)
    finally:
        lcd.close()


def cmd_run(interval: float = 1.0) -> None:
    psutil.cpu_percent(interval=None)  # prime the counter
    while True:
        try:
            lcd = LCD.open()
            screen = Screen(lcd)
            screen.reset()
            while True:
                screen.draw(read_sensors())
                time.sleep(interval)
        except (usb.core.USBError, RuntimeError) as exc:
            print("Gerät nicht erreichbar, neuer Versuch in 5 s:", exc, file=sys.stderr)
            time.sleep(5)
        except KeyboardInterrupt:
            return


DEFAULT_TEMPLATE = r"C:\Program Files (x86)\GOverlay\mystylen1 (LCD2).gov"


def cmd_gov(path: str = DEFAULT_TEMPLATE, interval: float = 1.0, once: bool = False) -> None:
    """Render a GOverlay template with LibreHardwareMonitor values."""
    import os
    from govtemplate import TemplateRenderer
    from sensormap import USED_KEYS, resolve
    from sensors_lhm import LHM

    lhm = LHM()
    try:
        with open("sensors.txt", "w", encoding="utf-8") as fh:   # inventory for mapping
            for row in lhm.describe():
                fh.write(" | ".join("" if v is None else str(v) for v in row) + "\n")
        while True:
            try:
                lcd = LCD.open()
                files = lcd.read_file_table()
                r = TemplateRenderer(lcd, files, path)
                r.draw_static()
                if r.missing:
                    print("Fehlt auf dem Gerät:", ", ".join(sorted(r.missing)), file=sys.stderr)
                first = True
                cycles = 0
                while True:
                    cycles += 1
                    if cycles == 120:
                        print(time.strftime("%Y-%m-%d %H:%M:%S"), "LHM Update-Kosten:", lhm.cost_report(), flush=True)
                    values = resolve(r.sensor_ids, lhm.read())
                    r.update(values)
                    if once or first:
                        print(time.strftime("%Y-%m-%d %H:%M:%S"), "Werte:",
                              {k: (None if v is None else round(v, 1)) for k, v in values.items()}, flush=True)
                        first = False
                        if not os.path.exists("nothrottle"):
                            kept = lhm.restrict_to(USED_KEYS)
                            print("Abgefragte Hardware:", ", ".join(kept), flush=True)
                    if once:
                        return
                    time.sleep(interval)
            except (usb.core.USBError, RuntimeError) as exc:
                print("Gerät nicht erreichbar, neuer Versuch in 5 s:", exc, file=sys.stderr)
                time.sleep(5)
    except KeyboardInterrupt:
        pass
    finally:
        lhm.close()


if __name__ == "__main__":
    if "--log" in sys.argv:                      # for windowless autostart (pythonw)
        i = sys.argv.index("--log")
        log = open(sys.argv[i + 1], "a", encoding="utf-8", buffering=1)
        sys.stdout = sys.stderr = log
        del sys.argv[i:i + 2]
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    if cmd == "gov":
        args = [a for a in sys.argv[2:] if a != "--once"]
        cmd_gov(*(args[:1] or [DEFAULT_TEMPLATE]), once="--once" in sys.argv)
    else:
        {"info": cmd_info, "demo": cmd_demo, "run": cmd_run}.get(cmd, lambda: print(__doc__))()
