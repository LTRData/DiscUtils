// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace DiscUtils.ExFat;

public static class ExFatSetupHelper
{
    /// <summary>Registers exFAT and the Core defaults without reflection.</summary>
    public static void SetupFileSystems()
    {
        Core.Formats.Register();
        Formats.Register();
    }
}
