"""Map GOverlay sensor ids (AIDA64 / OHMD) to LibreHardwareMonitor and psutil.

AIDA64 ids are replaced by the closest LHM sensor. Lookups are by LHM sensor
identifier; the first identifier that exists wins, so the mapping survives
different hardware indices.
"""

from __future__ import annotations

import re

import psutil


def _disk_free_gb(drive: str):
    def read(_lhm: dict[str, float]) -> float | None:
        try:
            return psutil.disk_usage(drive + ":\\").free / 1024**3
        except OSError:
            return None
    return read


USED_KEYS: set[str] = set()        # LHM sensor identifiers the template actually reads


def _lhm(*patterns: str):
    regs = [re.compile(p) for p in patterns]

    def read(lhm: dict[str, float]) -> float | None:
        for r in regs:
            for key in sorted(lhm):
                if r.fullmatch(key):
                    USED_KEYS.add(key)
                    return lhm[key]
        return None
    return read


# GOverlay id -> provider. Comments name what the AIDA64 id measures.
MAP = {
    "[OHMD:/amdcpu/0/load/0]": _lhm(r"/amdcpu/0/load/0", r"/intelcpu/0/load/0"),     # CPU total load
    "[AIDA:Value.SMEMUTI]": _lhm(r"/ram/load/0"),                                     # memory utilisation
    "[AIDA:Value.DGPU1]": _lhm(r"/gpu-(amd|nvidia|intel)/0/load/0"),                  # shown as GPU %, mapped to GPU core load
    "[AIDA:Value.TCPU]": _lhm(r"/amdcpu/0/temperature/2", r"/amdcpu/0/temperature/\d+",
                              r"/intelcpu/0/temperature/\d+"),                        # CPU temperature (needs PawnIO + admin)
    "[AIDA:Value.TGPU1]": _lhm(r"/gpu-(amd|nvidia|intel)/0/temperature/0"),           # GPU core temperature
    # AIDA64 counts drives from 1 (HDD2 = 2nd drive); LHM uses the Windows disk number (from 0).
    "[AIDA:Value.THDD1]": _lhm(r"/(hdd|nvme|ssd|storage)/0/temperature/0"),           # drive temperature (needs admin)
    "[AIDA:Value.THDD2]": _lhm(r"/(hdd|nvme|ssd|storage)/1/temperature/0"),
    "[AIDA:Value.FCPU]": _lhm(r"/lpc/[^/]+/0/fan/[01]", r"/lpc/[^/]+/fan/[01]"),      # CPU fan rpm (needs PawnIO + admin)
    "[AIDA:Value.FGPU1]": _lhm(r"/gpu-(amd|nvidia|intel)/0/fan/0"),                   # GPU fan rpm
    "[AIDA:Value.SDRVCFREESPC]": _disk_free_gb("C"),                                  # free space C: (GB)
    "[AIDA:Value.SDRVDFREESPC]": _disk_free_gb("D"),                                  # free space D: (GB)
}


def resolve(ids: list[str], lhm_values: dict[str, float]) -> dict[str, float | None]:
    out: dict[str, float | None] = {}
    for sid in ids:
        provider = MAP.get(sid)
        if provider is None and sid.startswith("[OHMD:"):
            provider = _lhm(re.escape(sid[6:-1]))
        v = provider(lhm_values) if provider else None
        # LHM reports 0.0 for sensors it cannot read without its driver.
        is_temp = "Value.T" in sid or "/temperature/" in sid
        out[sid] = None if v is None or (v == 0.0 and is_temp) else v
    return out
