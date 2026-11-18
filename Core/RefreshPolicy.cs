using System;
using System.Linq;

namespace PeripheralBatteryLite
{
    public static class RefreshPolicy
    {
        // Manual mode and unrestricted periodic polling do not need to track
        // keyboard/mouse events, so no Raw Input registration is necessary.
        public static bool RequiresActivityTracking(AppPreferences preferences)
        {
            if (preferences == null || preferences.IntervalMinutes == 0) return false;
            foreach (var id in new[] { BatterySnapshotStore.Mouse, BatterySnapshotStore.Keyboard })
            {
                string mode = preferences.GetDevice(id).AutoQueryMode;
                if (mode == "active" || (mode == "inherit" && preferences.OnlyWhenDeviceActive))
                    return true;
            }
            // An external plugin may request activity-only mode too.
            return preferences.Devices != null && preferences.Devices.Any(d =>
                d != null && d.AutoQueryMode == "active");
        }

        internal static bool ShouldAutoQuery(IBatteryProvider provider, AppPreferences prefs,
            RawInputActivityTracker activity, DateTime nowUtc)
        {
            string mode = prefs.GetDevice(provider.Id).AutoQueryMode;
            if (mode == "manual") return false;
            if (mode == "always") return true;
            bool onlyActive = mode == "active" || (mode == "inherit" && prefs.OnlyWhenDeviceActive);
            return EligibleForAutomaticQuery(provider, onlyActive,
                activity != null && activity.RecentlyActive(provider.Activity, nowUtc));
        }

        public static bool IsDue(DateTime? lastQueryUtc, int intervalMinutes, DateTime nowUtc)
        {
            if (intervalMinutes <= 0) return false;
            return !lastQueryUtc.HasValue ||
                   nowUtc - lastQueryUtc.Value.ToUniversalTime() >= TimeSpan.FromMinutes(intervalMinutes);
        }

        public static bool EligibleForAutomaticQuery(IBatteryProvider provider, bool onlyActive,
            bool recentlyActive)
        {
            if (!onlyActive) return true;
            // Headsets cannot be recognized as awake just because their USB
            // dongle is enumerated. Never query them in activity-only mode.
            if (provider.Activity == ActivityCategory.Audio ||
                provider.Activity == ActivityCategory.Other)
                return false;
            return recentlyActive;
        }

        private static bool TestPerDeviceModes(IBatteryProvider mouse, IBatteryProvider headset)
        {
            var prefs = new AppPreferences { IntervalMinutes = 240 };
            prefs.GetDevice(mouse.Id).AutoQueryMode = "manual";
            prefs.GetDevice(headset.Id).AutoQueryMode = "always";
            bool modes = !ShouldAutoQuery(mouse, prefs, null, DateTime.UtcNow) &&
                ShouldAutoQuery(headset, prefs, null, DateTime.UtcNow) &&
                RequiresActivityTracking(prefs); // The unmodified keyboard still inherits activity gating.
            prefs.GetDevice(BatterySnapshotStore.Keyboard).AutoQueryMode = "manual";
            return modes && !RequiresActivityTracking(prefs);
        }

        public static bool SelfTest()
        {
            DateTime now = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);
            var mouse = new AtkF1V3Provider();
            var headset = new SteelSeriesNova5Provider();
            var preferences = new AppPreferences();
            bool manualDoesNotListen = !RequiresActivityTracking(preferences);
            preferences.IntervalMinutes = 30;
            bool gatedPollingListens = RequiresActivityTracking(preferences);
            preferences.OnlyWhenDeviceActive = false;
            bool ungatedPollingDoesNotListen = !RequiresActivityTracking(preferences);
            return manualDoesNotListen && gatedPollingListens &&
                   ungatedPollingDoesNotListen && !IsDue(null, 0, now) &&
                   IsDue(null, 30, now) &&
                   !IsDue(now.AddMinutes(-29), 30, now) &&
                   IsDue(now.AddMinutes(-30), 30, now) &&
                   !IsDue(now.AddMinutes(-59), 60, now) &&
                   IsDue(now.AddMinutes(-60), 60, now) &&
                   !IsDue(now.AddMinutes(-119), 120, now) &&
                   IsDue(now.AddMinutes(-120), 120, now) &&
                   !IsDue(now.AddMinutes(-239), 240, now) &&
                   IsDue(now.AddMinutes(-240), 240, now) &&
                   !IsDue(now.AddMinutes(-299), 300, now) &&
                   IsDue(now.AddMinutes(-300), 300, now) &&
                   !EligibleForAutomaticQuery(mouse, true, false) &&
                   EligibleForAutomaticQuery(mouse, true, true) &&
                   !EligibleForAutomaticQuery(headset, true, true) &&
                   EligibleForAutomaticQuery(headset, false, false) &&
                   TestPerDeviceModes(mouse, headset);
        }
    }
}
