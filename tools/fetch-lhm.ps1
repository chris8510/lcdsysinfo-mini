# Downloads LibreHardwareMonitorLib 0.9.6 and its .NET Framework dependencies from nuget.org
# into vendor\lhm. Versions are pinned; System.Management/System.CodeDom come with .NET Framework.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$out = Join-Path $root 'vendor\lhm'
New-Item -ItemType Directory -Force $out | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem

# id, version, path of the DLL inside the package
$packages = @(
    @('LibreHardwareMonitorLib', '0.9.6', 'runtimes/win-x64/lib/net472/LibreHardwareMonitorLib.dll'),
    @('DiskInfoToolkit', '1.1.2', 'lib/net472/DiskInfoToolkit.dll'),
    @('BlackSharp.Core', '1.0.7', 'lib/net472/BlackSharp.Core.dll'),
    @('RAMSPDToolkit-NDD', '1.4.2', 'lib/net472/RAMSPDToolkit-NDD.dll'),
    @('HidSharp', '2.6.4', 'lib/netstandard2.0/HidSharp.dll'),
    @('System.Memory', '4.6.3', 'lib/net462/System.Memory.dll'),
    @('System.Buffers', '4.6.1', 'lib/net462/System.Buffers.dll'),
    @('System.Numerics.Vectors', '4.6.1', 'lib/net462/System.Numerics.Vectors.dll'),
    @('System.Runtime.CompilerServices.Unsafe', '6.1.2', 'lib/net462/System.Runtime.CompilerServices.Unsafe.dll'),
    @('System.Threading.AccessControl', '10.0.3', 'lib/net462/System.Threading.AccessControl.dll'),
    @('System.Security.AccessControl', '6.0.0', 'lib/net461/System.Security.AccessControl.dll'),
    @('System.Security.Principal.Windows', '5.0.0', 'lib/net461/System.Security.Principal.Windows.dll')
)

$tmp = Join-Path ([IO.Path]::GetTempPath()) ('lhm-' + [Guid]::NewGuid())
New-Item -ItemType Directory $tmp | Out-Null
try {
    foreach ($p in $packages) {
        $id, $ver, $inner = $p
        $pkg = Join-Path $tmp "$id.$ver.nupkg"
        Invoke-WebRequest -UseBasicParsing "https://www.nuget.org/api/v2/package/$id/$ver" -OutFile $pkg
        $zip = [IO.Compression.ZipFile]::OpenRead($pkg)
        try {
            $entry = $zip.Entries | Where-Object { $_.FullName -eq $inner }
            if (-not $entry) { throw "$inner fehlt in $id $ver" }
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $out ([IO.Path]::GetFileName($inner))), $true)
            $nuspec = $zip.Entries | Where-Object { $_.FullName -like '*.nuspec' } | Select-Object -First 1
            $reader = New-Object IO.StreamReader($nuspec.Open())
            $xml = [xml]$reader.ReadToEnd(); $reader.Close()
            $lic = $xml.package.metadata.license.'#text'
            if (-not $lic) { $lic = $xml.package.metadata.licenseUrl }
            "{0} {1}: {2}" -f $id, $ver, $lic
        } finally { $zip.Dispose() }
    }
} finally { Remove-Item -Recurse -Force $tmp }
