# Battery Analytics, Notifications, and Hardware Telemetry

Research and implementation snapshot: **2026-11-19** (v0.8.1 offline fix).

Peripheral Battery Lite remains a device-specific Windows WinForms tray. No
new or undocumented HID commands were added for these features. All telemetry
continues to run in per-device, time-limited child processes.

## Features in this release

### Scheduled refresh and notification

- Polling choices: off (default), 30 min, 1 h, 2 h, **4 h**, 5 h.
- Each device's mode: inherit the global policy, recent keyboard/mouse
  activity only, attempt regardless of activity, or manual-only.
- An audio dongle cannot prove its headset is awake. With activity-only
  protection the Nova 5 is deliberately skipped. Opting into "always" may
  wake or delay device sleep. The PC itself is never woken for polling.
- Each device: alert enabled/disabled (disabled by default) and a threshold
  from 5% to 95% (45% default).
- A successful new battery reading below or equal to its threshold and
  **not charging** queues a Windows NotifyIcon balloon warning. Offline and
  failed replies do not count as successful battery readings.
- The same discharge episode alerts **once only**. It resets after a
  successful reading at or above threshold + 5 percentage points.
- A quiet-hours window suppresses delivery but keeps pending alerts. Pending
  warnings must still have a successful reading no older than 24 h.
- The device detail form offers a persistent 24-hour snooze, plus a history
  of the last 200 submitted notifications across devices. The Windows OS
  may hide balloons under Do Not Disturb; log entries mean the notification
  was submitted to Windows, not that a human necessarily saw it.
- The notification state is kept separately in battery-alerts.json and
  survives app restarts. The history is of **submissions to Windows**, not
  verified delivery; use the tray menu "测试 Windows 提醒" to test banner visibility.
- Startup and tray hover do not query HID. An explicit left-click refresh
  continues to work, even with scheduled polling disabled.

### History and analytics

- Successful readings are persisted in battery-history.json in portable
  mode (or LocalAppData without portable.flag).
- Identical readings within 30 minutes are coalesced. Changes to charging
  or reported voltage may generate an additional sample. History retention
  choices are 30/90/180/365 days (180 default); file also has a hard cap
  of 15,000 rows. Storage uses atomic replacements.
- Device detail: 7/30/90/180-day charge curves, an editable low-battery
  threshold, CSV (UTF-8 with BOM) export, nickname, nominal mAh field, and
  rolling discharge estimates.
- A trend is shown only after at least three valid readings across at least
  12 hours, with two actual drops and at least 2% total observed decrease.
  Charging, increases and gaps over 72 hours are excluded.
- Estimated % per day uses measured percentage drop divided by the elapsed
  wall-clock time across usable intervals, **not** active-use time.
- Time to zero and time to the configured threshold are rough projections;
  usage profile, sleep, charge cycles and the battery gauge may change.
- When a **user-supplied nominal capacity** is known, a simple estimated
  remaining mAh and average equivalent mA can be computed. Neither is
  directly measured battery current, true usable capacity, health or cycle count.
- No nominal capacity is prefilled automatically; variant differences matter.

### Tray ergonomics

- Set the tray indicator to F1V3, V87, Nova 5 or the lowest **recent**
  successful reading. In lowest mode, samples older than 24 hours and
  failed recent reads do not qualify.
- The ring changes tint when the displayed battery reaches that device's
  configured threshold.
- Click a card to open device details or use the tray context submenu.
- The previous button-based/manual refresh and cache freshness indicators
  remain available.

## Exact hardware research

The existence of a VID/PID is never treated as evidence that every telemetry
field or protocol variant works. These results distinguish a published
protocol mapping from a live read on the owner's exact receiver.

| Device | Existing verified read | Additional info | Capacity research |
| --- | --- | --- | --- |
| ATK F1V3 / 373B:1303 | 17-byte 0x08/0x04 battery %, charging | Compx battery reply bytes [8..9] are big-endian mV; code now extracts only plausible 2500–4800 mV and carries it through cache/history/CSV | Official ATK F1 V3 lists **300 mAh for Ultimate+** and **200 mAh for Extreme+**. User must specify which. |
| VXE V87 / 320F:5088 | 64-byte status command 0x1A, %, charging | No proven voltage/current/mAh field in our confirmed response | V87 guides list **4000 mAh regular** and **8000 mAh Pro**; exact unit variant must be checked. |
| SteelSeries Arctis Nova 5 / 1038:2232 | 0xB0 status query, %, charging, **receiver link-state byte (offline = 0x02)** | The receiver may report an offline placeholder `0%`; the updated parser ignores its battery bytes and keeps last valid data. No confirmed voltage/current/mAh. | SteelSeries advertises **up to 60 h runtime**, not a definitive cell capacity. Retailer spec may mention 620 mAh but is not verified for this unit. |

### Source material

- [ATK F1 V3 specifications: capacity depends on variant](https://www.atk.store/products/atk-blazing-sky-f1-v3-series-esports-wireless-mouse)
- [V87/V87 Pro manual: 4000/8000 mAh](https://manuals.plus/ae/1005009578353255)
- [Nova 5 product page: battery runtime](https://steelseries.com/gaming-headsets/arctis-nova-5)
- [Compx/Pulsar 17-byte report, mV mapping, hardware example](https://github.com/HeyOkay/HaloBattery/issues/164)
- [Compx byte positions and charger flag](https://github.com/HeyOkay/HaloBattery/issues/82)
- [SteelSeries status 0xB0 and model-specific layout caveat](https://github.com/Ven0m0/steelseriesgg-rs/blob/main/docs/development/devices.md)
- [Windows NotifyIcon balloons](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.notifyicon?view=netframework-4.8)
- [Linux SteelSeries patch: Nova 5 `data[1] == 0x02` means offline](https://lore-kernel.gnuweeb.org/lkml/20260112041941.40531-5-srimanachanta%40gmail.com/)
- [Per-model Nova 5 state mapping (Win32 reports include a leading Report ID)](https://github.com/o0Zz/PeripheralBatteryMonitor/blob/master/src/PeripheralBatteryMonitor.Core/Providers/SteelSeries/SteelSeriesBatteryProvider.cs)

### Validation status and remaining hardware boundary

- Unit tests verify Compx parsing with 3732 mV from a checksum-valid
  synthetic frame, rejection of impossible voltage values, and safe
  transport from the child probe to the tray.
- **F1V3 voltage verified subsequently on the owner’s hardware.** Initial
  attempts timed out, but later cache/history entries contain valid responses
  with voltages 4113 mV, 4108 mV and 4095 mV, as well as percentages 75%/70%.
  The code still returns unknown on failed reads rather than fabricating data.
- Do not claim measured milliamps, battery health, charge cycles, or true
  remaining mAh. The validated commands return percent, charge status,
  and potentially F1V3 voltage; they do not provide coulomb counter or
  current measurements.
- To investigate other telemetry fields, use vendor documentation, read-only
  USB packet captures of vendor software, or trusted published captures
  before changing report parsers. Never fuzz commands, write firmware,
  change pairing, or send experimental device settings through this app.
- The Nova 5 receiver was physically checked in the **off state** on
  2026-11-19: a read-only `--headset-only` command reported `Device offline`
  with `percent=null` and `charging=null` instead of a fake 0%.
  Distinguish state (0x02) from the level byte; an online 0% remains a valid
  reading. The cache reports `Device offline` and displays its last valid
  level with a stale label, while the icon selection excludes it from
  current minimum-battery ranking. No offline sample or alert is generated.
- Running .\dist\PeripheralBatteryLite.Probe.exe --mouse-only performs the
  existing read-only mouse query; do not run it repeatedly just to fill
  history. Normal UI refresh should handle safe timeout and recover later.

## Verification commands

Windows PowerShell (run from the source checkout):

```powershell
.\build.ps1
.\dist\PeripheralBatteryLite.Probe.exe --self-test
.\dist\PeripheralBatteryLite.Probe.exe --analysis-self-test
.\dist\PeripheralBatteryLite.Probe.exe --headset-offline-self-test
.\dist\PeripheralBatteryLite.Probe.exe --scheduler-self-test
.\dist\PeripheralBatteryLite.Probe.exe --storage-paths
.\dist\PeripheralBatteryLite.Probe.exe --history-show
.\dist\PeripheralBatteryLite.Probe.exe --render-settings
.\dist\PeripheralBatteryLite.Probe.exe --render-detail "ATK F1V3"
```

The self-tests and preview operations do not send HID queries. Actual
battery query commands do and should be deliberately invoked only when needed.
