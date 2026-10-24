using System;

namespace PeripheralBatteryLite
{
    public sealed class BatteryReading
    {
        public string Name, Status, Connection;
        public int? Percent;
        public bool? Charging;
        public DateTime? LastSuccessUtc;
        public DateTime? LastQueryUtc;
        public ushort Vid, Pid;
        public string Detail;
        public string Display { get { return Percent.HasValue ? Percent.Value + "%" : "--"; } }
        public string Json
        {
            get
            {
                string pct = Percent.HasValue ? Percent.Value.ToString() : "null";
                string ch = Charging.HasValue ? (Charging.Value ? "true" : "false") : "null";
                return "{\"name\":\"" + Name + "\",\"vid_pid\":\"" + Vid.ToString("X4") + ":" + Pid.ToString("X4")
                    + "\",\"percent\":" + pct + ",\"charging\":" + ch
                    + ",\"status\":\"" + Status + "\"}";
            }
        }
    }
}
