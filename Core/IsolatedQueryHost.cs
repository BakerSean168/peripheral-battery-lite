using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
        // A stalled HID driver must never freeze the tray. Keep a hard timeout
        // per provider process even when the three readers run concurrently.
        private const int PerDeviceTimeoutMs = 4000;
        private const int MaximumConcurrentQueries = 3;

        // Synchronous diagnostics preserve provider display order. The tray
        // uses QueryParallel directly to render results as they become ready.
        public static List<BatteryReading> Query(IList<IBatteryProvider> selected)
        {
            if (selected == null || selected.Count == 0) return new List<BatteryReading>();
            var results = new BatteryReading[selected.Count];
            QueryParallel(selected, (i, reading) => results[i] = reading);
            return results.ToList();
        }

        // Called from a worker (never the UI thread). onResult runs on a worker
        // thread; UI consumers must marshal it through BeginInvoke.
        public static void QueryParallel(IList<IBatteryProvider> selected,
            Action<int, BatteryReading> onResult)
        {
            QueryParallelWith(selected, QueryOne, onResult);
        }

        private static void QueryParallelWith(IList<IBatteryProvider> selected,
            Func<IBatteryProvider, BatteryReading> query,
            Action<int, BatteryReading> onResult)
        {
            if (selected == null || selected.Count == 0) return;
            Parallel.For(0, selected.Count,
                new ParallelOptions { MaxDegreeOfParallelism = MaximumConcurrentQueries },
                i =>
                {
                    IBatteryProvider provider = selected[i];
                    BatteryReading reading;
                    try
                    {
                        reading = query(provider);
                        if (reading == null)
                            throw new InvalidOperationException("Provider returned a null battery reading");
                        reading.Name = provider.Id;
                    }
                    catch (Exception e)
                    {
                        reading = Error(provider, "Battery query failed",
                            e.GetType().Name + ": " + e.Message);
                    }
                    if (onResult != null) onResult(i, reading);
                });
        }

        private static BatteryReading QueryOne(IBatteryProvider provider)
        {
            string probe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                "PeripheralBatteryLite.Probe.exe");
            if (!File.Exists(probe))
                return Error(provider, "Probe executable missing",
                    "Keep the tray, probe and Contracts DLL in the same directory.");

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
                        catch (InvalidOperationException) { }
                        catch (System.ComponentModel.Win32Exception) { }
                        return Error(provider, "Timed out",
                            "HID battery query exceeded " + PerDeviceTimeoutMs + "ms.");
                    }

                    string output = process.StandardOutput.ReadToEnd().Trim();
                    BatteryReading result;
                    if (TryParse(output, out result))
                    {
                        result.Name = provider.Id;
                        return result;
                    }
                    return Error(provider, "No valid provider response",
                        "Child process exited with code " + process.ExitCode);
                }
            }
            catch (Exception e)
            {
                return Error(provider, "Battery query failed",
                    e.GetType().Name + ": " + e.Message);
            }
        }

        private static BatteryReading Error(IBatteryProvider provider, string status, string detail)
        {
            return new BatteryReading { Name = provider.Id, Status = status, Detail = detail };
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
            bool transport = TryParse("{\"name\":\"VXE V87\",\"vid_pid\":\"320F:5088\"," +
                "\"percent\":88,\"charging\":false,\"status\":\"OK\"}", out value) &&
                value.Percent == 88 && value.Status == "OK";
            if (!transport || TryParse("{\"name\":\"VXE V87\",\"percent\":111,\"status\":\"OK\"}", out value)
                || TryParse("{broken", out value))
                return false;

            // No device queries: exercise simultaneous provider completion,
            // distinct result slots, and independent handling of an exception.
            var providers = new List<IBatteryProvider> {
                new AtkF1V3Provider(), new VxeV87Provider(), new SteelSeriesNova5Provider()
            };
            int[] callbackCounts = new int[3];
            var resultByIndex = new BatteryReading[3];
            int completed = 0;
            QueryParallelWith(providers, p =>
            {
                if (p.Id == BatterySnapshotStore.Headset)
                    throw new InvalidOperationException("simulated receiver failure");
                if (p.Id == BatterySnapshotStore.Mouse)
                    Thread.Sleep(35); // A slower reader must not suppress others.
                return new BatteryReading { Status = "OK", Percent =
                    p.Id == BatterySnapshotStore.Mouse ? (int?)85 : (int?)100 };
            }, (i, r) =>
            {
                Interlocked.Increment(ref callbackCounts[i]);
                resultByIndex[i] = r;
                Interlocked.Increment(ref completed);
            });
            return completed == 3 &&
                callbackCounts.All(n => n == 1) &&
                resultByIndex[0].Name == BatterySnapshotStore.Mouse &&
                resultByIndex[0].Percent == 85 &&
                resultByIndex[1].Name == BatterySnapshotStore.Keyboard &&
                resultByIndex[1].Percent == 100 &&
                resultByIndex[2].Name == BatterySnapshotStore.Headset &&
                resultByIndex[2].Status == "Battery query failed" &&
                resultByIndex[2].Percent == null;
        }
    }
}
