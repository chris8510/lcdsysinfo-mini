"""Render a GOverlay LCDSysInfo 3.5 template (*.gov) with the device's own primitives.

Template format (observed in 'mystylen1 (LCD2).gov'): one entry per line,
'<kind>,<payload>'. Kind 1 lines are '<element id>=<property>=<value>'; kind 2
lines carry global config ('config=backgroundColor=65535'). Embedded file
blobs follow; they are already stored on the device, so they are ignored here.

GOverlay-specific semantics that are approximated (not reconstructed exactly):
maxdigits/maxdigits_strict, font_fixedspacing and t1_limit/t1_color.
"""

from __future__ import annotations

from collections import OrderedDict
from dataclasses import dataclass, field

from lcd import LCD, FileEntry, TRANSPARENT_BG

FIRST_CHAR_FIELD = 2          # font info field 2 is ' ', field k is chr(0x1E + k)


def load_template(path: str) -> tuple[list[dict], dict]:
    elements: "OrderedDict[str, dict]" = OrderedDict()
    config: dict[str, str] = {}
    with open(path, encoding="latin-1") as fh:
        for line in fh.read().splitlines():
            if line.startswith("--------------------FILE START"):
                break                                  # embedded blobs follow
            kind, _, payload = line.partition(",")
            parts = payload.split("=", 2)
            if kind == "1" and len(parts) == 3 and parts[0]:
                elements.setdefault(parts[0], {})[parts[1]] = parts[2]
            elif kind == "2" and len(parts) == 3 and parts[0] == "config":
                config[parts[1]] = parts[2]
    items = [e for e in elements.values() if e.get("disabled", "Enabled") == "Enabled" and "type" in e]
    return items, config


@dataclass
class FontMetrics:
    widths: dict[str, int] = field(default_factory=dict)
    height: int = 0

    @classmethod
    def from_info(cls, info: str) -> "FontMetrics":
        f = info.split(",")
        m = cls(height=int(f[1]) if len(f) > 1 and f[1].isdigit() else 0)
        for k in range(FIRST_CHAR_FIELD, len(f)):
            v = f[k].replace("#", "")
            if v.isdigit():
                m.widths[chr(0x1E + k)] = int(v)
        return m

    def text_width(self, s: str) -> int:
        default = max(self.widths.values(), default=10)
        return sum(self.widths.get(c, default) for c in s)


def _int(e: dict, key: str, default: int = 0) -> int:
    try:
        return int(float(e.get(key, default) or default))
    except ValueError:
        return default


class TemplateRenderer:
    def __init__(self, lcd: LCD, files: dict[str, FileEntry], path: str):
        self.lcd = lcd
        self.files = files
        self.items, self.config = load_template(path)
        self.metrics: dict[str, FontMetrics] = {}
        self.shown: dict[int, str] = {}
        self.missing: set[str] = set()

    @property
    def sensor_ids(self) -> list[str]:
        return [e["sensor_id"] for e in self.items if e["type"] == "sensor" and e.get("sensor_id")]

    def _font(self, name: str) -> tuple[FileEntry | None, FontMetrics]:
        entry = self.files.get(name)
        if entry is None:
            self.missing.add(name)
            return None, FontMetrics()
        if name not in self.metrics:
            self.metrics[name] = FontMetrics.from_info(entry.info)
        return entry, self.metrics[name]

    def _text(self, e: dict, s: str, clear_box: bool) -> None:
        x, y, w, h = _int(e, "x"), _int(e, "y"), _int(e, "width"), _int(e, "height")
        bgtrans = e.get("font_bgtrans", "0") == "1"
        bg = TRANSPARENT_BG if bgtrans else _int(e, "font_bgcolor", 0xFFFF)
        if clear_box and not bgtrans:
            self.lcd.rect(x, y, x + w, y + h, bg)
        font, m = self._font(e.get("font_style", ""))
        if font is None:
            return
        tw = m.text_width(s)
        align = _int(e, "font_alignment", -1)
        dx = 0 if align < 0 else (w - tw if align > 0 else (w - tw) // 2)
        tx = x + max(0, dx)
        self.lcd.text_font(tx, y, s, font, color=_int(e, "font_color", 0), bg=bg, width=x + w - tx)

    def draw_static(self) -> None:
        self.lcd.clear(_int(self.config, "backgroundColor", 0xFFFF))
        self.shown.clear()
        for e in self.items:
            t = e["type"]
            if t == "Icon":
                entry = self.files.get(e.get("icon", ""))
                if entry is None:
                    self.missing.add(e.get("icon", ""))
                else:
                    self.lcd.icon(_int(e, "x"), _int(e, "y"), entry)
            elif t == "Text":
                self._text(e, e.get("text", ""), clear_box=False)
            elif t == "Rectangle":
                x, y, w, h = _int(e, "x"), _int(e, "y"), _int(e, "width"), _int(e, "height")
                filled = e.get("filled", "1") == "1"
                radius = _int(e, "roundradius") if e.get("rounded") == "1" else 0
                self.lcd.rect(x, y, x + w, y + h, _int(e, "color"), outline=not filled, radius=radius)

    @staticmethod
    def format_value(e: dict, value: float | None) -> str:
        if value is None:
            s = "--"
        else:
            conv = _int(e, "convertpercent")
            if conv > 0:
                value = value * 100.0 / conv
            v = int(value + 0.5) if value >= 0 else -int(-value + 0.5)
            digits = _int(e, "maxdigits")
            if digits > 0 and e.get("maxdigits_strict") == "1":
                v = min(v, 10 ** digits - 1)
            s = str(v)
        return e.get("format", "[s]").replace("[s]", s)

    def update(self, values: dict[str, float | None]) -> None:
        for i, e in enumerate(self.items):
            if e["type"] != "sensor":
                continue
            s = self.format_value(e, values.get(e.get("sensor_id", "")))
            if self.shown.get(i) != s:
                self._text(e, s, clear_box=True)
                self.shown[i] = s
