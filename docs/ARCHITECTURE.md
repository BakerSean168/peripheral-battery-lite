# Architecture and extension boundaries

## Primary goal

Provide a small native Windows battery tray for devices that expose a documented HID battery/status query, with *opt-in* polling and deterministic cache behavior. It deliberately does **not** bundle a Chromium/Tauri runtime, manufacturer SDK or cloud telemetry.

## Components

```text
UI/BatteryTrayApp.cs         tray lifecycle, user-initiated refresh, scheduled checks
UI/BatteryPanel.cs           device cards and timestamps
UI/SettingsForm.cs           user preferences and startup toggles
Core/RefreshPolicy.cs        off/30m/60m/120m/300m and activity gating
Core/BatteryCoordinator.cs   provider dispatch, per-provider failure containment
Core/IsolatedQueryHost.cs    bounded child process for HID queries
Core/AppPreferences.cs       portable/legacy settings persistence
Core/AppDataPaths.cs         storage path selection via portable.flag
BatterySnapshotStore.cs     cache: last attempt, last success, battery value
Providers/ProviderRegistry.cs dynamic discovery of built-in and optional DLL providers
Contracts/IBatteryProvider.cs provider interface for both EXEs/plugins
Contracts/HidWindows.cs      Windows HID / SetupAPI enumeration and request-response
Platform/RawInputActivityTracker.cs passive device input headers
Platform/StartupRegistration.cs     Windows CurrentUser Run key
```

## Battery query lifecycle

1. When opening the tray, read local JSON cache only.
2. Query when the user explicitly chooses refresh, or when a configured interval is due *and* an eligible device is active (if activity gating is enabled). Raw Input is **not registered in manual mode**, nor if activity gating is disabled. Changing these settings registers/unregisters the listener without restarting.
3. Select providers and start up to **three concurrent child processes**, one per selected provider, each with its own deadline. Each completed result is applied to the UI and portable JSON cache immediately; a stuck device does not hold up the other cards.
4. Use the shared `IBatteryProvider` registry in each child. Discover the HID collection, issue only its known battery/status read, validate the result.
5. Merge success and failures per provider. A failure does not overwrite the last good battery percentage or last successful timestamp. Record the latest attempt independently.
6. Display snapshots with their **last successful time** and, if the latest attempt failed, the **failure time**; do not misrepresent stale values as live. Reuse the tray icon whenever its displayed battery percentage remains unchanged.

## Plugin ABI

The stable publicly accessible C# interface is in `PeripheralBatteryLite.Contracts.dll`. Built-in providers are compiled with the application. External plugins are **user-supplied compiled DLLs** loaded by reflection from `Plugins/` only if the user enables external plugins.

Plugins are **not sandboxed**. A malicious DLL can access the filesystem, network and processes with the host user's privileges. The toggle is disabled by default. Unlike a JavaScript scripting host, a developer must compile C# code to add a provider. See [PLUGINS.md](PLUGINS.md).

### Constraints and trade-offs

- Adding a new built-in provider requires a code change and rebuild.
- Adding an **external DLL provider** does not require rebuilding the tray, but requires compiling the provider against the public contract DLL.
- Windows can report a receiver while a keyboard/headset is asleep. Dongle presence is not an awake signal.
- For headsets without a passive activity detector, activity-only auto-refresh deliberately skips querying; explicit refresh still works.
- Protocol messages can affect a device's power state even when they do not change settings.
- WinForms is small and straightforward but the UI is Windows-specific.
- For a broader, user-friendly HID plugin ecosystem, the QuickJS plugin host in [JKWTCN/BatteryMonitor](https://github.com/JKWTCN/BatteryMonitor) is a stronger existing reference; consider contributing protocols upstream rather than endlessly expanding this application.

## Test boundary

The Windows CI workflow checks compilation, parser fixtures, cache read/write, interval selection, path portability and external DLL ABI. CI does not have real ATK/VXE/SteelSeries receivers, so physical query success must be verified on a Windows host with actual hardware. Do not mark other models as compatible from a VID/PID match alone.

## Deployment

The `portable.flag` marker enables side-by-side `settings.json` and `battery-cache.json`. Without the marker, `%LOCALAPPDATA%\PeripheralBatteryLite` remains the legacy fallback. Registry-based Windows login startup and Start Menu shortcuts necessarily remain OS-level integration points; moving a portable directory may require updating those paths.
