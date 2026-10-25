# Writing battery protocol providers

[中文 README](../README.md) · [English README](../README.en.md) · [Architecture](ARCHITECTURE.md)

Peripheral Battery Lite has three built-in providers and supports user-installed,
compiled **.NET Framework DLL providers**.

The shared ABI lives in `Contracts/IBatteryProvider.cs` and is distributed as
`PeripheralBatteryLite.Contracts.dll`. The host and the diagnostic child process
use the same contract assembly.

## Built-in implementations

| C# type | Device | Protocol |
| --- | --- | --- |
| `AtkF1V3Provider` | ATK F1V3 | 17-byte Nordic/Compx report |
| `VxeV87Provider` | VXE V87 | 64-byte battery/status HID report |
| `SteelSeriesNova5Provider` | Nova 5 | 65-byte SteelSeries report |

The core uses `ProviderRegistry` to locate `IBatteryProvider` implementations. Each provider declares a stable `Id`, a name, sort order and `ActivityCategory`, then implements `Query(List<HidEndpoint> interfaces)`. **Do not change the stable ID** after users have saved battery caches.

## Implement a plugin

Create a public class with a **public parameterless constructor** implementing
`IBatteryProvider`. A minimal safe example is
[`tests/SamplePlugin.cs`](../tests/SamplePlugin.cs).

To compile the sample on Windows PowerShell (with the .NET Framework compiler),
in the repository root:

```powershell
.\build.ps1

New-Item -ItemType Directory -Force .\dist\Plugins | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:library `
  '/out:dist\Plugins\SamplePlugin.dll' `
  '/r:dist\PeripheralBatteryLite.Contracts.dll' `
  .\tests\SamplePlugin.cs
```

Place the built DLL in the `Plugins` directory **next to the EXEs and Contracts DLL**. Open the app's settings and enable external plugins. Restart for plugin discovery.

By default external plugin loading is disabled. The app never downloads or installs third-party plugins automatically.

## Query contract

- `Status = "OK"` plus `Percent` between `0..100` means a valid reading.
- Return a non-OK status for sleeping, missing, unsupported or unavailable devices.
- `Charging` is optional; set it only when the device actually reports the state.
- Match VID/PID **and** usage page/report lengths. VID/PID alone may identify unrelated devices.
- Validate report signature, command echo, checksum and bounds before accepting a battery value.
- Use documented read-only battery/status commands; **do not guess** firmware, pairing or configuration writes.
- Unrecognized/slow HID responses must have bounded timeouts. Do not spin or keep idle devices awake.
- Cache queries happen in a separate diagnostic process to avoid indefinite GUI stalls.

## Important limits

**Native DLLs are not sandboxed.** They execute with the Windows user's permissions and
may access files, network resources or processes. Unlike a QuickJS plugin, they are not a
safe way to run untrusted community code. Only enable trusted, reviewed DLLs.

Activity gating currently tracks known mouse/keyboard receiver IDs through Windows Raw
Input events. A brand-new external provider is **not automatically included in the host's
activity-source recognition**. With activity-only polling enabled, it may remain manual-only;
a future revision would need an explicit per-provider activity matching API.

For a user-friendly sandboxed HID scripting platform, evaluate the
[QuickJS host in JKWTCN/BatteryMonitor](https://github.com/JKWTCN/BatteryMonitor/blob/master/docs/js-plugin-en.md)
rather than assuming this DLL loader provides equivalent isolation.

## Tests

Run `PeripheralBatteryLite.Probe.exe --self-test` to validate built-in parsers,
refresh policy and persistent storage.

The Windows CI workflow also compiles the sample plugin against Contracts and checks
that the host discovers it from the local `Plugins` directory.

Remember that CI **does not own physical USB receivers**. A new protocol is not considered
real-device verified until its battery query is tested on actual hardware.
