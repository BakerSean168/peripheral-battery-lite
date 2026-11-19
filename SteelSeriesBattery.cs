using System;
using System.Collections.Generic;
using System.Linq;

namespace PeripheralBatteryLite
{
    internal static class SteelSeriesBattery
    {
        // Nova 5 USB dongle: 1038:2232, vendor collection FFC0:0001.
        // Existing read-only HID query: 00 B0 + padding. HidWindows may retain
        // report ID 00 as the first byte, while hidapi sometimes strips it.
        //
        // Nova 5 status response (after the 0xB0 command byte):
        //   [1] = link state (0x02 means headset offline)
        //   [3] = battery percent
        //   [4] = charging flag (0x01 charging, 0x03 discharging)
        // The receiver can answer while the headset is powered off; therefore
        // its 0% placeholder MUST NOT be mistaken for a measured empty battery.
        //
        // Confirmed by HeadsetControl device descriptions and Linux SteelSeries
        // driver patch (documented per-model; do not generalize to Nova 7).
        private static bool TryParseStatus(byte[] response, out int percent,
            out bool? charging, out bool offline)
        {
            percent = 0;
            charging = null;
            offline = false;
            if (response == null || response.Length < 5) return false;

            int offset = response[0] == 0xB0 ? 0 :
                (response.Length >= 6 && response[0] == 0 && response[1] == 0xB0 ? 1 : -1);
            if (offset < 0 || response.Length < offset + 5) return false;

            // Status must be examined before charge/value validation: an
            // offline dongle can return placeholder or stale payload bytes.
            if (response[offset + 1] == 0x02)
            {
                offline = true;
                return true;
            }

            int value = response[offset + 3];
            byte chargeCode = response[offset + 4];
            if (value > 100 || (chargeCode != 1 && chargeCode != 3)) return false;
            percent = value;
            charging = chargeCode == 1;
            // Genuine zero is still valid when the link is not marked offline.
            return true;
        }

        // The public parser accepts usable battery readings only.
        public static bool Parse(byte[] response, out int percent, out bool? charging)
        {
            bool offline;
            return TryParseStatus(response, out percent, out charging, out offline) && !offline;
        }

        public static BatteryReading Read(List<HidEndpoint> devices)
        {
            var reading = new BatteryReading
            {
                Name = "SteelSeries Arctis Nova 5", Vid = 0x1038, Pid = 0x2232,
                Connection = "2.4G receiver", Status = "Not detected", Detail = ""
            };
            var candidates = devices.Where(d => d.Vid == reading.Vid && d.Pid == reading.Pid).ToList();
            if (candidates.Count == 0) return reading;
            var endpoints = candidates.Where(d =>
                d.UsagePage == 0xFFC0 && d.Usage == 0x0001 &&
                d.InputLength >= 65 && d.OutputLength == 65).ToList();

            if (endpoints.Count == 0)
            {
                reading.Status = "Headset battery interface not found";
                reading.Detail = string.Join("; ", candidates.Select(d => d.ToString()).ToArray());
                return reading;
            }

            byte[] request = new byte[65];
            request[0] = 0x00;
            request[1] = 0xB0; // existing read-only battery/status query
            foreach (var ep in endpoints)
            {
                try
                {
                    int level = 0;
                    bool? charging = null;
                    bool offline = false;
                    byte[] reply = HidWindows.Exchange(ep, request,
                        result => TryParseStatus(result, out level, out charging, out offline), 850);
                    if (reply == null)
                    {
                        reading.Status = "No valid headset battery reply";
                        continue;
                    }
                    if (offline)
                    {
                        reading.Status = "Device offline";
                        reading.Detail = "Dongle reports Nova 5 powered off or disconnected";
                        return reading; // null percent, no charging state
                    }
                    reading.Percent = level;
                    reading.Charging = charging;
                    reading.Status = "OK";
                    reading.Detail = ep.ToString();
                    return reading;
                }
                catch (Exception ex)
                {
                    reading.Status = "Headset HID read failed";
                    reading.Detail = ex.GetType().Name + ": " + ex.Message;
                }
            }
            return reading;
        }

        public static bool SelfTest()
        {
            int level;
            bool? charging;
            bool offline;
            var raw = new byte[65];
            raw[1] = 0xB0;
            raw[2] = 0x04; // not offline; explicit status for fixtures
            raw[4] = 73;
            raw[5] = 3;
            bool windows = Parse(raw, out level, out charging) &&
                level == 73 && charging == false;

            raw[4] = 47;
            raw[5] = 1;
            bool chargingTest = Parse(raw, out level, out charging) &&
                level == 47 && charging == true;

            raw[4] = 0;
            raw[5] = 3;
            bool genuineZero = Parse(raw, out level, out charging) &&
                level == 0 && charging == false && TryParseStatus(raw,
                    out level, out charging, out offline) && !offline;

            raw[2] = 0x02; // powered off, regardless of placeholder battery
            bool poweredOff = !Parse(raw, out level, out charging) &&
                TryParseStatus(raw, out level, out charging, out offline) &&
                offline && charging == null;
            raw[4] = 73;
            raw[5] = 0xFF; // placeholder values may not parse as charge code
            bool poweredOffWithGarbage = TryParseStatus(raw, out level,
                out charging, out offline) && offline && !charging.HasValue;

            raw[2] = 0x04;
            raw[4] = 255;
            raw[5] = 3;
            bool invalidBattery = !Parse(raw, out level, out charging);

            raw[4] = 50;
            raw[5] = 0xFF;
            bool invalidCharging = !Parse(raw, out level, out charging);

            var compact = new byte[] { 0xB0, 0x04, 0x00, 25, 3 };
            bool compactFrame = Parse(compact, out level, out charging) &&
                level == 25 && charging == false;
            compact[1] = 0x02;
            bool compactOffline = !Parse(compact, out level, out charging);

            var wrong = new byte[65];
            wrong[1] = 0xC0;
            bool invalidHeader = !Parse(wrong, out level, out charging);

            return windows && chargingTest && genuineZero && poweredOff &&
                poweredOffWithGarbage && invalidBattery && invalidCharging &&
                compactFrame && compactOffline && invalidHeader;
        }
    }
}
