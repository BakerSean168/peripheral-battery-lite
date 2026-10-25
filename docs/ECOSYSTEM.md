# Existing open-source battery monitors vs building a new host

Research snapshot: **2026-10-25**. This document distinguishes *extensible source code* from a *runtime plugin system*. Compatibility means the specific device's battery query was validated, **not** that a vendor ID is present in a table.

## Comparison

| Project | Desktop stack | How to add a new device | Plugin isolation | Rebuild main app? | Fit for this project |
| --- | --- | --- | --- | --- | --- |
| [JKWTCN/BatteryMonitor](https://github.com/JKWTCN/BatteryMonitor) | Qt 6 / C++ / hidapi | **Drop-in JavaScript** implementing `VendorId()`, `ProductId()`, `GetBattery()`; custom native providers for non-HID cases | **QuickJS sandbox**: no file/network/process APIs; HID commands still potentially risky | **No**, for JS HID plugins | **Strongest general-purpose host** |
| [Device Battery Tray](https://github.com/Tsifrokayf/Device-Battery-Tray) | Python / PyInstaller / pystray / tkinter / hidapi | Extend supported HID driver/registry code; no equivalent QuickJS host documented | No runtime JS sandbox documented | Generally yes, to ship a new custom protocol | Good plug-and-play tray/widgets; ATK protocol family |
| [PeriTray](https://github.com/oneday5799/PeriTray) | Tauri 2 / Rust / WebView | User JSON can add VID/PID metadata; a **new wire protocol** requires a Rust `BatteryDriver` implementation | Rust code compiled in app; JSON is not a protocol plugin | Yes, for a new protocol | Good multi-purpose peripherals/volume dashboard |
| [DeskVolt](https://github.com/risenxxx/DeskVolt) | Rust / desktop widget | Implement `Device` trait and register discovery | No external runtime plugin host documented | Yes | Good small native widget, limited out-of-box devices |
| [TrafficMonitor + PeripheralBatteryPlugin](https://github.com/Ariazonaa/PeripheralBatteryPlugin) | Existing TrafficMonitor + C++ DLL | Host accepts plugins; new proprietary battery protocols require new or updated compiled plugin DLL | Native DLL, no sandbox | TrafficMonitor itself: no; DLL: must compile | Flexible if you already run TrafficMonitor |
| **Peripheral Battery Lite** (this repo) | C# / WinForms / .NET Framework | `IBatteryProvider` native **DLL plugin**, referenced against Contracts DLL | **No sandbox**; external DLLs disabled by default | No, for a compiled external DLL | Small, targeted, validated against 3 actual devices |

### Important distinction

An architecture with `Provider` interfaces is not automatically a user-installable plugin platform. Runtime extensibility requires a stable loading mechanism and compatibility contract. **A programmable, sandboxed JS host is easier for ordinary users and safer than our current native-DLL interface**, though unsafe device writes remain possible.

## Closest upstream: JKWTCN/BatteryMonitor

Documentation: [JavaScript plugin guide (Chinese)](https://github.com/JKWTCN/BatteryMonitor/blob/master/docs/js-plugin.md) / [English](https://github.com/JKWTCN/BatteryMonitor/blob/master/docs/js-plugin-en.md).

It provides a full host:

- Device discovery by USB VID/PID, with candidate HID interfaces.
- `device.write/read` and feature report operations, with report IDs, timeouts and per-round limits.
- A QuickJS sandbox that omits filesystem, network and process APIs.
- User-managed plugin files under `plugins/`, including a plugin manager and plugins fetched from its repository.
- Native providers for Bluetooth, AirPods, Xiaomi buds, gamepads, Logitech, Razer, VGN-family peripherals, plus history/notifications/WebSocket.
- An MIT license.

**Current compatibility gap for our exact three devices:** BatteryMonitor documents verified ATK V75X and a VGN-family 2.4G receiver, but does **not** list our precise combination (ATK F1V3 + VXE V87 + SteelSeries Nova 5) as verified. Its JS HID plugin API is a plausible way to port these queries, but success needs real-device testing. Its default JS `device.read` timeout is capped at 500 ms per call; retry design should be tested against our occasional slow ATK replies. We have **not benchmarked its runtime memory use** against our WinForms application.

### Why we originally built Peripheral Battery Lite

The initial work optimized for a very narrow, measurable goal: **read three specific attached receivers on this Windows 11 machine** and show their battery in a small tray popup. In that context, three independently verified HID message parsers + a 38 MB-class measured Windows process + a manual refresh cache were easy to validate.

The initial architecture survey was **incomplete**: it did not adequately account for BatteryMonitor's existing JavaScript plugin host. It would have been more efficient to compare that host *before* committing to a new plugin platform. Our code is nonetheless useful as a small device-specific implementation and protocol test fixture.

## Decision guidance

**If the objective stays "three local devices, low resource use, controllable wakeups":** maintain Peripheral Battery Lite as the minimal personal utility. Do not grow its UI/SDK indiscriminately.

**If the objective changes to "let anyone install arbitrary device protocols as plugins":** evaluate contributing our three validated protocols to BatteryMonitor as JS plugins, potentially adopting/forking that host. It already offers the core/UI/plugin sandbox and much broader hardware coverage. Starting a parallel JS runtime from scratch would duplicate that work.

### Evidence needed before any migration

1. Install a clean BatteryMonitor build, measure idle memory/CPU (same Windows machine, same conditions) and compare with our application.
2. Port the three battery reads into JS prototypes, including report ID handling, signature validation and timeouts.
3. Verify battery levels on the actual F1V3, V87 and Nova 5 dongles—not merely matching USB IDs.
4. Check whether it can provide **manual-only mode**, *no startup reads*, portable same-folder settings, and sleep-friendly refresh without regression.
5. Check GUI usability, ZIP portability, plugin sandbox safety and ability to keep all three device values cached.
6. Compare maintenance cost and project goals. Only migrate when demonstrably better.

**No rewrite/migration is approved by this document.** It records an informed alternative and gives a test plan.

## References

- https://github.com/JKWTCN/BatteryMonitor
- https://github.com/JKWTCN/BatteryMonitor/blob/master/docs/js-plugin-en.md
- https://github.com/Tsifrokayf/Device-Battery-Tray
- https://github.com/oneday5799/PeriTray
- https://github.com/risenxxx/DeskVolt
- https://github.com/Ariazonaa/PeripheralBatteryPlugin
