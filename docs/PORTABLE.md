# Portable installation and data migration

`portable.flag` is a file shipped beside the Windows executables. Its presence
selects **portable mode**: `settings.json` and `battery-cache.json` are read and
written in the same directory as `PeripheralBatteryLite.exe`. This applies to
the tray and to `PeripheralBatteryLite.Probe.exe`.

Suggested single-directory deployment on this workstation:

```text
D:\opt\PeripheralBatteryLite\
  PeripheralBatteryLite.exe
  PeripheralBatteryLite.Probe.exe
  PeripheralBatteryLite.Contracts.dll
  portable.flag
  settings.json
  battery-cache.json
  README.md
  docs\
  Plugins\                 (optional, only trusted DLLs)
```

## Migrate a legacy installation

1. Exit the old tray process. Do not interrupt an active refresh.
2. Download and unzip the current `PeripheralBatteryLite-*-win-x64.zip` into
   `D:\opt\PeripheralBatteryLite` (or another writable folder).
3. Confirm `portable.flag` and all three executable/DLL binaries are present.
4. If present, **copy** the old files
   `%LOCALAPPDATA%\PeripheralBatteryLite\settings.json` and
   `%LOCALAPPDATA%\PeripheralBatteryLite\battery-cache.json` to the same
   folder as the new EXE. Do not overwrite newer portable copies by accident.
5. Verify locally before removing legacy copies:

   ```powershell
   & 'D:\opt\PeripheralBatteryLite\PeripheralBatteryLite.Probe.exe' --self-test
   & 'D:\opt\PeripheralBatteryLite\PeripheralBatteryLite.Probe.exe' --storage-paths
   & 'D:\opt\PeripheralBatteryLite\PeripheralBatteryLite.Probe.exe' --settings-show
   & 'D:\opt\PeripheralBatteryLite\PeripheralBatteryLite.Probe.exe' --cache-show
   ```

6. Update the Start Menu shortcut. If Windows login autostart was enabled,
   update the `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry to
   the new EXE path. Never silently enable autostart when it was disabled.
7. Launch the new EXE and confirm the cached percentages appear. Confirm the
   destination directory is writable using a small temporary file.
8. After confirming the new installation is working and that settings/cache
   hashes match, clean up the superseded C: app files and old AppData files.

Without `portable.flag`, the program continues to use the previous
`%LOCALAPPDATA%\PeripheralBatteryLite` data location for compatibility.

Windows still owns the Start Menu shortcut and (when enabled) the autostart
registry key; these OS-level integrations cannot reside exclusively inside
the portable directory. The application's own configuration and battery data do.
