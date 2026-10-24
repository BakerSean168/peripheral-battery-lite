using System;
using System.Collections.Generic;
using System.Linq;

namespace PeripheralBatteryLite
{
    public sealed class BatteryReading
    {
        public string Name, Status, Connection;
        public int? Percent;
        public bool? Charging;
        public DateTime? LastSuccessUtc;
        public DateTime? LastQueryUtc;
        public ushort Vid, Pid;
        public string Detail;
        public string Display { get { return Percent.HasValue ? Percent.Value + "%" : "--"; } }
        public string Json
        {
            get
            {
                string pct = Percent.HasValue ? Percent.Value.ToString() : "null";
                string ch = Charging.HasValue ? (Charging.Value ? "true" : "false") : "null";
                return "{\"name\":\"" + Name + "\",\"vid_pid\":\"" + Vid.ToString("X4") + ":" + Pid.ToString("X4")
                    + "\",\"percent\":" + pct + ",\"charging\":" + ch
                    + ",\"status\":\"" + Status + "\"}";
            }
        }
    }

    internal static class BatteryProtocols
    {
        public static bool ParseMouse(byte[] b, out int percent, out bool charging)
        {
            percent = 0; charging = false;
            // Compx/Nordic52: 17-byte report 08/04, checksum modulo 256 = 0x55.
            if (b == null || b.Length != 17 || b[0] != 0x08 || b[1] != 0x04) return false;
            int checksum = 0;
            foreach (byte v in b) checksum = (checksum + v) & 255;
            if (checksum != 0x55 || b[6] > 100 || b[7] > 1) return false;
            // Both genuine zero percent and 100 percent may exist; neither is faked.
            percent = b[6]; charging = b[7] == 1 && percent < 100;
            return true;
        }

        public static bool ParseKeyboard(byte[] b, out int percent, out bool? charging)
        {
            percent = 0; charging = null;
            // Weisheng 320F:5055, query: 04 00 00 1A 06 00 00 00
            // Response raw[0] is report ID 04. raw[3] is command 0x1A.
            // raw[8] is battery percent and raw[9] is charging flag.
            if (b == null || b.Length < 10 || b[0] != 4 || b[3] != 26 || b[8] > 100) return false;
            if (b[9] > 1) return false;
            percent = b[8]; charging = b[9] == 1;
            return true;
        }

        private static readonly byte[] MouseQuery =
            { 0x08, 0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x49 };
        private static readonly byte[] KeyboardQuery = { 4, 0, 0, 26, 6, 0, 0, 0 };

        public static BatteryReading ReadMouseOnly()
        {
            return ReadMouse(HidWindows.Enumerate());
        }

        public static BatteryReading ReadMouse(List<HidEndpoint> devices)
        {
            return ReadOne(devices, "ATK F1V3", 0x373B, 0x1303, true);
        }

        public static List<BatteryReading> ReadAll()
        {
            List<HidEndpoint> devices = HidWindows.Enumerate();
            return new List<BatteryReading>
            {
                ReadOne(devices, "ATK F1V3", 0x373B, 0x1303, true),
                V87KeyboardBattery.Read(devices),
                SteelSeriesBattery.Read(devices)
            };
        }

        private static BatteryReading ReadKeyboard(List<HidEndpoint> devices)
        {
            bool wired = devices.Any(d => d.Vid == 0x320F && d.Pid == 0x5055 &&
                (d.ProductName ?? "").IndexOf("V87", StringComparison.OrdinalIgnoreCase) >= 0);
            bool receiver = devices.Any(d => d.Vid == 0x320F && d.Pid == 0x5088 &&
                (d.ProductName ?? "").IndexOf("V87", StringComparison.OrdinalIgnoreCase) >= 0);
            var reading = new BatteryReading
            {
                Name = "VXE V87", Vid = 0x320F, Pid = receiver ? (ushort)0x5088 : (ushort)0x5055,
                Connection = wired && receiver ? "USB and 2.4G receiver" :
                    receiver ? "2.4G receiver" : wired ? "USB" : "",
                Status = wired && receiver ? "USB and 2.4G receiver detected; link unknown" :
                    receiver ? "2.4G receiver detected; link unknown" :
                    wired ? "USB connected - battery protocol unverified" : "Not detected",
                Detail = "Firmware exposes FF1C:0092 64-byte reports; no verified battery command."
            };
            // A receiver existing is not proof the keyboard radio link is active.
            // Avoid probing undocumented 64-byte commands, firmware, settings or pairing.
            return reading;
        }

        private static BatteryReading ReadOne(List<HidEndpoint> devices, string name, ushort vid, ushort pid, bool mouse)
        {
            var reading = new BatteryReading { Name = name, Vid = vid, Pid = pid, Status = "Not detected",
                Connection = mouse ? "2.4G receiver" : "USB", Detail = "" };
            List<HidEndpoint> candidates = devices.Where(d => d.Vid == vid && d.Pid == pid &&
                (mouse || (d.ProductName ?? "").IndexOf("V87", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (candidates.Count == 0) return reading;
            IEnumerable<HidEndpoint> matches = mouse
                ? candidates.Where(d => (d.UsagePage == 0xFF02 || d.UsagePage == 0xFF04) && d.OutputLength == 17 && d.InputLength >= 17)
                : candidates.Where(d => d.UsagePage >= 0xFF00 && d.OutputLength == 8 && d.InputLength >= 10);
            var selected = matches.OrderBy(d => mouse && d.UsagePage != 0xFF02 ? 1 : 0).ToList();
            if (selected.Count == 0)
            {
                reading.Status = mouse ? "Mouse battery interface unavailable" : "USB connected - battery protocol unverified";
                reading.Detail = string.Join("; ", candidates.Select(d => d.ToString()).ToArray());
                return reading;
            }
            foreach (var ep in selected)
            {
                try
                {
                    int value = 0;
                    bool chargeMouse = false;
                    bool? chargeKeyboard = null;
                    byte[] b = HidWindows.Exchange(ep, mouse ? MouseQuery : KeyboardQuery,
                        result => mouse ? ParseMouse(result, out value, out chargeMouse)
                                        : ParseKeyboard(result, out value, out chargeKeyboard));
                    if (b == null) { reading.Status = "No valid battery reply"; continue; }
                    reading.Percent = value;
                    reading.Charging = mouse ? (bool?)chargeMouse : chargeKeyboard;
                    reading.Status = "OK";
                    reading.Detail = ep.ToString();
                    return reading;
                }
                catch (Exception ex)
                {
                    reading.Status = "HID read failed";
                    reading.Detail = ex.GetType().Name + ": " + ex.Message;
                }
            }
            return reading;
        }

        public static string Probe()
        {
            var ep = HidWindows.Enumerate();
            if (ep.Count == 0) return "No known HID endpoints found.";
            return string.Join(Environment.NewLine, ep.Select(d => d.ToString()).ToArray());
        }

        public static bool SelfTest()
        {
            byte[] sampleMouse = { 0x08, 0x04, 0, 0, 0, 0x02, 0x23, 0, 0x0E, 0x94, 0, 0, 0, 0, 0, 0, 0x82 };
            int pct; bool charging; bool? kbCharging;
            bool mouseOk = ParseMouse(sampleMouse, out pct, out charging) && pct == 35 && !charging;
            var wrong = (byte[])sampleMouse.Clone(); wrong[6] = 101;
            bool badRejected = !ParseMouse(wrong, out pct, out charging);
            byte[] sampleKb = { 4, 0, 0, 26, 6, 0, 0, 0, 80, 1, 0, 0, 0 };
            bool kbOk = ParseKeyboard(sampleKb, out pct, out kbCharging) && pct == 80 && kbCharging == true;
            return mouseOk && badRejected && kbOk && SteelSeriesBattery.SelfTest() && V87KeyboardBattery.SelfTest();
        }
    }
}