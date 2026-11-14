# Measure CPU cycles of the display process(es) over N seconds.
# -Name LCDSysInfoMini (app, default) or -Name pythonw* (Python prototype).
param([int]$Seconds = 60, [string]$Name = "LCDSysInfoMini")
Add-Type -Namespace W -Name KM -MemberDefinition '[DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint a, bool i, int p); [DllImport("kernel32.dll")] public static extern bool QueryProcessCycleTime(IntPtr h, out ulong c);'
function Cyc($id) { $h = [W.KM]::OpenProcess(0x1000, $false, $id); $c = [uint64]0; [void][W.KM]::QueryProcessCycleTime($h, [ref]$c); $c }
$ids = @(Get-Process -Name $Name -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Id)
$mhz = (Get-CimInstance Win32_Processor).MaxClockSpeed
$a = @{}; foreach ($i in $ids) { $a["$i"] = Cyc $i }
$t0 = Get-Date; Start-Sleep -Seconds $Seconds; $dt = ((Get-Date) - $t0).TotalSeconds
$tot = 0; foreach ($i in $ids) { $tot += (Cyc $i) - $a["$i"] }
$sec = $tot / ($mhz * 1e6)
"{0:N0} Mio. Zyklen in {1:N0} s = ca. {2:N2} % eines Kerns ({3:N3} % aller Kerne, Schaetzung ueber {4} MHz Nenntakt)" -f ($tot / 1e6), $dt, ($sec / $dt * 100), ($sec / $dt / [Environment]::ProcessorCount * 100), $mhz
Get-Process -Id $ids | ForEach-Object { "{0}#{1}: Working Set {2:N1} MB" -f $_.ProcessName, $_.Id, ($_.WorkingSet64 / 1MB) }
