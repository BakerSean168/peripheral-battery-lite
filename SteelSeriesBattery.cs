using System;
using System.Collections.Generic;
using System.Linq;

namespace PeripheralBatteryLite
{
    internal static class SteelSeriesBattery
    {
        // Arctis Nova 5 wireless dongle: USB 1038:2232, vendor usage FFC0:0001.
        // The published Arctis Battery Monitor implements a read-only battery query:
        // 00 B0 ... zero pad, response command B0, battery field index 3, charge index 4
        // (the Windows native HID read path may retain the extra ReportID 00 prefix).
        public static bool Parse(byte[] response, out int percent, out bool? charging)
        {
            percent = 0;
            charging = null;
            if (response == null || response.Length < 5) return false;

            // Canonical HID payload may start with B0, or Windows raw report may start 00 B0.
            int offset = response[0] == 0xB0 ? 0 :
                (response.Length >= 6 && response[0] == 0 && response[1] == 0xB0 ? 1 : -1);
            if (offset < 0 || response.Length < offset + 5) return false;
            int value = response[offset + 3];
            byte chargeCode = response[offset + 4];

            // Reject invalid frames instead of silently clamping battery percentage.
            if (value > 100 || chargeCode != 1 && chargeCode != 3) return false;
            percent = value;
            charging = chargeCode == 1;
            return true;
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

            // Only the expected vendor collection receives the documented battery query.
            byte[] request = new byte[65];
            request[0] = 0x00; // report ID
            request[1] = 0xB0; // read battery
            foreach (var ep in endpoints)
            {
                try
                {
                    int level = 0;
                    bool? charging = null;
                    byte[] reply = HidWindows.Exchange(ep, request, result => Parse(result, out level, out charging), 850);
                    if (reply == null)
                    {
                        reading.Status = "No valid headset battery reply";
                        continue;
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
            var raw = new byte[65]; raw[1] = 0xB0; raw[4] = 73; raw[5] = 3;
            bool windows = Parse(raw, out level, out charging) && level == 73 && charging == false;
            raw[4] = 47; raw[5] = 1;
            bool chargingTest = Parse(raw, out level, out charging) && level == 47 && charging == true;
            raw[4] = 255;
            bool rejectsBad = !Parse(raw, out level, out charging);
            return windows && chargingTest && rejectsBad;
        }
    }
}
