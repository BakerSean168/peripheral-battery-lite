using System.Collections.Generic;

namespace PeripheralBatteryLite
{
    // Shared between the tray EXE, diagnostic child EXE, and optional plugin DLLs.
    // Built against .NET Framework 4.8 and loaded from Contracts.dll.
    public enum ActivityCategory { Mouse, Keyboard, Audio, Other }

    public interface IBatteryProvider
    {
        // Stable cache key. Do not rename this between versions.
        string Id { get; }
        string DisplayName { get; }
        string ShortName { get; }
        int SortOrder { get; }
        ActivityCategory Activity { get; }
        BatteryReading Query(List<HidEndpoint> interfaces);
    }
}
