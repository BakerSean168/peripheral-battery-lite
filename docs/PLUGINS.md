# Battery protocol providers

Peripheral Battery Lite uses compile-time built-in providers and optional
user-installed .NET Framework DLL providers. The stable protocol boundary
is `IBatteryProvider` in `Providers/IBatteryProvider.cs`.

A provider has `Id`, `DisplayName`, `ShortName`, `SortOrder`,
`Activity` and `Query(List<HidEndpoint>)`. Battery data is returned
as `BatteryReading`; `Status="OK"` and `Percent=0..100` denote a valid
response, while exceptions or absent hardware must return a non-OK status.
The `Id` is used as the persistent cache key and should never be renamed.

The built-ins are registered via reflection within the application assembly:

- `AtkF1V3Provider` — Nordic/Compx 17-byte USB HID battery report
- `VxeV87Provider` — Weisheng 64-byte status report (2.4G)
- `SteelSeriesNova5Provider` — SteelSeries 65-byte USB HID battery report

## Adding a provider

Implement the public `IBatteryProvider` type and provide a public parameterless
constructor. Compile with .NET Framework 4.8, referencing the shared
`PeripheralBatteryLite.Contracts.dll` assembly. The public types are shared
between the tray and diagnostic EXEs. A sample implementation lives in
`tests/SamplePlugin.cs`. Distribute the Contracts DLL with both EXEs.

Place the built DLL in the `Plugins` folder next to the exe, then enable
"允许加载 Plugins 目录内的第三方 DLL" in Settings and restart.

**Safety:** DLLs are loaded in-process with the user's full OS permissions.
They are *not sandboxed*. Only enable trusted source-reviewed plugins. The
option is OFF by default. The app does not retrieve plugins from a network
or automatically install them.

## Activity rules

A provider must classify itself with the appropriate `ActivityCategory`.
The optional "only when active" auto-poll gate uses Windows Raw Input events,
matched by the known mouse/keyboard receiver IDs. A headset dongle remaining
attached is *not* evidence the headset is awake; built-in audio providers are
intentionally excluded from background polling when active-only is enabled.
Unchecking this option enables scheduled queries for every provider.

This is a best-effort power-saving heuristic, not a verified physical
sleep-state detector. Manual refresh bypasses the gate.
