$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$dest = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dest -Force | Out-Null
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $csc)) { throw 'Windows .NET Framework compiler was not found' }

$common = @('/nologo', '/optimize+', '/platform:x64',
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    '/r:System.Core.dll', '/r:System.Runtime.Serialization.dll')
$contractFiles = @(
    'Contracts\BatteryReading.cs',
    'Contracts\IBatteryProvider.cs',
    'Contracts\HidWindows.cs'
) | ForEach-Object { Join-Path $root $_ }
$contractPath = Join-Path $dest 'PeripheralBatteryLite.Contracts.dll'
& $csc @common '/target:library' ("/out:" + $contractPath) @contractFiles
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$sourceFiles = @(
    'Program.cs',
    'BatteryProtocols.cs',
    'V87KeyboardBattery.cs',
    'SteelSeriesBattery.cs',
    'BatterySnapshotStore.cs',
    'Core\AppPreferences.cs',
    'Core\RefreshPolicy.cs',
    'Core\BatteryCoordinator.cs',
    'Core\IsolatedQueryHost.cs',
    'Providers\ProviderRegistry.cs',
    'Platform\RawInputActivityTracker.cs',
    'Platform\StartupRegistration.cs',
    'UI\UiTheme.cs',
    'UI\BatteryPanel.cs',
    'UI\SettingsForm.cs',
    'UI\BatteryTrayApp.cs'
) | ForEach-Object { Join-Path $root $_ }
$references = @('/r:' + $contractPath)
& $csc @common @references '/target:exe' ("/out:" + (Join-Path $dest 'PeripheralBatteryLite.Probe.exe')) @sourceFiles
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $csc @common @references '/target:winexe' ("/out:" + (Join-Path $dest 'PeripheralBatteryLite.exe')) @sourceFiles
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host ('Build succeeded: ' + $dest)
