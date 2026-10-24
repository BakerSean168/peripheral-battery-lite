using System;
using System.Collections.Generic;
using System.Linq;

namespace PeripheralBatteryLite
{
    internal static class V87KeyboardBattery
    {
        // The VXE V87 2.4G dongle is VID:PID 320F:5088, usage FF1C:0092.
        // Battery/status READ command documented by the feker-keepalive
        // project for the same 64-byte interface; confirmed with this V87.
        // Only call Read in direct response to an explicit user refresh.
        private static readonly byte[] Query = MakeQuery();

        private static byte[] MakeQuery()
        {
            var request = new byte[64];
            request[0] = 0x04;
            request[1] = 0x20;
            request[2] = 0;
            request[3] = 0x1A;
            request[4] = 0x06;
            return request;
        }

        public static bool Parse(byte[] response, out int percent, out bool charging)
        {
            percent = 0;
            charging = false;
            if (response == null || response.Length != 64) return false;
            if (response[0] != 4 || response[1] != 0x20 ||
                response[2] != 0 || response[3] != 0x1A || response[4] != 6)
                return false;
            if (response[8] > 100 || response[9] > 1) return false;
            percent = response[8];
            charging = response[9] == 1;
            return true;
        }

        private static BatteryReading Unknown(string status, string detail)
        {
            return new BatteryReading
            {
                Name = "VXE V87", Vid = 0x320F, Pid = 0x5088,
                Connection = "2.4G receiver", Status = status, Detail = detail
            };
        }

        public static BatteryReading Read(List<HidEndpoint> devices)
        {
            var interfaces = devices.Where(d => d.Vid == 0x320F && d.Pid == 0x5088 &&
                (d.ProductName ?? "").IndexOf("V87", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (interfaces.Count == 0)
                return Unknown("Not detected", "VXE V87 2.4G dongle not found");
            var endpoint = interfaces.FirstOrDefault(d => d.UsagePage == 0xFF1C && d.Usage == 0x0092 &&
                d.InputLength == 64 && d.OutputLength == 64);
            if (endpoint == null)
                return Unknown("Keyboard battery interface not found",
                    string.Join("; ", interfaces.Select(d => d.ToString()).ToArray()));

            // No scheduled polling, activity monitors, keep-alive loops, or radio wake-ups.
            try
            {
                int percent = 0;
                bool charging = false;
                byte[] response = HidWindows.Exchange(endpoint, Query,
                    result => Parse(result, out percent, out charging), 900);
                if (response == null)
                    return Unknown("No valid battery reply", "64-byte read-only status query got no matching response");
                return new BatteryReading
                {
                    Name = "VXE V87", Vid = 0x320F, Pid = 0x5088, Percent = percent,
                    Charging = charging, Connection = "2.4G receiver",
                    Status = "OK", Detail = "FF1C:0092 64B status reply"
                };
            }
            catch (Exception e)
            {
                return Unknown("HID battery read failed", e.GetType().Name + ": " + e.Message);
            }
        }

        public static bool SelfTest()
        {
            int level;
            bool charging;
            var raw = new byte[64];
            raw[0] = 4; raw[1] = 0x20; raw[3] = 0x1A; raw[4] = 6;
            raw[8] = 100; raw[9] = 0;
            bool normal = Parse(raw, out level, out charging) && level == 100 && !charging;
            raw[8] = 21; raw[9] = 1;
            bool isCharging = Parse(raw, out level, out charging) && level == 21 && charging;
            raw[8] = 101;
            bool invalidBattery = !Parse(raw, out level, out charging);
            raw[8] = 55; raw[3] = 0xEE;
            bool invalidCommand = !Parse(raw, out level, out charging);
            return normal && isCharging && invalidBattery && invalidCommand;
        }
    }
}
