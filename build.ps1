$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dest | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @((Join-Path $root 'HidWindows.cs'), (Join-Path $root 'BatteryProtocols.cs'), (Join-Path $root 'SteelSeriesBattery.cs'), (Join-Path $root 'V87KeyboardBattery.cs'), (Join-Path $root 'BatterySnapshotStore.cs'), (Join-Path $root 'Program.cs'))
$common = @('/nologo', '/optimize+', '/platform:x64', '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Core.dll', '/r:System.Runtime.Serialization.dll')
& $csc @common '/target:exe' ("/out:" + (Join-Path $dest 'PeripheralBatteryLite.Probe.exe')) @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $csc @common '/target:winexe' ("/out:" + (Join-Path $dest 'PeripheralBatteryLite.exe')) @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ('Build succeeded: ' + $dest)
