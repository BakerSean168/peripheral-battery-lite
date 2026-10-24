using System;
using System.Collections.Generic;
using System.Linq;

namespace PeripheralBatteryLite
{
    internal static class BatteryCoordinator
    {
        public static List<BatteryReading> Query(IList<IBatteryProvider> providers)
        {
            var result = new List<BatteryReading>();
            if (providers == null || providers.Count == 0) return result;

            List<HidEndpoint> endpoints;
            bool externalProviders = providers.Any(p =>
                p.GetType().Assembly != typeof(BatteryCoordinator).Assembly);
            try { endpoints = HidWindows.Enumerate(externalProviders); }
            catch (Exception error)
            {
                return providers.Select(p => new BatteryReading
                {
                    Name = p.Id, Status = "Device enumeration failed",
                    Detail = error.GetType().Name + ": " + error.Message
                }).ToList();
            }

            foreach (var provider in providers)
            {
                try
                {
                    BatteryReading r = provider.Query(endpoints);
                    if (r == null) throw new InvalidOperationException("Provider returned no response");
                    r.Name = provider.Id; // Stable ID is determined by registry, not packet content.
                    result.Add(r);
                }
                catch (Exception error)
                {
                    result.Add(new BatteryReading
                    {
                        Name = provider.Id, Status = "Provider failed",
                        Detail = error.GetType().Name + ": " + error.Message
                    });
                }
            }
            return result;
        }

        public static List<IBatteryProvider> SelectDue(IList<IBatteryProvider> all,
            List<BatteryReading> cached, AppPreferences prefs, RawInputActivityTracker activity,
            DateTime nowUtc)
        {
            var selected = new List<IBatteryProvider>();
            if (prefs.IntervalMinutes == 0) return selected;
            foreach (var provider in all)
            {
                var previous = cached.FirstOrDefault(r => r.Name == provider.Id);
                DateTime? lastAttempt = previous == null ? null : previous.LastQueryUtc;
                bool recent = activity != null && activity.RecentlyActive(provider.Activity, nowUtc);
                if (RefreshPolicy.IsDue(lastAttempt, prefs.IntervalMinutes, nowUtc) &&
                    RefreshPolicy.EligibleForAutomaticQuery(provider,
                        prefs.OnlyWhenDeviceActive, recent))
                    selected.Add(provider);
            }
            return selected;
        }
    }
}
