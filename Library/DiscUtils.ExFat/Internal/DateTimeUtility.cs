// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;

namespace DiscUtils.ExFat.Internal;
/// <summary>
/// Utility for <see cref="DateTime"/>
/// </summary>
public static class DateTimeUtility
{
    /// <summary>
    /// Converts a time stamp to <see cref="DateTime"/>.
    /// </summary>
    /// <param name="timeStamp">The time stamp.</param>
    /// <param name="tenMs">The ten ms.</param>
    /// <returns></returns>
    public static DateTime FromTimeStamp(uint timeStamp, byte tenMs)
    {
        var twoSeconds = (int)timeStamp & 0x1F; // 0-4 - 5 bits
        var minute = (int)(timeStamp >> 5) & 0x3F; // 5-10 - 6 bits
        var hour = (int)(timeStamp >> 11) & 0x1F; // 11-15 - 5 bits
        var day = (int)(timeStamp >> 16) & 0x1F; // 16-20 - 5 bits
        var month = (int)(timeStamp >> 21) & 0x0F; // 21-24 - 4 bits
        var year = (int)(timeStamp >> 25) & 0x7F; // 25-31 - 7 bits
        var seconds = twoSeconds * 2 + tenMs / 100;
        var milliseconds = tenMs % 100 * 10;
        return new DateTime(year + 1980, month, day, hour, minute, seconds, milliseconds, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// Converts a <see cref="DateTime"/> to time stamp.
    /// </summary>
    /// <param name="dateTime">The date time.</param>
    /// <returns></returns>
    public static (uint timeStamp, byte tenMs) ToTimeStamp(this DateTime dateTime)
    {
        var timeStamp = (dateTime.Year - 1980) << 25
                        | dateTime.Month << 21
                        | dateTime.Day << 16
                        | dateTime.Hour << 11
                        | dateTime.Minute << 5
                        | dateTime.Second >> 1;
        var tenMs = dateTime.Millisecond / 10 + dateTime.Second % 2 * 100;
        return ((uint)timeStamp, (byte)tenMs);
    }

    /// <summary>
    /// Converts an exFAT time offset to <see cref="TimeSpan"/>.
    /// </summary>
    /// <param name="offset">The offset.</param>
    /// <returns></returns>
    public static TimeSpan FromTimeZoneOffset(byte offset)
    {
        if (offset < 0x80)
        {
            return TimeSpan.Zero;
        }

        double hoursOffset;
        if (offset < 0xD0)
        {
            hoursOffset = (offset - 0x80) * 0.25;
        }
        else
        {
            hoursOffset = (offset - 0x100) * 0.25;
        }

        var timeSpanOffset = TimeSpan.FromHours(hoursOffset);
        return timeSpanOffset;
    }

    /// <summary>
    /// Converts a <see cref="TimeSpan" /> to time zone offset byte.
    /// </summary>
    /// <param name="timeSpanOffset">The time span offset.</param>
    /// <returns></returns>
    public static byte ToTimeZoneOffset(this TimeSpan timeSpanOffset)
    {
        var quartersOffset = (int)timeSpanOffset.TotalHours * 4;
        if (quartersOffset < 0)
        {
            return (byte)(0x100 + quartersOffset);
        }

        return (byte)(0x80 + quartersOffset);
    }

    /// <summary>
    /// Adds offset to date and returns a <see cref="DateTimeOffset"/>, whatever the offset is.
    /// Because .NET allows only to work with current offset (why?)
    /// </summary>
    /// <param name="dateTime">The date time.</param>
    /// <param name="offset">The offset.</param>
    /// <returns></returns>
    public static DateTimeOffset ToDateTimeOffset(this DateTime dateTime, TimeSpan offset)
    {
        // unspecifed in our ExFat context means "local with unspecified zone", so it's the base to local, thus is it like local+0, which is UTC
        if (dateTime.Kind == DateTimeKind.Unspecified)
        {
            dateTime = new DateTime(dateTime.Ticks, DateTimeKind.Utc);
        }

        var dateTimeOffset = new DateTimeOffset(dateTime).ToOffset(offset);
        return dateTimeOffset;
    }
}
