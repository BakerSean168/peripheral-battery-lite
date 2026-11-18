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
        private const string Version = "0.8.0";
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                if (args.Length > 0) { RunCommand(args); return; }
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

        private static void RunCommand(string[] args)
        {
            string arg = args[0];
            if (arg == "--version")
            {
                Console.WriteLine("Peripheral Battery Lite v" + Version);
                return;
            }
            if (arg == "--self-test")
            {
                bool protocol = BatteryProtocols.SelfTest();
                bool registry = ProviderRegistry.SelfTest();
                bool policy = RefreshPolicy.SelfTest();
                bool settings = AppPreferencesStore.SelfTest();
                bool cache = BatterySnapshotStore.SelfTest();
                bool isolated = IsolatedQueryHost.SelfTest();
                bool storage = AppDataPaths.SelfTest();
                bool staleUi = BatteryPanel.SelfTest();
                bool history = BatteryHistoryStore.SelfTest();
                bool insights = BatteryInsights.SelfTest();
                bool alerts = BatteryAlertStore.SelfTest();
                Console.WriteLine("Protocol parsers: " + (protocol ? "PASS" : "FAIL"));
                Console.WriteLine("Provider registry: " + (registry ? "PASS" : "FAIL"));
                Console.WriteLine("Refresh policy: " + (policy ? "PASS" : "FAIL"));
                Console.WriteLine("Settings persistence: " + (settings ? "PASS" : "FAIL"));
                Console.WriteLine("Battery cache persistence: " + (cache ? "PASS" : "FAIL"));
                Console.WriteLine("Isolated HID response transport: " + (isolated ? "PASS" : "FAIL"));
                Console.WriteLine("Portable storage path resolution: " + (storage ? "PASS" : "FAIL"));
                Console.WriteLine("Stale cache/failure UI status: " + (staleUi ? "PASS" : "FAIL"));
                Console.WriteLine("Battery history and CSV export: " + (history ? "PASS" : "FAIL"));
                Console.WriteLine("Drain and capacity estimates: " + (insights ? "PASS" : "FAIL"));
                Console.WriteLine("Notification state and snooze: " + (alerts ? "PASS" : "FAIL"));
                Environment.ExitCode = protocol && registry && policy && settings && cache &&
                    isolated && storage && staleUi && history && insights && alerts ? 0 : 1;
                return;
            }
            if (arg == "--plugin-self-test")
            {
                var builtin = ProviderRegistry.Load(false);
                var extended = ProviderRegistry.Load(true);
                bool good = builtin.Count == 3 &&
                    extended.Any(p => p.Id == "Example Test Peripheral") &&
                    extended.Count == 4;
                Console.WriteLine(good ? "PASS: external plugin contracts load in diagnostics process" :
                    "FAIL: contracts plugin compatibility: " + string.Join("; ", ProviderRegistry.LoadErrors));
                Environment.ExitCode = good ? 0 : 1;
                return;
            }
            if (arg == "--activity-probe")
            {
                Application.EnableVisualStyles();
                using (var panel = new BatteryPanel())
                using (var activity = new RawInputActivityTracker())
                {
                    bool initiallyUnregistered = !activity.IsRegistered;
                    bool registered = activity.Register(panel.Handle);
                    bool disabled = activity.Unregister() && !activity.IsRegistered;
                    bool reenabled = activity.Register(panel.Handle) && activity.IsRegistered;
                    bool stopped = activity.Unregister() && !activity.IsRegistered;
                    bool ok = initiallyUnregistered && registered && disabled && reenabled && stopped;
                    Console.WriteLine(ok ? "RAW_INPUT_TOGGLE=PASS" :
                        "RAW_INPUT_TOGGLE=FAIL");
                    Environment.ExitCode = ok ? 0 : 2;
                }
                return;
            }
            if (arg == "--ui-self-test")
            {
                Application.EnableVisualStyles();
                using (var panel = new BatteryPanel())
                {
                    panel.UpdateData(new List<BatteryReading>(), ProviderRegistry.Load(false),
                        new AppPreferences());
                    string selected = null;
                    panel.DetailsRequested += id => selected = id;
                    var hit = typeof(BatteryPanel).GetMethod("OnMouseClick",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);
                    hit.Invoke(panel, new object[] {
                        new MouseEventArgs(MouseButtons.Left, 1, 38, 83 + 88 + 25, 0) });
                    bool good = selected == BatterySnapshotStore.Keyboard;
                    Console.WriteLine(good ? "UI_CARD_CLICK=PASS" : "UI_CARD_CLICK=FAIL");
                    Environment.ExitCode = good ? 0 : 1;
                }
                return;
            }
            if (arg == "--render-settings" || arg == "--render-settings-data")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var settings = new SettingsForm(new AppPreferences(),
                    false, 3, true))
                {
                    settings.Show();
                    if (arg == "--render-settings-data")
                        settings.Controls.OfType<TabControl>().First().SelectedIndex = 1;
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(settings.Width, settings.Height))
                    {
                        settings.DrawToBitmap(bitmap,
                            new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        string location = Path.Combine(Path.GetTempPath(),
                            arg == "--render-settings-data" ?
                            "PeripheralBatteryLite-settings-data-preview.png" :
                            "PeripheralBatteryLite-settings-preview.png");
                        bitmap.Save(location, ImageFormat.Png);
                        Console.WriteLine("PREVIEW=" + location);
                    }
                    settings.Hide();
                }
                return;
            }
            if (arg == "--render-detail" || arg == "--render-detail-settings" ||
                arg == "--render-detail-notifications")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var prefs = new AppPreferencesStore().Load();
                var providers = ProviderRegistry.Load(false);
                string id = args.Length > 1 ? args[1] : BatterySnapshotStore.Mouse;
                var provider = providers.FirstOrDefault(p => p.Id == id);
                if (provider == null) { Console.WriteLine("Unknown device"); Environment.ExitCode = 2; return; }
                var cached = new BatterySnapshotStore().Snapshot();
                var snapshot = cached.FirstOrDefault(x => x.Name == id);
                using (var form = new DeviceDetailForm(id, provider.DisplayName, snapshot,
                    prefs, new BatteryHistoryStore(), new BatteryAlertStore()))
                {
                    form.Show();
                    if (arg == "--render-detail-settings" || arg == "--render-detail-notifications")
                        form.Controls.OfType<TabControl>().First().SelectedIndex =
                            arg == "--render-detail-settings" ? 1 : 2;
                    Application.DoEvents();
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
                        string location = Path.Combine(Path.GetTempPath(),
                            arg == "--render-detail-settings" ?
                            "PeripheralBatteryLite-detail-settings-preview.png" :
                            arg == "--render-detail-notifications" ?
                            "PeripheralBatteryLite-detail-notifications-preview.png" :
                            "PeripheralBatteryLite-detail-preview.png");
                        bitmap.Save(location, ImageFormat.Png);
                        Console.WriteLine("PREVIEW=" + location);
                    }
                    form.Hide();
                }
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
            if (arg == "--cache-self-test" || arg == "--scheduler-self-test" ||
                arg == "--analysis-self-test")
            {
                bool ok = arg == "--cache-self-test" ? BatterySnapshotStore.SelfTest() :
                    arg == "--scheduler-self-test" ? RefreshPolicy.SelfTest() :
                    BatteryHistoryStore.SelfTest() && BatteryInsights.SelfTest() &&
                    BatteryAlertStore.SelfTest();
                Console.WriteLine(ok ? "PASS" : "FAIL");
                Environment.ExitCode = ok ? 0 : 1;
                return;
            }
            if (arg == "--storage-paths")
            {
                Console.WriteLine("MODE=" + (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                    AppDataPaths.PortableMarker)) ? "PORTABLE" : "LEGACY"));
                Console.WriteLine("ROOT=" + AppDataPaths.ResolveRoot());
                Console.WriteLine("CACHE_PATH=" + AppDataPaths.CachePath);
                Console.WriteLine("SETTINGS_PATH=" + AppDataPaths.SettingsPath);
                Console.WriteLine("HISTORY_PATH=" + AppDataPaths.HistoryPath);
                Console.WriteLine("ALERTS_PATH=" + AppDataPaths.AlertsPath);
                return;
            }
            if (arg == "--history-show")
            {
                var history = new BatteryHistoryStore();
                var prefs = new AppPreferencesStore().Load();
                foreach (var p in ProviderRegistry.Load(false))
                {
                    var samples = history.Recent(p.Id, 30);
                    Console.WriteLine(p.Id + ": " + samples.Count + " readings (30d)");
                }
                Console.WriteLine("HISTORY_PATH=" + AppDataPaths.HistoryPath);
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
                Console.WriteLine("quiet_hours=" + p.QuietHoursEnabled);
                Console.WriteLine("tray_icon_device=" + p.TrayIconDeviceId);
                foreach (var device in p.Devices)
                    Console.WriteLine("device=" + device.DeviceId +
                        " alerts=" + device.AlertsEnabled +
                        " threshold=" + device.AlertThreshold +
                        " policy=" + device.AutoQueryMode);
                Console.WriteLine("only_when_device_active=" + p.OnlyWhenDeviceActive);
                Console.WriteLine("external_dll_plugins=" + p.EnableExternalPlugins);
                Console.WriteLine("startup=" + StartupRegistration.Enabled);
                return;
            }
            if (arg == "--provider-query" && args.Length > 1)
            {
                // Internal child-process entry point, invoked only by the
                // supervising UI. This is the sole path that directly touches
                // the HID driver; the parent enforces a hard timeout.
                var providers = ProviderRegistry.Load(
                    new AppPreferencesStore().Load().EnableExternalPlugins);
                var selected = providers.FirstOrDefault(p => p.Id == args[1]);
                if (selected == null) { Environment.ExitCode = 2; return; }
                var values = BatteryCoordinator.Query(new List<IBatteryProvider> { selected });
                Console.WriteLine(values[0].Json);
                Environment.ExitCode = values[0].Status == "OK" ? 0 : 2;
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
                var readings = IsolatedQueryHost.Query(p);
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
            Console.WriteLine("Peripheral Battery Lite: --self-test | --history-show | --analysis-self-test | --cache-show | --settings-show |" +
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
