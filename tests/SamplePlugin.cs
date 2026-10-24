using System.Collections.Generic;
using PeripheralBatteryLite;

// A harmless test fixture. It is compiled only by Windows CI and is not
// included in the published portable package.
public sealed class ExampleBatteryProvider : IBatteryProvider
{
    public string Id { get { return "Example Test Peripheral"; } }
    public string DisplayName { get { return "Example Test Peripheral"; } }
    public string ShortName { get { return "Example"; } }
    public int SortOrder { get { return 999; } }
    public ActivityCategory Activity { get { return ActivityCategory.Other; } }

    public BatteryReading Query(List<HidEndpoint> interfaces)
    {
        return new BatteryReading
        {
            Name = Id, Percent = 42, Charging = false, Status = "OK"
        };
    }
}
