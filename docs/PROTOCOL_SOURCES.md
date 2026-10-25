# Protocol research, acknowledgments, and license notes

The implementation of Peripheral Battery Lite is written in C#. It does not ship upstream third-party applications or embedded copies of their source files. Publicly available device IDs, protocol packet layouts, response fields, and hardware observations were used as references.

A **protocol fact** (for example, a USB VID:PID, command byte, or data offset) is not the same thing as copying a project's implementation. The copyright status of a particular derived implementation depends on the actual copied expression and applicable law. If external source is copied into this repository in the future, perform a separate license review first.

## Projects consulted

| Project | Relevant research | Upstream license (verified 2026-10-25) |
| --- | --- | --- |
| [papelblume/atk-tool-plus](https://github.com/papelblume/atk-tool-plus) | ATK device catalog, Nordic/Compx battery message layouts and checksums | MIT |
| [Fan4Metal/mouse_tray](https://github.com/Fan4Metal/mouse_tray) | ATK and other HID family implementation notes | Check upstream LICENSE before reusing code |
| [duypham312/feker-keepalive](https://github.com/duypham312/feker-keepalive) | 320F:5088 / FF1C:0092, 64-byte Weisheng-family status query; **same command can act as keep-alive** | GPL-3.0 |
| [JKWTCN/BatteryMonitor](https://github.com/JKWTCN/BatteryMonitor) | Provider architecture, VGN-related protocol family, modular QuickJS extensions | MIT |
| [SHISGAY114514/pulse-wireless-battery](https://github.com/SHISGAY114514/pulse-wireless-battery) | Wireless receiver model ID mappings and battery decoding | MIT |
| [vonempalmeolmos/arctisbattery](https://github.com/vonempalmeolmos/arctisbattery) | SteelSeries Arctis HID battery queries | GPL-3.0 |
| [Tsifrokayf/Device-Battery-Tray](https://github.com/Tsifrokayf/Device-Battery-Tray) | Windows peripheral battery dashboard patterns | Check upstream LICENSE before reusing code |

**Important GPL note:** Some protocol sources above are GPL-3.0 licensed. The MIT License in this repository applies to *our original implementation*, not to those projects. Do **not** copy GPL-3.0 implementation code, creative structures or other copyrightable expression into MIT-licensed files without resolving license obligations. If a contributor needs upstream GPL code rather than protocol facts, raise a dedicated design/license issue before copying.

This is a provenance record and not a legal opinion.

## Hardware verification methodology

1. Enumerate USB HID interfaces by VID, PID, usage page, usage and report lengths.
2. Identify a documented battery/status read operation; do not guess firmware or pairing commands.
3. Match the response signature and validate the battery value range.
4. Test with the physical receiver on Windows; report what was actually observed.
5. Document that USB enumeration **does not** establish battery-query compatibility.
6. Prefer manual refresh or passive activity gating to avoid keeping a sleeping device awake.
7. Include at least one failed-response parser test and a no-device code path.

Verified local devices include ATK F1V3 (373B:1303), VXE V87 (320F:5088) and SteelSeries Arctis Nova 5 (1038:2232). **Other models sharing a VID:PID are not automatically verified.**

## Trademark notice

ATK, VXE, VGN and SteelSeries brand/product names are used only for hardware compatibility identification. This project is not affiliated with or endorsed by those manufacturers.
