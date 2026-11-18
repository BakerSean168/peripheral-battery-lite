using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PeripheralBatteryLite
{
    [DataContract]
    internal sealed class AlertState
    {
        [DataMember(Order = 1)] public string DeviceId;
        [DataMember(Order = 2)] public bool Alerted;
        [DataMember(Order = 3)] public bool Pending;
        [DataMember(Order = 4)] public string SnoozeUntilUtc;
        [DataMember(Order = 5)] public int LastPercent;
    }

    [DataContract]
    internal sealed class NotificationEntry
    {
        [DataMember(Order = 1)] public string DeviceId;
        [DataMember(Order = 2)] public string TimeUtc;
        [DataMember(Order = 3)] public int Percent;

        public DateTime When
        {
            get
            {
                DateTime d;
                return DateTime.TryParse(TimeUtc, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out d) ? d.ToUniversalTime() : DateTime.MinValue;
            }
        }
    }

    [DataContract]
    internal sealed class AlertFile
    {
        [DataMember(Order = 1)] public int SchemaVersion = 1;
        [DataMember(Order = 2)] public List<AlertState> States = new List<AlertState>();
        [DataMember(Order = 3)] public List<NotificationEntry> Log = new List<NotificationEntry>();
    }

    internal sealed class BatteryAlertStore
    {
        private readonly string path;
        private readonly AlertFile data;
        public string LastError { get; private set; }

        public BatteryAlertStore(string filePath = null)
        {
            path = filePath ?? AppDataPaths.AlertsPath;
            data = Load(path);
        }

        private static AlertFile Load(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024)
                    return new AlertFile();
                using (var stream = File.OpenRead(path))
                {
                    var result = new DataContractJsonSerializer(typeof(AlertFile))
                        .ReadObject(stream) as AlertFile;
                    if (result != null && result.SchemaVersion == 1 &&
                        result.States != null && result.Log != null)
                        return result;
                }
            }
            catch (Exception) {}
            return new AlertFile();
        }

        private AlertState State(string id)
        {
            var s = data.States.FirstOrDefault(x => x != null &&
                string.Equals(x.DeviceId, id, StringComparison.OrdinalIgnoreCase));
            if (s != null) return s;
            s = new AlertState { DeviceId = id };
            data.States.Add(s);
            return s;
        }

        public void Observe(string id, int percent, bool? charging,
            DevicePreferences prefs)
        {
            if (prefs == null || string.IsNullOrWhiteSpace(id) ||
                percent < 0 || percent > 100) return;
            var state = State(id);
            if (percent >= Math.Min(100, prefs.AlertThreshold + 5))
            {
                state.Alerted = false;
                state.Pending = false;
            }
            else if (prefs.AlertsEnabled && percent <= prefs.AlertThreshold &&
                charging != true && !state.Alerted)
            {
                state.Pending = true;
            }
            if (!prefs.AlertsEnabled) state.Pending = false;
            state.LastPercent = percent;
            Save();
        }

        public void Snooze(string id, DateTime untilUtc)
        {
            State(id).SnoozeUntilUtc = untilUtc.ToUniversalTime().ToString("o");
            Save();
        }

        private static DateTime? ParseTime(string raw)
        {
            DateTime time;
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out time)) return time.ToUniversalTime();
            return null;
        }

        public List<NotificationEntry> DeliverPending(AppPreferences prefs,
            IEnumerable<BatteryReading> snapshots, DateTime nowUtc)
        {
            var delivered = new List<NotificationEntry>();
            nowUtc = nowUtc.ToUniversalTime();
            if (prefs.IsQuietHour(nowUtc.ToLocalTime())) return delivered;
            foreach (var reading in snapshots)
            {
                if (reading == null || string.IsNullOrEmpty(reading.Name)) continue;
                var config = prefs.GetDevice(reading.Name);
                var state = State(reading.Name);
                var snoozed = ParseTime(state.SnoozeUntilUtc);
                if (!config.AlertsEnabled || !state.Pending || state.Alerted ||
                    (snoozed.HasValue && snoozed.Value > nowUtc)) continue;
                // A failed reading retains a stale cached battery value; never
                // notify on that value. A pending quiet-hours alert is allowed
                // after a successful reading up to 24 hours old.
                if (!reading.LastSuccessUtc.HasValue ||
                    nowUtc - reading.LastSuccessUtc.Value.ToUniversalTime() > TimeSpan.FromHours(24) ||
                    reading.Status == "Previous query failed" || reading.Charging == true ||
                    !reading.Percent.HasValue || reading.Percent.Value > config.AlertThreshold)
                    continue;
                state.Pending = false;
                state.Alerted = true;
                var entry = new NotificationEntry
                {
                    DeviceId = reading.Name,
                    Percent = reading.Percent.Value,
                    TimeUtc = nowUtc.ToString("o", CultureInfo.InvariantCulture)
                };
                delivered.Add(entry);
                data.Log.Add(entry);
            }
            if (delivered.Count > 0)
            {
                if (data.Log.Count > 200) data.Log.RemoveRange(0, data.Log.Count - 200);
                Save();
            }
            return delivered;
        }

        public List<NotificationEntry> RecentNotifications(string deviceId = null, int max = 20)
        {
            return data.Log.Where(e => e != null &&
                (deviceId == null || string.Equals(deviceId, e.DeviceId, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(e => e.When).Take(Math.Max(1, max)).ToList();
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
                    new DataContractJsonSerializer(typeof(AlertFile)).WriteObject(stream, data);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
            }
            catch (Exception e) { LastError = e.Message; }
            finally { if (temp != null) try { File.Delete(temp); } catch (IOException) {} }
        }

        public static bool SelfTest()
        {
            string dir = Path.Combine(Path.GetTempPath(), "PBL-alert-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, "alerts.json");
            DateTime now = new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
            var prefs = new AppPreferences();
            var device = prefs.GetDevice("mouse");
            device.AlertsEnabled = true;
            device.AlertThreshold = 45;
            Func<int, BatteryReading> reading = pct => new BatteryReading {
                Name = "mouse", Percent = pct, Charging = false, Status = "Cached",
                LastSuccessUtc = now
            };
            try
            {
                var store = new BatteryAlertStore(path);
                store.Observe("mouse", 43, false, device);
                var first = store.DeliverPending(prefs, new[] { reading(43) }, now);
                if (first.Count != 1) return false;
                store.Observe("mouse", 42, false, device);
                if (store.DeliverPending(prefs, new[] { reading(42) }, now).Count != 0) return false;
                store.Observe("mouse", 51, false, device);
                store.Observe("mouse", 40, false, device);
                store.Snooze("mouse", now.AddHours(24));
                if (store.DeliverPending(prefs, new[] { reading(40) }, now).Count != 0) return false;
                var reloaded = new BatteryAlertStore(path);
                if (reloaded.DeliverPending(prefs, new[] { reading(40) }, now.AddHours(25)).Count != 0)
                    return false; // Snapshot has expired, no stale notification.
                reloaded.Observe("mouse", 40, false, device);
                var fresh = reading(40);
                fresh.LastSuccessUtc = now.AddHours(25);
                prefs.QuietHoursEnabled = true;
                // Same UTC hour in tests may be a different local timezone;
                // force a full-day quiet window except 12 via specific test date.
                DateTime local = now.AddHours(25).ToLocalTime();
                prefs.QuietStartHour = local.Hour;
                prefs.QuietEndHour = (local.Hour + 1) % 24;
                if (reloaded.DeliverPending(prefs, new[] { fresh }, now.AddHours(25)).Count != 0)
                    return false;
                prefs.QuietHoursEnabled = false;
                if (reloaded.DeliverPending(prefs, new[] { fresh }, now.AddHours(25)).Count != 1)
                    return false;
                return new BatteryAlertStore(path).RecentNotifications("mouse").Count == 2;
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) {} }
        }
    }
}
