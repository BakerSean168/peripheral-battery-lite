# Peripheral Battery Lite

A lightweight, native Windows system-tray battery monitor with modular HID
protocol providers. It currently supports:

| Peripheral | VID:PID | Battery query |
| --- | --- | --- |
| ATK F1V3 mouse (2.4G receiver) | 373B:1303 | Nordic/Compx HID, 17-byte battery report |
| VXE V87 keyboard (2.4G receiver) | 320F:5088 | Telink/Weisheng HID, 64-byte status report |
| SteelSeries Arctis Nova 5 headset | 1038:2232 | SteelSeries HID, 65-byte B0 report |

The project began as a small local Windows utility and was moved to Oracle2
for source development. All three protocols have been tested with real
connected hardware in Windows 11.

## Features

- Native C# / WinForms, .NET Framework 4.8, Windows HID APIs; no Electron,
  Chromium, Node.js background server, account login or internet traffic.
- Compact dark floating battery dashboard with battery bars, last-query
  timestamps, a tray percentage icon, manual refresh and a settings dialog.
- Saves successful readings and timestamps under
  \`%LOCALAPPDATA%\PeripheralBatteryLite\battery-cache.json\`.
  Query failures retain the last successful reading.
- Automatic polling is **OFF by default**. Optional intervals: 30 minutes,
  1 hour, 2 hours, 5 hours. A 30-second local timer checks only whether a
  query is due; it doesn't contact HID endpoints when none is due.
- Optional "only when active" mode: monitors only the Windows Raw Input
  *device header* of mouse and keyboard input, not key contents or cursor
  coordinates. It polls automatically only when the matching receiver has
  generated an input event within the previous 3 minutes.
- Headset "awake" state cannot be passively verified from its connected USB
  dongle. With activity-only enabled, Nova 5 is **manual-refresh-only**.
  Disabling activity-only allows it to be auto-queried at the selected interval.
- Windows startup setting via current-user Run registry key (no admin).
- Modular \`IBatteryProvider\` registry: built-ins are discovered by reflection.
  User-installed DLLs under \`Plugins/\` can be enabled explicitly, and are
  **disabled by default**. See [Provider guide](docs/PLUGINS.md).

## Usage

Download/build the EXEs and run \`PeripheralBatteryLite.exe\`. Click the
notification area icon to open the dashboard. Click **刷新电量** to update all
three devices. Click **设置** to change the polling interval, activity check,
startup preference, pin behavior and optional external plugins.

If the app runs but its icon is hidden, expand the Windows system tray with
\`^\`.

To prevent queries from waking a sleeping wireless keyboard, retain the
default manual-refresh-only mode, or enable activity-only automatic refresh.
Note that passive input tracking is a best-effort hint, not a hardware-specific
sleep-state detector. Manual refresh can wake a sleeping device.

## Development and build

Source of truth: \`/home/ubuntu/projects/peripheral-battery-lite\` on Oracle2.

Since it uses WinForms and Win32 HID, build and hardware test on Windows 10/11.
The Oracle2 ARM64 VPS stores the Git source and implements the application;
the Windows 11 machine compiles the native x64 EXEs using its built-in
\`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe\`.

PowerShell:

\`\`\`powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\dist\PeripheralBatteryLite.Probe.exe --self-test
.\dist\PeripheralBatteryLite.Probe.exe --cache-show
.\dist\PeripheralBatteryLite.Probe.exe --settings-show
.\dist\PeripheralBatteryLite.exe
\`\`\`

A GitHub Actions Windows job builds the portable binary and runs headless
parser, registry, scheduling, settings and cache tests on each push/PR.

### Diagnostic commands

\`PeripheralBatteryLite.Probe.exe\` supports:

- \`--self-test\`: protocol parsers, registry, scheduler, persistence tests
- \`--scheduler-self-test\`, \`--cache-self-test\`: isolated tests
- \`--cache-show\`, \`--settings-show\`: read local state only
- \`--probe\`: enumerate supported HID descriptors (read-only)
- \`--once\`: query each connected device without touching cache
- \`--refresh-cache\`: query and persist battery values
- \`--mouse-only\`, \`--keyboard-only\`, \`--headset-only\`: targeted reads
- \`--render-preview\`: render a UI preview PNG into Windows Temp (no HID reads)

## Architecture

\`\`\`text
WinForms tray / BatteryPanel / SettingsForm
    |
    +--> AppPreferencesStore ---- settings.json
    +--> BatterySnapshotStore --- battery-cache.json
    +--> RawInputActivityTracker (input headers only)
    +--> RefreshPolicy / BatteryCoordinator
                 |
                 +--> ProviderRegistry (IBatteryProvider)
                          |
                          +--> ATK F1V3 provider
                          +--> VXE V87 provider
                          +--> SteelSeries Nova 5 provider
                          +--> optional trusted DLL providers
                                      |
                                      +--> HidWindows / SetupAPI / USB HID
\`\`\`

Queries are allowed only as a result of an explicit refresh or an eligible
opted-in timer event. The device protocols send known battery/status commands,
not firmware, pairing, key mapping, lighting or EQ commands.

## Protocol research references

- https://github.com/papelblume/atk-tool-plus
- https://github.com/Fan4Metal/mouse_tray
- https://github.com/duypham312/feker-keepalive
- https://github.com/JKWTCN/BatteryMonitor
- https://github.com/SHISGAY114514/pulse-wireless-battery
- https://github.com/vonempalmeolmos/arctisbattery

The application code is an independent C# implementation based on documented
HID wire formats and real-device readings. It does not ship original third-party
applications or their binaries.
