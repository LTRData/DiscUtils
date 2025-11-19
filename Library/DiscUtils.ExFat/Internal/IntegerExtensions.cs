// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace DiscUtils.ExFat.Internal;
/// <summary>
/// Extensions to <see cref="int"/> and friends
/// </summary>
public static class IntegerExtensions
{
    /// <summary>
    /// Rotates 1 bit right.
    /// </summary>
    /// <param name="v">The v.</param>
    /// <returns></returns>
    public static ushort RotateRight(this ushort v) => (ushort)((v << 15) | (v >> 1));

    /// <summary>
    /// Rotates 1 bit right.
    /// </summary>
    /// <param name="v">The v.</param>
    /// <returns></returns>
    public static uint RotateRight(this uint v) => (v << 31) | (v >> 1);
}