"""LibreHardwareMonitor sensor source (in-process via pythonnet, .NET Framework)."""
from __future__ import annotations

import os
import sys
import time

_LIB = os.path.join(os.path.dirname(os.path.abspath(__file__)), "vendor", "lhm")


class LHM:
    def __init__(self) -> None:
        import pythonnet
        pythonnet.load("netfx")
        import clr  # noqa: F401  (provided by pythonnet)
        sys.path.append(_LIB)
        clr.AddReference(os.path.join(_LIB, "LibreHardwareMonitorLib.dll"))
        from LibreHardwareMonitor.Hardware import Computer
        c = Computer()
        c.IsCpuEnabled = c.IsGpuEnabled = c.IsMemoryEnabled = True
        c.IsMotherboardEnabled = c.IsStorageEnabled = True
        c.Open()
        self.computer = c
        self._last_update: dict[str, float] = {}
        self._values: dict[str, float] = {}
        self._cost: dict[str, list] = {}
        self._only: set[str] | None = None
        if os.path.exists(os.path.join(os.path.dirname(_LIB), "..", "nothrottle")):
            self.INTERVALS = {}       # measurement mode: update everything every read

    def _walk(self, hw):
        yield hw
        for sub in hw.SubHardware:
            yield from self._walk(sub)

    # Seconds between hardware updates; slowly changing sources are polled less often.
    INTERVALS = {"Storage": 10.0, "Motherboard": 5.0, "SuperIO": 5.0}
    DEFAULT_INTERVAL = 0.0          # every read

    def restrict_to(self, sensor_ids: set[str]) -> list[str]:
        """Update only hardware that owns one of these sensors; returns the hardware names kept."""
        keep = set()
        for top in self.computer.Hardware:
            for hw in self._walk(top):
                if any(str(s.Identifier) in sensor_ids for s in hw.Sensors):
                    keep.add(str(hw.Identifier))
        self._only = keep
        return sorted(keep)

    def read(self) -> dict[str, float]:
        """Return {'/identifier/of/sensor': value}; throttled hardware keeps its last values."""
        now = time.monotonic()
        for top in self.computer.Hardware:
            for hw in self._walk(top):
                key = str(hw.Identifier)
                kind = str(hw.HardwareType)
                if self._only is not None and key not in self._only:
                    continue
                if now - self._last_update.get(key, -1e9) < self.INTERVALS.get(kind, self.DEFAULT_INTERVAL):
                    continue
                t0 = time.perf_counter()
                hw.Update()
                cost = self._cost.setdefault(kind, [0.0, 0])
                cost[0] += time.perf_counter() - t0
                cost[1] += 1
                self._last_update[key] = now
                for s in hw.Sensors:
                    if s.Value is not None:
                        self._values[str(s.Identifier)] = float(s.Value)
        return dict(self._values)

    def cost_report(self) -> str:
        """Average wall time per Update() call by hardware type (ms)."""
        return ", ".join(f"{k}: {v[0] / v[1] * 1000:.1f} ms x{v[1]}" for k, v in sorted(self._cost.items()) if v[1])

    def describe(self):
        for top in self.computer.Hardware:
            for hw in self._walk(top):
                hw.Update()
                for s in hw.Sensors:
                    yield str(hw.HardwareType), str(hw.Name), str(s.SensorType), str(s.Name), str(s.Identifier), s.Value

    def close(self) -> None:
        self.computer.Close()


if __name__ == "__main__":
    lhm = LHM()
    for row in lhm.describe():
        print(" | ".join("" if v is None else str(v) for v in row))
    lhm.close()
