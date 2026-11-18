using System;
using System.Collections.Generic;
using System.Linq;

namespace PeripheralBatteryLite
{
    internal sealed class BatteryInsights
    {
        public int Samples;
        public double? DailyPercent;
        public double? DaysToEmpty;
        public double? DaysToThreshold;
        public double? EstimatedRemainingMah;
        public double? EstimatedAverageMa;

        public static BatteryInsights Calculate(IList<BatterySample> source,
            int? currentPercent, int threshold, int? nominalMah)
        {
            var result = new BatteryInsights();
            if (source == null) return result;
            var samples = source.Where(x => x != null && x.When != DateTime.MinValue)
                .OrderBy(x => x.When).ToList();
            result.Samples = samples.Count;
            if (nominalMah.HasValue && nominalMah.Value > 0 && currentPercent.HasValue)
                result.EstimatedRemainingMah = nominalMah.Value * currentPercent.Value / 100.0;
            if (samples.Count < 3) return result;

            double consumedPct = 0;
            double observedDays = 0;
            int decliningPairs = 0;
            for (int i = 1; i < samples.Count; i++)
            {
                var before = samples[i - 1];
                var after = samples[i];
                double hours = (after.When - before.When).TotalHours;
                // Gaps after an offline PC/charging interval cannot be assigned
                // reliably to continuous discharge; exclude them.
                if (hours < 0.5 || hours > 72 ||
                    before.Charging == true || after.Charging == true ||
                    after.Percent > before.Percent) continue;
                int drop = before.Percent - after.Percent;
                if (drop > 0) decliningPairs++;
                consumedPct += drop;
                observedDays += hours / 24.0;
            }
            // Three observations, two actual decreases, at least half a day
            // and 2% observed discharge avoid displaying false precision.
            if (decliningPairs < 2 || consumedPct < 2 || observedDays < 0.5)
                return result;
            double perDay = consumedPct / observedDays;
            if (perDay < 0.05 || perDay > 100) return result;
            result.DailyPercent = perDay;
            if (currentPercent.HasValue)
            {
                result.DaysToEmpty = currentPercent.Value / perDay;
                result.DaysToThreshold = Math.Max(0, currentPercent.Value - threshold) / perDay;
            }
            if (nominalMah.HasValue && nominalMah.Value > 0)
                result.EstimatedAverageMa = nominalMah.Value * perDay / 2400.0;
            return result;
        }

        public static string Duration(double? days)
        {
            if (!days.HasValue) return "暂无可靠预测";
            if (days.Value <= 0) return "已达到";
            if (days.Value < 1) return "约 " +
                Math.Max(1, Math.Round(days.Value * 24)).ToString("0") + " 小时";
            return "约 " + days.Value.ToString(days.Value < 10 ? "0.0" : "0") + " 天";
        }

        public static bool SelfTest()
        {
            var start = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
            Func<int, int, bool?, BatterySample> make = (days, level, charging) =>
                new BatterySample { DeviceId = "test", Percent = level,
                    Charging = charging, TimeUtc = start.AddDays(days).ToString("o") };
            var data = new List<BatterySample> { make(0, 80, false), make(1, 76, false),
                make(2, 72, false), make(3, 68, false) };
            var stats = Calculate(data, 68, 45, 4000);
            if (!stats.DailyPercent.HasValue ||
                Math.Abs(stats.DailyPercent.Value - 4) > 0.01 ||
                !stats.EstimatedRemainingMah.HasValue ||
                Math.Abs(stats.EstimatedRemainingMah.Value - 2720) > 0.01 ||
                !stats.DaysToThreshold.HasValue ||
                Math.Abs(stats.DaysToThreshold.Value - 5.75) > 0.01 ||
                !stats.EstimatedAverageMa.HasValue ||
                Math.Abs(stats.EstimatedAverageMa.Value - 6.6667) > 0.01)
                return false;
            var chargingHistory = new List<BatterySample> { make(0, 70, false),
                make(1, 95, true), make(2, 90, false) };
            var unknown = Calculate(chargingHistory, 90, 45, null);
            return unknown.DailyPercent == null && unknown.EstimatedRemainingMah == null &&
                Duration(null) == "暂无可靠预测" && Duration(0) == "已达到";
        }
    }
}
