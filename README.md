# Peripheral Battery Lite v0.4.0

Portable Windows 10/11 tray utility for three wireless peripherals.
Runs on the Windows built-in .NET Framework 4.x, without Electron or an extra background service.

## Manual refresh only

**No HID battery queries on app startup, when opening the panel, while hovering, or in the background. No timer exists.**
On startup, the tray icon and popup display only saved values from the previous successful manual query.

How to use:
1. Run PeripheralBatteryLite.exe.
2. Left-click the tray icon to open the floating panel.
3. Click the `刷新` button in the bottom-right of the panel, or right-click the tray icon and choose `立即刷新`.
4. A single manual refresh reads ATK F1V3, VXE V87 and SteelSeries Nova 5, and updates the local cache.
5. Each device displays its last successful battery value and local query time (MM-dd HH:mm:ss).
6. If a query fails, the last successful battery and timestamp are kept; the panel tells you that the latest attempt failed.

## Persistent cache

Windows file: `%LOCALAPPDATA%\PeripheralBatteryLite\battery-cache.json`

- JSON includes each device ID, percentage, charging flag, last query attempt in ISO UTC,
  last successful query in ISO UTC, and the last attempt success flag.
- The app never creates or modifies the cache until the user explicitly requests a refresh.
- Values remain available after closing or restarting the program or rebooting Windows.
- Stale cached values are NOT live battery telemetry.
- The cache is written atomically; a corrupt cache is ignored without any device communication.

## Live-tested interfaces (Oct 24, 2026)

| Device | HID VID:PID | Protocol |
| --- | --- | --- |
| ATK F1V3 mouse | 373B:1303 | 17-byte Nordic/Compx read-only report |
| VXE V87 keyboard in 2.4G mode | 320F:5088 | FF1C:0092, 64-byte status/battery read |
| SteelSeries Arctis Nova 5 headset | 1038:2232 | FFC0:0001, B0 battery report |

Most battery queries are fast (hundreds of milliseconds), but some ATK HID reads
have taken several seconds or stalled temporarily. Manual refresh is asynchronous
so the UI is not meant to freeze. The per-device queries are still sequential.

## Diagnostics (explicit manual invocation only)

Use PeripheralBatteryLite.Probe.exe with:

    --self-test         validate battery parsers (does not query devices)
    --cache-self-test   validate file persistence without querying devices
    --cache-show        print saved values/timestamps (does not query devices)
    --refresh-cache     one explicit battery query for all devices, save to cache
    --once              read all three, but do not update the cache
    --mouse-only        read just the mouse
    --keyboard-only     read just the keyboard
    --headset-only      read just the headset
    --probe             list matching HID descriptors

Windows PowerShell build:
    powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

## Sleep and safety

- No background polling, wake or keep-alive behavior when idle.
- Manual refresh may briefly wake a sleeping device: it sends a known read-only query.
- No changes to keyboard mapping, firmware, LED, pairing, mouse DPI, audio EQ, or system sleep settings.
- No cloud service, networking or telemetry.
- Startup-with-Windows is OFF unless explicitly enabled from the tray menu.

Public protocol references:
- https://github.com/duypham312/feker-keepalive
- https://github.com/JKWTCN/BatteryMonitor
- https://github.com/SHISGAY114514/pulse-wireless-battery
- https://github.com/papelblume/atk-tool-plus
- https://github.com/vonempalmeolmos/arctisbattery

Source folder: /home/baker/projects/peripheral-battery-lite (WSL).
