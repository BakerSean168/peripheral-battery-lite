using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace PeripheralBatteryLite
{
    [DataContract]
    public sealed class AppPreferences
    {
        // 0=manual, otherwise minimum minutes between automatic queries.
        [DataMember(Order = 1)] public int IntervalMinutes = 0;
        // Activity gating uses actual RAWINPUT events for mouse/keyboard,
        // never a generic Windows-idle heuristic. Audio has no passive signal
        // and is skipped for automatic polling in activity-only mode.
        [DataMember(Order = 2)] public bool OnlyWhenDeviceActive = true;
        // User-supplied DLLs execute code inside this process; opt-in only.
        [DataMember(Order = 3)] public bool EnableExternalPlugins = false;
        [DataMember(Order = 4)] public bool PinPanel = false;

        public AppPreferences Copy()
        {
            return new AppPreferences
            {
                IntervalMinutes = IntervalMinutes,
                OnlyWhenDeviceActive = OnlyWhenDeviceActive,
                EnableExternalPlugins = EnableExternalPlugins,
                PinPanel = PinPanel
            };
        }

        public void Normalize()
        {
            if (!(IntervalMinutes == 0 || IntervalMinutes == 30 || IntervalMinutes == 60 ||
                IntervalMinutes == 120 || IntervalMinutes == 300))
                IntervalMinutes = 0;
        }
    }

    internal sealed class AppPreferencesStore
    {
        public readonly string PathName;
        public string LastError { get; private set; }

        public AppPreferencesStore(string path = null)
        {
            PathName = path ?? System.IO.Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), "PeripheralBatteryLite", "settings.json");
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
                initial.IntervalMinutes = 120;
                initial.OnlyWhenDeviceActive = false;
                initial.PinPanel = true;
                if (!store.Save(initial)) return false;
                var saved = store.Load();
                if (saved.IntervalMinutes != 120 || saved.OnlyWhenDeviceActive || !saved.PinPanel)
                    return false;
                saved.IntervalMinutes = 17;
                if (!store.Save(saved) || store.Load().IntervalMinutes != 0) return false;
                return true;
            }
            finally
            {
                try { Directory.Delete(System.IO.Path.GetDirectoryName(path), true); } catch (IOException) {}
            }
        }
    }
}
