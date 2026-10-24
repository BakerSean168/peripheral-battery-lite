using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace PeripheralBatteryLite
{
    // A provider is a self-contained device protocol. A third-party assembly can
    // reference PeripheralBatteryLite.exe and implement this public interface.
    // Loading external DLLs is opt-in: arbitrary DLL code is not sandboxed.
    public enum ActivityCategory { Mouse, Keyboard, Audio, Other }

    public interface IBatteryProvider
    {
        string Id { get; }
        string DisplayName { get; }
        string ShortName { get; }
        int SortOrder { get; }
        ActivityCategory Activity { get; }
        BatteryReading Query(List<HidEndpoint> interfaces);
    }

    internal static class ProviderRegistry
    {
        public static List<string> LoadErrors = new List<string>();

        public static List<IBatteryProvider> Load(bool loadExternal)
        {
            var providers = new List<IBatteryProvider>();
            LoadErrors = new List<string>();
            AddFromAssembly(Assembly.GetExecutingAssembly(), providers);
            if (loadExternal)
            {
                string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
                if (Directory.Exists(directory))
                {
                    foreach (string path in Directory.GetFiles(directory, "*.dll").OrderBy(x => x))
                    {
                        try { AddFromAssembly(Assembly.LoadFrom(path), providers); }
                        catch (Exception e) { LoadErrors.Add(Path.GetFileName(path) + ": " + e.Message); }
                    }
                }
            }
            return providers.GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p.SortOrder).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void AddFromAssembly(Assembly assembly, List<IBatteryProvider> found)
        {
            foreach (Type type in assembly.GetTypes())
            {
                if (!typeof(IBatteryProvider).IsAssignableFrom(type) || type.IsInterface ||
                    type.IsAbstract || !type.IsPublic || type.GetConstructor(Type.EmptyTypes) == null)
                    continue;
                try
                {
                    var provider = (IBatteryProvider)Activator.CreateInstance(type);
                    if (!string.IsNullOrWhiteSpace(provider.Id) && !found.Any(p =>
                        string.Equals(p.Id, provider.Id, StringComparison.OrdinalIgnoreCase)))
                        found.Add(provider);
                }
                catch (Exception e) { LoadErrors.Add(type.FullName + ": " + e.Message); }
            }
        }

        public static bool SelfTest()
        {
            var p = Load(false);
            return p.Count == 3 &&
                   p[0].Id == BatterySnapshotStore.Mouse &&
                   p[1].Id == BatterySnapshotStore.Keyboard &&
                   p[2].Id == BatterySnapshotStore.Headset &&
                   p.Select(x => x.Id).Distinct().Count() == 3;
        }
    }

    public sealed class AtkF1V3Provider : IBatteryProvider
    {
        public string Id { get { return BatterySnapshotStore.Mouse; } }
        public string DisplayName { get { return "ATK F1V3"; } }
        public string ShortName { get { return "F1V3"; } }
        public int SortOrder { get { return 10; } }
        public ActivityCategory Activity { get { return ActivityCategory.Mouse; } }
        public BatteryReading Query(List<HidEndpoint> interfaces) { return BatteryProtocols.ReadMouse(interfaces); }
    }

    public sealed class VxeV87Provider : IBatteryProvider
    {
        public string Id { get { return BatterySnapshotStore.Keyboard; } }
        public string DisplayName { get { return "VXE V87"; } }
        public string ShortName { get { return "V87"; } }
        public int SortOrder { get { return 20; } }
        public ActivityCategory Activity { get { return ActivityCategory.Keyboard; } }
        public BatteryReading Query(List<HidEndpoint> interfaces) { return V87KeyboardBattery.Read(interfaces); }
    }

    public sealed class SteelSeriesNova5Provider : IBatteryProvider
    {
        public string Id { get { return BatterySnapshotStore.Headset; } }
        public string DisplayName { get { return "SteelSeries Arctis Nova 5"; } }
        public string ShortName { get { return "Nova 5"; } }
        public int SortOrder { get { return 30; } }
        public ActivityCategory Activity { get { return ActivityCategory.Audio; } }
        public BatteryReading Query(List<HidEndpoint> interfaces) { return SteelSeriesBattery.Read(interfaces); }
    }
}
