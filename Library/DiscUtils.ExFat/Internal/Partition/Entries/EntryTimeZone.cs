// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using DiscUtils.ExFat.Internal.Buffers;

namespace DiscUtils.ExFat.Internal.Partition.Entries;
/// <summary>
/// Allows to specify time zones
/// </summary>
/// <seealso cref="TimeSpan" />
public class EntryTimeZone : IValueProvider<TimeSpan>
{
    private readonly IValueProvider<byte> _timeZoneOffsetProvider;

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    public TimeSpan Value
    {
        get => DateTimeUtility.FromTimeZoneOffset(_timeZoneOffsetProvider.Value);
        set => _timeZoneOffsetProvider.Value = value.ToTimeZoneOffset();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntryTimeZone"/> class.
    /// </summary>
    /// <param name="timeZoneOffsetProvider">The time zone offset provider.</param>
    public EntryTimeZone(IValueProvider<byte> timeZoneOffsetProvider)
    {
        _timeZoneOffsetProvider = timeZoneOffsetProvider;
    }
}