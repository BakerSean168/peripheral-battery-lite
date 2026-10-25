# BatteryMonitor performance study: source audit and short Windows benchmark

Date: **2026-10-25**. Upstream: [JKWTCN/BatteryMonitor](https://github.com/JKWTCN/BatteryMonitor), release **v1.5.261019**, source revision `50f628c`. Upstream uses **C++20 / Qt 6 Widgets / HIDAPI / QuickJS**, not Qt Quick/QML. This was a temporary, non-installed benchmark, not an adoption decision.

## Method and limitations

- Both applications were sampled on **the same Windows 11 workstation** with `Get-Process` in PowerShell; the window of each application was hidden/minimized.
- Peripheral Battery Lite was an already-running instance with **automatic battery queries OFF**.
- BatteryMonitor ran as a temporary extraction of the official GitHub ZIP (`BatteryMonitor.zip`, **26,775,161 bytes**, **69,300,317 bytes unpacked**). Its EXE SHA-256 was `1713C994B5DF250F600CAC90903EC699392C7A590C200486DF8F8E23D9826BCB`. Authenticode signature status: `NotSigned`.
- Process data: working set (`WorkingSet64`), committed private bytes (`PrivateMemorySize64`), number of threads, and cumulative CPU time (`CPU`). Note that the two memory metrics are **different**; working set includes resident shared DLL pages.
- BatteryMonitor's first launch had no preexisting user registry/preferences or data directory. The source default interval is 10 seconds; however after the first run the app's registry showed `RefreshInterval=60000`. The actual scheduling throughout that first run **was not independently traced**, so the measured differences should **not** be attributed solely to 10-second polling.
- This is a single short comparison, not matched feature workload, leak test, cross-version benchmark, or battery drain measurement.
- No modifications were made to Peripheral Battery Lite's EXE, settings or cache.

## Measured results (first run, ~60 seconds)

| Metric | BatteryMonitor | Peripheral Battery Lite |
| --- | ---: | ---: |
| Working set at ~2s | 57.87 MiB | 44.44 MiB |
| Working set at ~25s | 71.64 MiB | 44.46 MiB |
| Working set at ~60s | **74.10 MiB** | **44.56 MiB** |
| Private commit at ~60s | **26.37 MiB** | **31.57 MiB** |
| Threads at ~60s | **31** | **6** |
| Additional CPU time from t~2s to t~60s | **2.828 seconds** | **0.047 seconds** |

Workloads are intentionally different: BatteryMonitor runs multiple native protocol providers, BLE listeners and plugins; our tray remains passive until a manual refresh.

## Second run: longer interval, not a proof of improvement

BatteryMonitor was restarted with its QSettings `RefreshInterval=60000` (60s):

| Elapsed | Working set | Private commit | Threads | Cumulative process CPU |
| --- | ---: | ---: | ---: | ---: |
| 3s | 56.68 MiB | 18.62 MiB | 30 | 0.500s |
| 15s | 57.04 MiB | 18.67 MiB | 30 | 1.625s |
| 30s | 57.23 MiB | 19.50 MiB | 31 | 3.406s |
| 45s | 57.49 MiB | 19.04 MiB | 28 | 5.906s |

This second run's working set was smaller, **but its CPU time was higher**. The two runs had different startup/runtime conditions, so neither memory changes nor CPU changes can be assigned causally to the interval setting. Continuous active BLE scanning and event-driven refresh may contribute even when the periodic timer is slower; this remains a hypothesis, not a profiled root cause.

## Performance mechanisms actually found in upstream source

1. **Per-Provider worker isolation**: `src/core/BatteryManager.cpp` starts one `QThread` per built-in native provider (13 built-in providers) and each enabled JS plugin. A slow provider cannot freeze others, but the many threads increase per-process resources. An in-flight/pending flag coalesces multiple refresh requests.
2. **Change-only UI updates**: `mainwindow.cpp::onDevicesUpdated` uses `sameDisplayContent` and avoids reconstructing the table, details and tray icon when the displayed state is unchanged. RSSI updates get a narrower path.
3. **HID interface-path caching**: `src/providers/hid/VgnHidProvider.cpp` remembers the previously responsive HID interface. A sleeping receiver avoids rescanning every interface each cycle; a removed path triggers rediscovery.
4. **One shared BLE advertisement watcher**: `src/providers/bluetooth/SharedBleWatcher.cpp` multiplexes AirPods and Xiaomi callbacks. It uses **Active** BLE scanning without a manufacturer filter, which may consume resources even when no matching battery device is present. Its actual impact requires an ETW/WPA profile.
5. **Change-only SQLite recording**: `src/history/BatteryHistoryStore.cpp` stores a row when battery/connection state changes, otherwise a heartbeat no more frequently than every five minutes. Records within a snapshot are written inside one transaction; samples are indexed by device and timestamp. This avoids writing the entire state on every timer tick.
6. **Bounded JS plugin runtime**: `src/scripting/QuickjsEngine.h` caps **each** QuickJS runtime at 16 MiB and the stack at 256 KiB, with execution deadline support. This does not mean the complete application is limited to 16 MiB.
7. **Change-only summary logging**: `BatteryManager.cpp::logSummaryIfChanged` suppresses repeated INFO logs when device signatures are unchanged.
8. **Qt Widgets rather than a browser UI**: `CMakeLists.txt` links `Qt6::Widgets` and does not use Qt Quick/QML. A full Qt release still brings many shared DLLs. The distributed software OpenGL fallback DLL occupies ~20.6 MB on disk but should not be assumed resident in RAM.

## Optimization opportunities inferred from the source, not yet implemented

1. **Add manual-only, 30m/1h/2h/5h and per-device activity gates** if pursuing the target users' sleep-sensitive workflow. Upstream's UI only exposes 5/10/30/60-second polling, and its manager performs an immediate refresh at startup. Reducing radio/HID requests matters more than squeezing a few megabytes from Qt.
2. **Disable unused native providers and BLE watchers** in user settings. Otherwise 13 native provider worker threads and at least one active BLE listener can run even on machines that only need two or three HID receivers.
3. **Use lazy provider creation or a small bounded worker pool**, rather than reserving one dedicated Qt thread per Provider on every installation, while preserving timeouts and per-provider failure isolation.
4. **Avoid automatically scheduling a pending refresh immediately after a slow provider completes** when the previous interval already elapsed; enforce a minimum cooldown or exponential backoff for unavailable interfaces.
5. **Lazy-initialize heavyweight UI/history/plugin subsystems** when launching directly into tray mode. Memory savings are speculative until measured.
6. **Retain change-only UI refresh, cached HID path selection, shared BLE transport, and change-only history writes**; these are credible patterns worth reusing.
7. **Measure with ETW/WPA and repeat trials** before attributing CPU/energy use to Qt, BLE, or poll intervals. Count actual HID writes, BLE scan events, wakeups and SQLite transactions.

## Implications for Peripheral Battery Lite

For only three supported receivers, the existing **manual-first** C# tray is lighter in working set on this machine and has less idle activity. It is not automatically superior for broad community device support. If extensibility becomes the core goal, upstream's QuickJS plugin host remains a strong option: port our three validated protocols to JS **and re-test against real hardware**, then decide whether to contribute or fork.

**No adoption or rewrite was performed.** The upstream benchmark ran temporarily and the test process was terminated.

## Source references

- [Manager workers and refresh scheduling](https://github.com/JKWTCN/BatteryMonitor/blob/v1.5.261019/src/core/BatteryManager.cpp)
- [Conditional UI rebuild](https://github.com/JKWTCN/BatteryMonitor/blob/v1.5.261019/mainwindow.cpp)
- [VGN last-good HID path](https://github.com/JKWTCN/BatteryMonitor/blob/v1.5.261019/src/providers/hid/VgnHidProvider.cpp)
- [Shared BLE watcher](https://github.com/JKWTCN/BatteryMonitor/blob/v1.5.261019/src/providers/bluetooth/SharedBleWatcher.cpp)
- [SQLite history](https://github.com/JKWTCN/BatteryMonitor/blob/v1.5.261019/src/history/BatteryHistoryStore.cpp)
- [QuickJS limits](https://github.com/JKWTCN/BatteryMonitor/blob/v1.5.261019/src/scripting/QuickjsEngine.h)
- [Release v1.5.261019](https://github.com/JKWTCN/BatteryMonitor/releases/tag/v1.5.261019)
