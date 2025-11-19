// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;

namespace DiscUtils.ExFat.Internal;
/// <summary>
/// Utility for enums
/// </summary>
public static class EnumUtility
{
    /// <summary>
    /// Determines whether the specified value has flag.
    /// </summary>
    /// <typeparam name="TEnum">The type of the enum.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="check">The check.</param>
    /// <returns>
    ///   <c>true</c> if the specified value has flag; otherwise, <c>false</c>.
    /// </returns>
    public static bool HasAny<TEnum>(this TEnum value, TEnum check)
        where TEnum : struct, IConvertible
    {
        var intCheck = check.ToInt32(null);
        var intValue = value.ToInt32(null);
        return (intValue & intCheck) != 0;
    }

    /// <summary>
    /// Determines whether the specified value has all required flags.
    /// </summary>
    /// <typeparam name="TEnum">The type of the enum.</typeparam>
    /// <param name="value">The value.</param>
    /// <param name="check">The check.</param>
    /// <returns></returns>
    public static bool HasAll<TEnum>(this TEnum value, TEnum check)
        where TEnum : struct, IConvertible
    {
        var intCheck = check.ToInt32(null);
        var intValue = value.ToInt32(null);
        return (intValue & intCheck) == intCheck;
    }
}
