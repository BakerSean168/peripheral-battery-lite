$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dest -Force | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $csc)) { throw 'Windows .NET Framework compiler was not found' }
$relativeSources = @(
    'Program.cs',
    'HidWindows.cs',
    'BatteryProtocols.cs',
    'V87KeyboardBattery.cs',
    'SteelSeriesBattery.cs',
    'BatterySnapshotStore.cs',
    'Core\AppPreferences.cs',
    'Core\RefreshPolicy.cs',
    'Core\BatteryCoordinator.cs',
    'Core\IsolatedQueryHost.cs',
    'Providers\IBatteryProvider.cs',
    'Platform\RawInputActivityTracker.cs',
    'Platform\StartupRegistration.cs',
    'UI\UiTheme.cs',
    'UI\BatteryPanel.cs',
    'UI\SettingsForm.cs',
    'UI\BatteryTrayApp.cs'
)
$sources = @($relativeSources | ForEach-Object { Join-Path $root $_ })
$common = @('/nologo', '/optimize+', '/platform:x64',
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    '/r:System.Core.dll', '/r:System.Runtime.Serialization.dll')
& $csc @common '/target:exe' ("/out:" + (Join-Path $dest 'PeripheralBatteryLite.Probe.exe')) @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $csc @common '/target:winexe' ("/out:" + (Join-Path $dest 'PeripheralBatteryLite.exe')) @sources
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ('Build succeeded: ' + $dest)
