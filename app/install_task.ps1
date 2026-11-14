# Run elevated (right-click > "Mit PowerShell ausfuehren" as administrator, or from an admin shell).
# - registers the logon task 'lcdsysinfo-mini' that starts LCDSysInfoMini.exe with admin rights
#   (needed so LibreHardwareMonitor can read CPU temperature and fans via PawnIO)
# - backs up and disables GOverlay's own autostart task, if present (not deleted)
$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
Start-Transcript -Path (Join-Path $dir 'install_task.log') -Force | Out-Null
try {
    $exe = Join-Path $dir 'LCDSysInfoMini\LCDSysInfoMini.exe'
    if (-not (Test-Path $exe)) { throw "$exe fehlt - zuerst build.cmd ausfuehren" }
    $user = "$env:USERDOMAIN\$env:USERNAME"

    if (Get-ScheduledTask -TaskName 'GOverlay Autostart' -ErrorAction SilentlyContinue) {
        $backup = Join-Path $dir 'backup_GOverlay_Autostart.xml'
        if (-not (Test-Path $backup)) { Export-ScheduledTask -TaskName 'GOverlay Autostart' | Out-File $backup -Encoding unicode }
        Disable-ScheduledTask -TaskName 'GOverlay Autostart' | Out-Null
        "GOverlay Autostart: gesichert ($backup) und deaktiviert"
    }
    Get-Process GOverlay -ErrorAction SilentlyContinue | Stop-Process -Force

    Stop-ScheduledTask -TaskName 'lcdsysinfo-mini' -ErrorAction SilentlyContinue
    Get-Process LCDSysInfoMini -ErrorAction SilentlyContinue | Stop-Process -Force
    $action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory (Split-Path $exe)
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
        -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName 'lcdsysinfo-mini' -Action $action -Trigger $trigger -Principal $principal `
        -Settings $settings -Description 'LCDSysInfo Mini: Statusanzeige fuer das LCDSysInfo 3.5 (Ersatz fuer GOverlay)' -Force | Out-Null
    "Aufgabe 'lcdsysinfo-mini' zeigt auf $exe"
    Start-ScheduledTask -TaskName 'lcdsysinfo-mini'
    Start-Sleep -Seconds 10
    "Status: " + (Get-ScheduledTask -TaskName 'lcdsysinfo-mini').State
} catch { "FEHLER: $_" } finally { Stop-Transcript | Out-Null }
