# Security and hardware-safety policy

This utility reads battery/status data over proprietary USB HID interfaces. Some HID commands that appear read-only can still disturb device sleep; malformed vendor commands can have wider effects.

- Default behavior is manual-refresh-only; no automatic hardware queries on startup.
- Optional auto-refresh should use the documented battery queries only, with bounded timeouts.
- The built-in providers have been validated with specific physical receivers, not all VID/PID-family devices.
- External DLL plugins run with the current user's full Windows privileges. They are **disabled by default and not sandboxed**. Only install source-reviewed providers from trusted authors.
- Never package API keys, unique device paths, serial numbers or real keystrokes with diagnostic reports.
- Never use this program for firmware flashing, pairing or device configuration.

If you discover a vulnerability, prefer a private report through GitHub's **Report a vulnerability** feature if it is available for this repository; otherwise contact the owner through an appropriate private channel. Do not publish exploitation instructions or private information in a public issue.

No guarantee is made that every unverified firmware behaves identically.
