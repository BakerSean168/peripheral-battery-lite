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
    internal sealed class BatterySample
    {
        [DataMember(Order = 1)] public string DeviceId;
        [DataMember(Order = 2)] public string TimeUtc;
        [DataMember(Order = 3)] public int Percent;
        [DataMember(Order = 4)] public bool? Charging;
        [DataMember(Order = 5)] public int? VoltageMillivolts;

        public DateTime When
        {
            get
            {
                DateTime result;
                return DateTime.TryParse(TimeUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out result) ?
                    result.ToUniversalTime() : DateTime.MinValue;
            }
        }
    }

    [DataContract]
    internal sealed class BatteryHistoryFile
    {
        [DataMember(Order = 1)] public int SchemaVersion = 1;
        [DataMember(Order = 2)] public List<BatterySample> Samples = new List<BatterySample>();
    }

    // Low-frequency, bounded JSON storage avoids adding database dependencies.
    // Atomic replacement prevents a crash mid-write from destroying previous history.
    internal sealed class BatteryHistoryStore
    {
        private readonly string path;
        private readonly List<BatterySample> samples;
        public string LastError { get; private set; }

        public BatteryHistoryStore(string filePath = null)
        {
            path = filePath ?? AppDataPaths.HistoryPath;
            samples = Load(path);
        }

        private static List<BatterySample> Load(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 8 * 1024 * 1024)
                    return new List<BatterySample>();
                using (var file = File.OpenRead(path))
                {
                    var obj = new DataContractJsonSerializer(typeof(BatteryHistoryFile))
                        .ReadObject(file) as BatteryHistoryFile;
                    if (obj != null && obj.SchemaVersion == 1 && obj.Samples != null)
                        return obj.Samples.Where(s => s != null && s.DeviceId != null &&
                            s.DeviceId.Length <= 128 && s.Percent >= 0 && s.Percent <= 100 &&
                            s.When != DateTime.MinValue).Take(15000).ToList();
                }
            }
            catch (Exception) { /* Corrupt files cannot interfere with battery reads. */ }
            return new List<BatterySample>();
        }

        public List<BatterySample> Recent(string id, int days = 30, DateTime? nowUtc = null)
        {
            DateTime cutoff = (nowUtc ?? DateTime.UtcNow).ToUniversalTime().AddDays(-Math.Max(1, days));
            return samples.Where(x => string.Equals(x.DeviceId, id, StringComparison.OrdinalIgnoreCase) &&
                x.When >= cutoff).OrderBy(x => x.When).ToList();
        }

        public bool Record(string id, int? percent, bool? charging,
            DateTime nowUtc, int retentionDays = 180, int? voltageMillivolts = null)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length > 128 ||
                !percent.HasValue || percent.Value < 0 || percent.Value > 100) return false;
            nowUtc = nowUtc.ToUniversalTime();
            // Discard accidental retrograde/duplicate data from clock changes.
            var last = samples.Where(x => string.Equals(x.DeviceId, id, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.When).FirstOrDefault();
            if (last != null && last.When > nowUtc) return false;
            if (last != null && last.Percent == percent.Value &&
                last.Charging == charging && last.VoltageMillivolts == voltageMillivolts &&
                nowUtc - last.When < TimeSpan.FromMinutes(30))
                return false;
            samples.Add(new BatterySample
            {
                DeviceId = id, Percent = percent.Value, Charging = charging,
                VoltageMillivolts = voltageMillivolts,
                TimeUtc = nowUtc.ToString("o", CultureInfo.InvariantCulture)
            });
            DateTime cutoff = nowUtc.AddDays(-Math.Max(30, Math.Min(365, retentionDays)));
            samples.RemoveAll(s => s.When < cutoff);
            if (samples.Count > 15000)
                samples.RemoveRange(0, samples.Count - 15000);
            Save();
            return LastError == null;
        }

        public void ExportCsv(string id, string destination, int days = 30)
        {
            var data = Recent(id, days);
            var lines = new List<string> { "utc_time,device_id,battery_percent,charging,voltage_mV" };
            foreach (var item in data)
            {
                lines.Add(item.When.ToString("o", CultureInfo.InvariantCulture) + ",\"" +
                    item.DeviceId.Replace("\"", "\"\"") + "\"," +
                    item.Percent.ToString(CultureInfo.InvariantCulture) + "," +
                    (item.Charging.HasValue ? (item.Charging.Value ? "true" : "false") : "") + "," +
                    (item.VoltageMillivolts.HasValue ? item.VoltageMillivolts.Value.ToString() : ""));
            }
            File.WriteAllLines(destination, lines, new UTF8Encoding(true));
        }

        private void Save()
        {
            LastError = null;
            string temp = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(BatteryHistoryFile))
                        .WriteObject(stream, new BatteryHistoryFile { Samples = samples });
                    stream.Flush(true);
                }
                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else File.Move(temp, path);
            }
            catch (Exception e) { LastError = e.Message; }
            finally { if (temp != null) try { File.Delete(temp); } catch (IOException) {} }
        }

        public static bool SelfTest()
        {
            var folder = Path.Combine(Path.GetTempPath(), "PBL-history-" + Guid.NewGuid().ToString("N"));
            var file = Path.Combine(folder, "history.json");
            try
            {
                var store = new BatteryHistoryStore(file);
                DateTime t = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
                if (!store.Record("mouse", 80, false, t, 180, 3732)) return false;
                if (store.Record("mouse", 80, false, t.AddMinutes(1), 180, 3732)) return false;
                if (!store.Record("mouse", 78, false, t.AddDays(1))) return false;
                if (!store.Record("keyboard", 40, false, t.AddDays(1))) return false;
                if (store.Record("mouse", null, false, t.AddDays(2))) return false;
                var loaded = new BatteryHistoryStore(file);
                var recent = loaded.Recent("mouse", 30, t.AddDays(2));
                if (recent.Count != 2 || recent[0].Percent != 80 || recent[1].Percent != 78 ||
                    recent[0].VoltageMillivolts != 3732)
                    return false;
                string csv = Path.Combine(folder, "export.csv");
                loaded.ExportCsv("mouse", csv, 100);
                if (!File.ReadAllText(csv).Contains("battery_percent,charging,voltage_mV") ||
                    !File.ReadAllText(csv).Contains(",78,false")) return false;
                return loaded.Recent("mouse", 1, t.AddDays(3)).Count == 0;
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (IOException) {}
            }
        }
    }
}
