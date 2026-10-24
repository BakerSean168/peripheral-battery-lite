using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PeripheralBatteryLite
{
    [DataContract]
    internal sealed class ProviderPayload
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "vid_pid")] public string VidPid { get; set; }
        [DataMember(Name = "percent")] public int? Percent { get; set; }
        [DataMember(Name = "charging")] public bool? Charging { get; set; }
        [DataMember(Name = "status")] public string Status { get; set; }
    }

    internal static class IsolatedQueryHost
    {
        // Windows HID drivers can occasionally stall on a CreateFile/overlapped
        // I/O cleanup. Per-device subprocess isolation keeps the tray responsive,
        // allows a hard timeout and avoids orphan background threads in the UI.
        private const int PerDeviceTimeoutMs = 4000;

        public static List<BatteryReading> Query(IList<IBatteryProvider> selected)
        {
            var values = new List<BatteryReading>();
            string probe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "PeripheralBatteryLite.Probe.exe");
            foreach (var provider in selected)
            {
                if (!File.Exists(probe))
                {
                    values.Add(Error(provider, "Probe executable missing",
                        "Keep both EXE files in the same directory."));
                    continue;
                }
                try
                {
                    var start = new ProcessStartInfo
                    {
                        FileName = probe,
                        Arguments = "--provider-query \"" + provider.Id.Replace("\"", "\\\"") + "\"",
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true
                    };
                    using (Process process = Process.Start(start))
                    {
                        if (!process.WaitForExit(PerDeviceTimeoutMs))
                        {
                            try
                            {
                                process.Kill();
                                process.WaitForExit(1000);
                            }
                            catch (InvalidOperationException) {}
                            catch (System.ComponentModel.Win32Exception) {}
                            values.Add(Error(provider, "Timed out",
                                "HID query exceeded " + PerDeviceTimeoutMs + "ms; no background polling continues."));
                            continue;
                        }

                        string output = process.StandardOutput.ReadToEnd().Trim();
                        BatteryReading result;
                        if (TryParse(output, out result))
                        {
                            result.Name = provider.Id;
                            values.Add(result);
                        }
                        else
                            values.Add(Error(provider, "No valid provider response",
                                "Child process exited with code " + process.ExitCode));
                    }
                }
                catch (Exception e)
                {
                    values.Add(Error(provider, "Battery query failed",
                        e.GetType().Name + ": " + e.Message));
                }
            }
            return values;
        }

        private static BatteryReading Error(IBatteryProvider provider, string status, string detail)
        {
            return new BatteryReading
            {
                Name = provider.Id, Status = status, Detail = detail
            };
        }

        public static bool TryParse(string json, out BatteryReading reading)
        {
            reading = null;
            if (string.IsNullOrEmpty(json) || json.Length > 16384) return false;
            try
            {
                using (var input = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    var obj = (ProviderPayload)new DataContractJsonSerializer(
                        typeof(ProviderPayload)).ReadObject(input);
                    if (obj == null || string.IsNullOrWhiteSpace(obj.Name) ||
                        string.IsNullOrWhiteSpace(obj.Status) ||
                        (obj.Percent.HasValue && (obj.Percent < 0 || obj.Percent > 100)))
                        return false;
                    var pair = (obj.VidPid ?? "").Split(':');
                    ushort vid = 0, pid = 0;
                    if (pair.Length == 2)
                    {
                        ushort.TryParse(pair[0], NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out vid);
                        ushort.TryParse(pair[1], NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out pid);
                    }
                    reading = new BatteryReading
                    {
                        Name = obj.Name, Status = obj.Status, Percent = obj.Percent,
                        Charging = obj.Charging, Vid = vid, Pid = pid
                    };
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        public static bool SelfTest()
        {
            BatteryReading value;
            bool good = TryParse("{\"name\":\"VXE V87\",\"vid_pid\":\"320F:5088\"," +
                "\"percent\":88,\"charging\":false,\"status\":\"OK\"}", out value) &&
                value.Percent == 88 && value.Status == "OK";
            return good && !TryParse("{\"name\":\"VXE V87\",\"percent\":111,\"status\":\"OK\"}", out value) &&
                !TryParse("{broken", out value);
        }
    }
}
