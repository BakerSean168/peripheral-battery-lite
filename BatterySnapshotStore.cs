using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PeripheralBatteryLite
{
    [DataContract]
    internal sealed class StoredBattery
    {
        [DataMember(Order = 1)] public string DeviceId;
        [DataMember(Order = 2)] public int? Percent;
        [DataMember(Order = 3)] public bool? Charging;
        [DataMember(Order = 4)] public string LastQueryUtc;
        [DataMember(Order = 5)] public string LastSuccessUtc;
        [DataMember(Order = 6)] public bool LastQuerySucceeded;
    }

    [DataContract]
    internal sealed class BatteryCacheFile
    {
        [DataMember(Order = 1)] public int SchemaVersion = 1;
        [DataMember(Order = 2)] public List<StoredBattery> Devices = new List<StoredBattery>();
    }

    // This cache is the single source of displayed values. App launch never queries USB.
    // Only the UI "Refresh" action or an explicitly invoked diagnostic command can update it.
    internal sealed class BatterySnapshotStore
    {
        public const string Mouse = "ATK F1V3";
        public const string Keyboard = "VXE V87";
        public const string Headset = "SteelSeries Arctis Nova 5";

        private static readonly string[] KnownIds = { Mouse, Keyboard, Headset };
        private readonly string cachePath;
        private readonly BatteryCacheFile state;
        public string LastSaveError { get; private set; }
        public string FilePath { get { return cachePath; } }

        public BatterySnapshotStore(string path = null)
        {
            cachePath = path ?? Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "PeripheralBatteryLite", "battery-cache.json");
            state = Read(cachePath);
            state.Devices.RemoveAll(d => d == null || !KnownIds.Contains(d.DeviceId));
            foreach (string id in KnownIds)
            {
                if (!state.Devices.Any(d => d.DeviceId == id))
                    state.Devices.Add(new StoredBattery { DeviceId = id });
            }
        }

        private static BatteryCacheFile Read(string path)
        {
            try
            {
                if (!File.Exists(path)) return new BatteryCacheFile();
                using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var obj = new DataContractJsonSerializer(typeof(BatteryCacheFile))
                        .ReadObject(file) as BatteryCacheFile;
                    if (obj != null && obj.SchemaVersion == 1 && obj.Devices != null)
                        return obj;
                }
            }
            catch (Exception) { /* invalid or unavailable cache: safely start with no readings */ }
            return new BatteryCacheFile();
        }

        private static string NowIso()
        {
            return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        }

        private static DateTime? ParseUtc(string iso)
        {
            DateTime date;
            if (!string.IsNullOrEmpty(iso) &&
                DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date))
                return date.ToUniversalTime();
            return null;
        }

        public List<BatteryReading> Snapshot()
        {
            var result = new List<BatteryReading>();
            foreach (string id in KnownIds)
            {
                var stored = state.Devices.First(d => d.DeviceId == id);
                var successful = ParseUtc(stored.LastSuccessUtc);
                var attempted = ParseUtc(stored.LastQueryUtc);
                var reading = new BatteryReading
                {
                    Name = id,
                    Vid = id == Mouse ? (ushort)0x373B : id == Keyboard ? (ushort)0x320F : (ushort)0x1038,
                    Pid = id == Mouse ? (ushort)0x1303 : id == Keyboard ? (ushort)0x5088 : (ushort)0x2232,
                    Percent = successful.HasValue ? stored.Percent : null,
                    Charging = successful.HasValue ? stored.Charging : null,
                    Status = !attempted.HasValue ? "Not queried" :
                        !stored.LastQuerySucceeded ? "Previous query failed" : "Cached",
                    LastSuccessUtc = successful,
                    LastQueryUtc = attempted,
                    Connection = "2.4G receiver",
                    Detail = "Local cache; no automatic device polling"
                };
                result.Add(reading);
            }
            return result;
        }

        public void MergeAndSave(IEnumerable<BatteryReading> fresh)
        {
            LastSaveError = null;
            foreach (var newReading in fresh)
            {
                var stored = state.Devices.FirstOrDefault(d => d.DeviceId == newReading.Name);
                if (stored == null) continue;
                string time = NowIso();
                stored.LastQueryUtc = time;
                bool okay = newReading.Status == "OK" && newReading.Percent.HasValue &&
                    newReading.Percent.Value >= 0 && newReading.Percent.Value <= 100;
                stored.LastQuerySucceeded = okay;
                if (okay)
                {
                    stored.Percent = newReading.Percent.Value;
                    stored.Charging = newReading.Charging;
                    stored.LastSuccessUtc = time;
                }
            }
            string temp = null;
            try
            {
                var directory = Path.GetDirectoryName(cachePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                temp = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var file = File.Open(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(BatteryCacheFile)).WriteObject(file, state);
                    file.Flush(true);
                }
                if (File.Exists(cachePath))
                {
                    string backup = cachePath + ".bak";
                    File.Replace(temp, cachePath, backup, true);
                    try { File.Delete(backup); } catch (IOException) { }
                }
                else
                    File.Move(temp, cachePath);
            }
            catch (Exception e)
            {
                LastSaveError = e.GetType().Name + ": " + e.Message;
            }
            finally
            {
                if (temp != null) try { File.Delete(temp); } catch (IOException) { }
            }
        }

        public static bool SelfTest()
        {
            string folder = Path.Combine(Path.GetTempPath(), "PBL-cache-test-" + Guid.NewGuid().ToString("N"));
            string file = Path.Combine(folder, "battery-cache.json");
            try
            {
                var store = new BatterySnapshotStore(file);
                if (store.Snapshot().Any(x => x.Percent.HasValue || x.LastQueryUtc.HasValue))
                    return false;
                store.MergeAndSave(new[]
                {
                    new BatteryReading { Name = Mouse, Percent = 85, Charging = false, Status = "OK" },
                    new BatteryReading { Name = Keyboard, Percent = 100, Charging = false, Status = "OK" },
                    new BatteryReading { Name = Headset, Percent = 73, Charging = false, Status = "OK" }
                });
                if (store.LastSaveError != null || !File.Exists(file)) return false;
                var loaded = new BatterySnapshotStore(file).Snapshot();
                if (loaded.Count != 3 || loaded[0].Percent != 85 || loaded[1].Percent != 100 ||
                    loaded[2].Percent != 73 || loaded.Any(x => !x.LastQueryUtc.HasValue))
                    return false;
                DateTime initialSuccess = loaded[1].LastSuccessUtc.Value;
                store.MergeAndSave(new[]
                {
                    new BatteryReading { Name = Keyboard, Status = "No valid battery reply" }
                });
                var afterFailure = new BatterySnapshotStore(file).Snapshot();
                if (afterFailure[1].Percent != 100 || afterFailure[1].LastSuccessUtc != initialSuccess ||
                    afterFailure[1].Status != "Previous query failed") return false;
                if (new BatterySnapshotStore(Path.Combine(folder, "absent.json")).Snapshot()
                    .Any(x => x.Percent.HasValue)) return false;
                // A broken/corrupt cache should be ignored without querying real hardware.
                File.WriteAllText(file, "{ invalid JSON", Encoding.UTF8);
                if (new BatterySnapshotStore(file).Snapshot().Any(x => x.Percent.HasValue)) return false;
                return true;
            }
            finally
            {
                if (Directory.Exists(folder))
                    try { Directory.Delete(folder, true); } catch (IOException) { }
            }
        }
    }
}
