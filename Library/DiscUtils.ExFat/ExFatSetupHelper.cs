// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace DiscUtils.ExFat;

public static class ExFatSetupHelper
{
    public static void SetupFileSystems() => Formats.Register();
}
