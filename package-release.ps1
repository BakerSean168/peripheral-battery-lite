$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$version = '0.8.1'
$out = Join-Path $root ("dist\PeripheralBatteryLite-v$version-windows-x64.zip")
$temp = Join-Path $env:TEMP ("PBL-release-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $temp | Out-Null
try {
    foreach ($name in @('PeripheralBatteryLite.exe',
        'PeripheralBatteryLite.Probe.exe', 'PeripheralBatteryLite.Contracts.dll',
        'portable.flag')) {
        Copy-Item -LiteralPath (Join-Path $root ("dist\" + $name)) -Destination $temp
    }
    foreach ($name in @('README.md','README.en.md','LICENSE',
        'CONTRIBUTING.md','SECURITY.md')) {
        Copy-Item -LiteralPath (Join-Path $root $name) -Destination $temp
    }
    $docs = Join-Path $temp 'docs'
    New-Item -ItemType Directory -Force $docs | Out-Null
    foreach ($name in @('ARCHITECTURE.md','ECOSYSTEM.md','PERFORMANCE.md',
        'PLUGINS.md','PORTABLE.md','PROTOCOL_SOURCES.md','BATTERY_ANALYTICS.md')) {
        Copy-Item -LiteralPath (Join-Path $root ("docs\" + $name)) -Destination $docs
    }
    # Do not ship developer caches, per-user settings, battery history, alerts or plugins.
    Compress-Archive -Path (Join-Path $temp '*') -DestinationPath $out -Force -CompressionLevel Optimal
    Write-Output "RELEASE_ZIP=$out"
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
