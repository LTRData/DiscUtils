// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using DiscUtils.ExFat.Internal.Buffers;

namespace DiscUtils.ExFat.Internal.Partition.Entries;
/// <inheritdoc />
/// <summary>
/// Provides <see cref="T:System.DateTimeOffset" /> based on <see cref="T:System.DateTime" /> and <see cref="T:System.TimeSpan" /> sources
/// </summary>
/// <seealso cref="DateTimeOffset" />
public class EntryDateTimeOffset : IValueProvider<DateTimeOffset>
{
    private readonly IValueProvider<DateTime> _dateTimeProvider;
    private readonly IValueProvider<TimeSpan> _offsetProvider;

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    public DateTimeOffset Value
    {
        get
        {
            var offset = _offsetProvider.Value;
            // the provided date is local, so first, we shift it to UTC, then add the offset
            var dateTime = new DateTime((_dateTimeProvider.Value - offset).Ticks, DateTimeKind.Utc);
            return dateTime.ToDateTimeOffset(offset);
        }
        set
        {
            // DateTime member is the local, and this is what we expect
            _dateTimeProvider.Value = value.DateTime;
            _offsetProvider.Value = value.Offset;
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntryDateTimeOffset"/> class.
    /// </summary>
    /// <param name="dateTimeProvider">The date time provider.</param>
    /// <param name="offsetProvider">The offset provider.</param>
    public EntryDateTimeOffset(IValueProvider<DateTime> dateTimeProvider, IValueProvider<TimeSpan> offsetProvider)
    {
        _dateTimeProvider = dateTimeProvider;
        _offsetProvider = offsetProvider;
    }
}