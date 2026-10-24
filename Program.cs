using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace PeripheralBatteryLite
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                if (args.Length > 0) { RunCommand(args[0]); return; }
                bool acquired;
                using (var lockInstance = new Mutex(true,
                    @"Local\PeripheralBatteryLite-9E1C819F", out acquired))
                {
                    if (!acquired) return;
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    using (var app = new BatteryTrayApp()) Application.Run(app);
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.GetType().Name + ": " + e.Message);
                Environment.ExitCode = 3;
            }
        }

        private static void RunCommand(string arg)
        {
            if (arg == "--self-test")
            {
                bool protocol = BatteryProtocols.SelfTest();
                bool registry = ProviderRegistry.SelfTest();
                bool policy = RefreshPolicy.SelfTest();
                bool settings = AppPreferencesStore.SelfTest();
                bool cache = BatterySnapshotStore.SelfTest();
                Console.WriteLine("Protocol parsers: " + (protocol ? "PASS" : "FAIL"));
                Console.WriteLine("Provider registry: " + (registry ? "PASS" : "FAIL"));
                Console.WriteLine("Refresh policy: " + (policy ? "PASS" : "FAIL"));
                Console.WriteLine("Settings persistence: " + (settings ? "PASS" : "FAIL"));
                Console.WriteLine("Battery cache persistence: " + (cache ? "PASS" : "FAIL"));
                Environment.ExitCode = protocol && registry && policy && settings && cache ? 0 : 1;
                return;
            }
            if (arg == "--render-preview")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var providers = ProviderRegistry.Load(false);
                var cached = new BatterySnapshotStore().Snapshot(providers.Select(x => x.Id));
                using (var panel = new BatteryPanel())
                {
                    panel.UpdateData(cached, providers, new AppPreferencesStore().Load());
                    panel.Show();
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(panel.Width, panel.Height))
                    {
                        panel.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        string location = Path.Combine(Path.GetTempPath(), "PeripheralBatteryLite-preview.png");
                        bitmap.Save(location, ImageFormat.Png);
                        Console.WriteLine("PREVIEW=" + location);
                    }
                    panel.Hide();
                }
                return;
            }
            if (arg == "--cache-self-test" || arg == "--scheduler-self-test")
            {
                bool ok = arg == "--cache-self-test" ?
                    BatterySnapshotStore.SelfTest() : RefreshPolicy.SelfTest();
                Console.WriteLine(ok ? "PASS" : "FAIL");
                Environment.ExitCode = ok ? 0 : 1;
                return;
            }
            if (arg == "--cache-show")
            {
                var snapshot = new BatterySnapshotStore();
                foreach (var reading in snapshot.Snapshot())
                    PrintReading(reading);
                Console.WriteLine("CACHE_PATH=" + snapshot.FilePath);
                return;
            }
            if (arg == "--settings-show")
            {
                var preferences = new AppPreferencesStore();
                var p = preferences.Load();
                Console.WriteLine("poll_interval_minutes=" + p.IntervalMinutes);
                Console.WriteLine("only_when_device_active=" + p.OnlyWhenDeviceActive);
                Console.WriteLine("external_dll_plugins=" + p.EnableExternalPlugins);
                Console.WriteLine("startup=" + StartupRegistration.Enabled);
                return;
            }
            if (arg == "--probe")
            {
                Console.WriteLine(BatteryProtocols.Probe());
                return;
            }
            if (arg == "--once" || arg == "--refresh-cache" ||
                arg == "--mouse-only" || arg == "--keyboard-only" ||
                arg == "--headset-only")
            {
                var p = ProviderRegistry.Load(new AppPreferencesStore().Load().EnableExternalPlugins);
                if (arg == "--mouse-only")
                    p = p.Where(x => x.Id == BatterySnapshotStore.Mouse).ToList();
                else if (arg == "--keyboard-only")
                    p = p.Where(x => x.Id == BatterySnapshotStore.Keyboard).ToList();
                else if (arg == "--headset-only")
                    p = p.Where(x => x.Id == BatterySnapshotStore.Headset).ToList();
                var readings = BatteryCoordinator.Query(p);
                if (arg == "--refresh-cache")
                {
                    var store = new BatterySnapshotStore();
                    store.MergeAndSave(readings);
                    foreach (var reading in store.Snapshot()) PrintReading(reading);
                    if (store.LastSaveError != null)
                    {
                        Console.WriteLine("SAVE_ERROR=" + store.LastSaveError);
                        Environment.ExitCode = 2;
                    }
                }
                else
                {
                    foreach (var reading in readings) PrintReading(reading);
                    if (readings.Any(r => !r.Percent.HasValue)) Environment.ExitCode = 2;
                }
                return;
            }
            Console.WriteLine("Peripheral Battery Lite: --self-test | --cache-show | --settings-show |" +
                " --scheduler-self-test | --probe | --once | --refresh-cache |" +
                " --mouse-only | --keyboard-only | --headset-only");
        }

        private static void PrintReading(BatteryReading reading)
        {
            Console.WriteLine(reading.Json +
                " last_success_utc=" + (reading.LastSuccessUtc.HasValue ?
                    reading.LastSuccessUtc.Value.ToString("o") : "null") +
                " last_attempt_utc=" + (reading.LastQueryUtc.HasValue ?
                    reading.LastQueryUtc.Value.ToString("o") : "null"));
        }
    }
}
