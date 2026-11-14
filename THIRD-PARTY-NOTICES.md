# Third-party components

These libraries are not stored in this repository. `tools/fetch-lhm.ps1` downloads them from nuget.org
into `vendor/lhm/`, and `app/build.cmd` copies them next to `LCDSysInfoMini.exe`. Each keeps its own license
(as declared in its NuGet package).

| Package | Version | License |
|---|---|---|
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | 0.9.6 | MPL-2.0 |
| DiskInfoToolkit | 1.1.2 | MPL-2.0 |
| BlackSharp.Core | 1.0.7 | MPL-2.0 |
| RAMSPDToolkit-NDD | 1.4.2 | MPL-2.0 |
| HidSharp | 2.6.4 | Apache-2.0 (license file in package) |
| System.Memory | 4.6.3 | MIT |
| System.Buffers | 4.6.1 | MIT |
| System.Numerics.Vectors | 4.6.1 | MIT |
| System.Runtime.CompilerServices.Unsafe | 6.1.2 | MIT |
| System.Threading.AccessControl | 10.0.3 | MIT |
| System.Security.AccessControl | 6.0.0 | MIT |
| System.Security.Principal.Windows | 5.0.0 | MIT |

The source code of the MPL-2.0 components is available from their upstream projects; this project uses them
unmodified.

Runtime dependencies that are installed separately and not distributed here:

- **libusb-win32** (`libusb0.dll`, `libusb0.sys`), LGPL/GPL, installed with GOverlay / the LCDSysInfo driver.
- **PawnIO** driver, see https://pawnio.eu.

The Python prototype uses `pyusb` (BSD-3-Clause), `psutil` (BSD-3-Clause) and `pythonnet` (MIT), installed via pip.
