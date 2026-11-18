using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PeripheralBatteryLite
{
    [DataContract]
    public sealed class DevicePreferences
    {
        [DataMember(Order = 1)] public string DeviceId = "";
        [DataMember(Order = 2)] public string Alias = "";
        [DataMember(Order = 3)] public int AlertThreshold = 45;
        [DataMember(Order = 4)] public bool AlertsEnabled = false;
        // inherit = global activity policy; active = recent input required;
        // always = poll on schedule; manual = never poll automatically.
        [DataMember(Order = 5)] public string AutoQueryMode = "inherit";
        // User-provided nominal battery capacity: not hardware telemetry.
        [DataMember(Order = 6)] public int? RatedCapacityMah;

        public DevicePreferences Copy()
        {
            return new DevicePreferences
            {
                DeviceId = DeviceId, Alias = Alias, AlertThreshold = AlertThreshold,
                AlertsEnabled = AlertsEnabled, AutoQueryMode = AutoQueryMode,
                RatedCapacityMah = RatedCapacityMah
            };
        }

        public void Normalize()
        {
            if (DeviceId == null) DeviceId = "";
            if (Alias == null) Alias = "";
            DeviceId = DeviceId.Trim();
            Alias = Alias.Trim();
            if (Alias.Length > 30) Alias = Alias.Substring(0, 30);
            if (AlertThreshold < 5 || AlertThreshold > 95) AlertThreshold = 45;
            if (AutoQueryMode != "inherit" && AutoQueryMode != "active" &&
                AutoQueryMode != "always" && AutoQueryMode != "manual")
                AutoQueryMode = "inherit";
            if (RatedCapacityMah.HasValue &&
                (RatedCapacityMah.Value < 1 || RatedCapacityMah.Value > 100000))
                RatedCapacityMah = null;
        }
    }

    [DataContract]
    public sealed class AppPreferences
    {
        // 0=manual; otherwise the minimum minutes between scheduled queries.
        [DataMember(Order = 1)] public int IntervalMinutes = 0;
        [DataMember(Order = 2)] public bool OnlyWhenDeviceActive = true;
        [DataMember(Order = 3)] public bool EnableExternalPlugins = false;
        [DataMember(Order = 4)] public bool PinPanel = false;
        [DataMember(Order = 5)] public List<DevicePreferences> Devices = new List<DevicePreferences>();
        // "lowest" picks the lowest recent reading; otherwise use a provider ID.
        [DataMember(Order = 6)] public string TrayIconDeviceId = "ATK F1V3";
        [DataMember(Order = 7)] public bool QuietHoursEnabled = false;
        [DataMember(Order = 8)] public int QuietStartHour = 23;
        [DataMember(Order = 9)] public int QuietEndHour = 8;
        [DataMember(Order = 10)] public int HistoryRetentionDays = 180;

        [OnDeserializing]
        private void SetMigrationDefaults(StreamingContext context)
        {
            // DataContractJsonSerializer bypasses field initializers for missing fields.
            Devices = new List<DevicePreferences>();
            TrayIconDeviceId = "ATK F1V3";
            QuietStartHour = 23;
            QuietEndHour = 8;
            HistoryRetentionDays = 180;
        }

        public DevicePreferences GetDevice(string id)
        {
            if (Devices == null) Devices = new List<DevicePreferences>();
            var found = Devices.FirstOrDefault(d =>
                d != null && string.Equals(d.DeviceId, id, StringComparison.OrdinalIgnoreCase));
            if (found != null) return found;
            found = new DevicePreferences { DeviceId = id ?? "" };
            Devices.Add(found);
            return found;
        }

        public string NameOf(string id, string fallback)
        {
            var d = GetDevice(id);
            return string.IsNullOrWhiteSpace(d.Alias) ? fallback : d.Alias;
        }

        public AppPreferences Copy()
        {
            return new AppPreferences
            {
                IntervalMinutes = IntervalMinutes,
                OnlyWhenDeviceActive = OnlyWhenDeviceActive,
                EnableExternalPlugins = EnableExternalPlugins,
                PinPanel = PinPanel,
                Devices = Devices == null ? new List<DevicePreferences>() :
                    Devices.Where(d => d != null).Select(d => d.Copy()).ToList(),
                TrayIconDeviceId = TrayIconDeviceId,
                QuietHoursEnabled = QuietHoursEnabled,
                QuietStartHour = QuietStartHour,
                QuietEndHour = QuietEndHour,
                HistoryRetentionDays = HistoryRetentionDays
            };
        }

        public bool IsQuietHour(DateTime local)
        {
            if (!QuietHoursEnabled || QuietStartHour == QuietEndHour) return false;
            int h = local.Hour;
            if (QuietStartHour < QuietEndHour)
                return h >= QuietStartHour && h < QuietEndHour;
            return h >= QuietStartHour || h < QuietEndHour;
        }

        public void Normalize()
        {
            if (!(IntervalMinutes == 0 || IntervalMinutes == 30 ||
                IntervalMinutes == 60 || IntervalMinutes == 120 ||
                IntervalMinutes == 240 || IntervalMinutes == 300))
                IntervalMinutes = 0;
            if (Devices == null) Devices = new List<DevicePreferences>();
            Devices = Devices.Where(d => d != null && !string.IsNullOrWhiteSpace(d.DeviceId))
                .Take(100).ToList();
            foreach (var device in Devices) device.Normalize();
            Devices = Devices.GroupBy(d => d.DeviceId, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).ToList();
            if (string.IsNullOrWhiteSpace(TrayIconDeviceId) ||
                TrayIconDeviceId.Length > 128) TrayIconDeviceId = "ATK F1V3";
            if (QuietStartHour < 0 || QuietStartHour > 23) QuietStartHour = 23;
            if (QuietEndHour < 0 || QuietEndHour > 23) QuietEndHour = 8;
            if (HistoryRetentionDays != 30 && HistoryRetentionDays != 90 &&
                HistoryRetentionDays != 180 && HistoryRetentionDays != 365)
                HistoryRetentionDays = 180;
        }
    }

    internal sealed class AppPreferencesStore
    {
        public readonly string PathName;
        public string LastError { get; private set; }

        public AppPreferencesStore(string path = null)
        {
            PathName = path ?? AppDataPaths.SettingsPath;
        }

        public AppPreferences Load()
        {
            try
            {
                if (!File.Exists(PathName)) return new AppPreferences();
                using (var input = File.OpenRead(PathName))
                {
                    var value = (AppPreferences)new DataContractJsonSerializer(typeof(AppPreferences)).ReadObject(input);
                    if (value != null) { value.Normalize(); return value; }
                }
            }
            catch (Exception e) { LastError = e.Message; }
            return new AppPreferences();
        }

        public bool Save(AppPreferences preferences)
        {
            LastError = null;
            preferences.Normalize();
            string temp = null;
            try
            {
                string parent = System.IO.Path.GetDirectoryName(PathName);
                Directory.CreateDirectory(parent);
                temp = PathName + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var output = File.Open(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    new DataContractJsonSerializer(typeof(AppPreferences)).WriteObject(output, preferences);
                    output.Flush(true);
                }
                if (File.Exists(PathName))
                {
                    string backup = PathName + ".bak";
                    File.Replace(temp, PathName, backup, true);
                    try { File.Delete(backup); } catch (IOException) {}
                }
                else File.Move(temp, PathName);
                return true;
            }
            catch (Exception e) { LastError = e.Message; return false; }
            finally { if (temp != null) try { File.Delete(temp); } catch (IOException) {} }
        }

        public static bool SelfTest()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                "PBL-settings-" + Guid.NewGuid().ToString("N"), "settings.json");
            try
            {
                var store = new AppPreferencesStore(path);
                var initial = store.Load();
                if (initial.IntervalMinutes != 0 || !initial.OnlyWhenDeviceActive ||
                    initial.EnableExternalPlugins) return false;
                initial.IntervalMinutes = 240;
                initial.OnlyWhenDeviceActive = false;
                initial.PinPanel = true;
                initial.GetDevice("VXE V87").Alias = "Keyboard";
                initial.GetDevice("VXE V87").AlertsEnabled = true;
                initial.GetDevice("VXE V87").RatedCapacityMah = 4000;
                initial.TrayIconDeviceId = "lowest";
                initial.QuietHoursEnabled = true;
                if (!store.Save(initial)) return false;
                var saved = store.Load();
                if (saved.IntervalMinutes != 240 || saved.OnlyWhenDeviceActive || !saved.PinPanel ||
                    saved.NameOf("VXE V87", "V87") != "Keyboard" ||
                    saved.GetDevice("VXE V87").RatedCapacityMah != 4000 ||
                    !saved.GetDevice("VXE V87").AlertsEnabled || saved.TrayIconDeviceId != "lowest")
                    return false;
                var clone = saved.Copy();
                clone.GetDevice("VXE V87").Alias = "Changed";
                if (saved.GetDevice("VXE V87").Alias != "Keyboard") return false;
                if (!saved.IsQuietHour(new DateTime(2026, 11, 18, 23, 0, 0)) ||
                    saved.IsQuietHour(new DateTime(2026, 11, 18, 12, 0, 0))) return false;
                saved.IntervalMinutes = 17;
                if (!store.Save(saved) || store.Load().IntervalMinutes != 0) return false;
                // Previous four-field settings must still deserialize cleanly.
                File.WriteAllText(path,
                    "{\"IntervalMinutes\":120,\"OnlyWhenDeviceActive\":true,\"EnableExternalPlugins\":false,\"PinPanel\":false}");
                var legacy = store.Load();
                return legacy.IntervalMinutes == 120 && legacy.Devices != null &&
                    legacy.GetDevice("ATK F1V3").AlertThreshold == 45 &&
                    legacy.QuietStartHour == 23 && legacy.QuietEndHour == 8 &&
                    legacy.HistoryRetentionDays == 180;
            }
            finally
            {
                try { Directory.Delete(System.IO.Path.GetDirectoryName(path), true); } catch (IOException) {}
            }
        }
    }
}
