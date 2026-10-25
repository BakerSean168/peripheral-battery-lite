using System;
using System.IO;

namespace PeripheralBatteryLite
{
    // Portable installs keep their user-owned cache and settings beside the app.
    // The opt-in marker prevents changing storage paths for legacy installations.
    // A marker is a file (not a setting), so startup doesn't have to query devices
    // or create any directories to discover its storage layout.
    internal static class AppDataPaths
    {
        public const string PortableMarker = "portable.flag";
        public const string CacheFilename = "battery-cache.json";
        public const string SettingsFilename = "settings.json";

        public static string ResolveRoot(string appDirectory = null, string localAppDataDirectory = null)
        {
            string executableDirectory = appDirectory ?? AppDomain.CurrentDomain.BaseDirectory;
            string fallback = localAppDataDirectory ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (File.Exists(Path.Combine(executableDirectory, PortableMarker)))
                return executableDirectory;
            return Path.Combine(fallback, "PeripheralBatteryLite");
        }

        public static string CachePath
        {
            get { return Path.Combine(ResolveRoot(), CacheFilename); }
        }

        public static string SettingsPath
        {
            get { return Path.Combine(ResolveRoot(), SettingsFilename); }
        }

        public static bool SelfTest()
        {
            string temp = Path.Combine(Path.GetTempPath(),
                "PBL-portable-test-" + Guid.NewGuid().ToString("N"));
            string app = Path.Combine(temp, "app");
            string fallback = Path.Combine(temp, "appdata");
            try
            {
                Directory.CreateDirectory(app);
                Directory.CreateDirectory(fallback);
                string legacy = ResolveRoot(app, fallback);
                if (legacy != Path.Combine(fallback, "PeripheralBatteryLite"))
                    return false;
                if (Directory.Exists(legacy)) return false; // resolution must not write.
                File.WriteAllText(Path.Combine(app, PortableMarker),
                    "Portable mode: settings and cache are stored alongside this EXE.\n");
                string portable = ResolveRoot(app, fallback);
                if (portable != app) return false;
                if (File.Exists(Path.Combine(app, CacheFilename)) ||
                    File.Exists(Path.Combine(app, SettingsFilename))) return false;
                File.Delete(Path.Combine(app, PortableMarker));
                return ResolveRoot(app, fallback) == legacy;
            }
            finally
            {
                try { Directory.Delete(temp, true); }
                catch (IOException) {}
                catch (UnauthorizedAccessException) {}
            }
        }
    }
}
