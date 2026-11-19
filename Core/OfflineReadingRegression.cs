using System;
using System.IO;
using System.Linq;

namespace PeripheralBatteryLite
{
    // Full offline => cache => history => notification regression, without
    // touching real USB receivers or the user's on-disk state.
    internal static class OfflineReadingRegression
    {
        public static bool SelfTest()
        {
            string folder = Path.Combine(Path.GetTempPath(),
                "PBL-offline-regression-" + Guid.NewGuid().ToString("N"));
            try
            {
                var cache = new BatterySnapshotStore(Path.Combine(folder, "cache.json"));
                var history = new BatteryHistoryStore(Path.Combine(folder, "history.json"));
                var alerts = new BatteryAlertStore(Path.Combine(folder, "alerts.json"));
                var prefs = new AppPreferences();
                var config = prefs.GetDevice(BatterySnapshotStore.Headset);
                config.AlertsEnabled = true;
                config.AlertThreshold = 45;

                string id = BatterySnapshotStore.Headset;
                DateTime when = DateTime.UtcNow;
                cache.MergeAndSave(new[] {
                    new BatteryReading { Name = id, Status = "OK",
                        Percent = 47, Charging = false }
                });
                if (cache.LastSaveError != null ||
                    !history.Record(id, 47, false, when)) return false;
                alerts.Observe(id, 47, false, config);
                var original = cache.Snapshot().First(x => x.Name == id);

                // The dongle answers 0% but says its Nova 5 is powered off.
                var frame = new byte[65];
                frame[1] = 0xB0;
                frame[2] = 0x02;
                frame[4] = 0;
                frame[5] = 3;
                int percent;
                bool? charging;
                if (SteelSeriesBattery.Parse(frame, out percent, out charging))
                    return false;
                cache.MergeAndSave(new[] {
                    new BatteryReading { Name = id, Status = "Device offline" }
                });
                var offline = new BatterySnapshotStore(Path.Combine(folder, "cache.json"))
                    .Snapshot().First(x => x.Name == id);
                if (offline.Status != "Device offline" || offline.Percent != 47 ||
                    offline.LastSuccessUtc != original.LastSuccessUtc ||
                    offline.LastQueryUtc < offline.LastSuccessUtc ||
                    new BatteryHistoryStore(Path.Combine(folder, "history.json"))
                        .Recent(id).Count != 1 ||
                    alerts.DeliverPending(prefs, new[] { offline }, DateTime.UtcNow).Count != 0 ||
                    alerts.RecentNotifications(id).Count != 0)
                    return false;

                // A genuine zero with a connected-state report is different:
                // it must be recorded, and may generate a low-battery warning.
                frame[2] = 0x04;
                if (!SteelSeriesBattery.Parse(frame, out percent, out charging) ||
                    percent != 0 || charging != false) return false;
                cache.MergeAndSave(new[] {
                    new BatteryReading { Name = id, Status = "OK",
                        Percent = percent, Charging = charging }
                });
                if (!history.Record(id, 0, false, when.AddMinutes(1))) return false;
                alerts.Observe(id, 0, false, config);
                var connected = cache.Snapshot().First(x => x.Name == id);
                var notifications = alerts.DeliverPending(prefs,
                    new[] { connected }, DateTime.UtcNow);
                return notifications.Count == 1 && notifications[0].Percent == 0 &&
                    history.Recent(id).Count == 2 &&
                    alerts.DeliverPending(prefs, new[] { connected }, DateTime.UtcNow).Count == 0;
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch (IOException) {}
            }
        }
    }
}
