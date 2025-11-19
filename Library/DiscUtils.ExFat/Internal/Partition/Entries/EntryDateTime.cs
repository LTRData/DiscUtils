// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using DiscUtils.ExFat.Internal.Buffers;

namespace DiscUtils.ExFat.Internal.Partition.Entries;
/// <inheritdoc />
/// <summary>
/// Provides <see cref="T:System.DateTime" /> based on DOS <see cref="T:System.UInt32" /> data
/// </summary>
/// <seealso cref="T:System.DateTime" />
public class EntryDateTime : IValueProvider<DateTime>
{
    private readonly IValueProvider<uint> _dateTimeProvider;
    private readonly IValueProvider<byte> _tenMsProvider;

    /// <inheritdoc />
    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    public DateTime Value
    {
        get => DateTimeUtility.FromTimeStamp(_dateTimeProvider.Value, _tenMsProvider != null ? _tenMsProvider.Value : (byte)0);
        set
        {
            var (timeStamp, tenMs) = value.ToTimeStamp();
            
            _dateTimeProvider.Value = timeStamp;
            
            if (_tenMsProvider != null)
            {
                _tenMsProvider.Value = tenMs;
            }
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntryDateTime"/> class.
    /// </summary>
    /// <param name="dateTimeProvider">The date time provider.</param>
    /// <param name="tenMsProvider">The ten ms provider.</param>
    public EntryDateTime(IValueProvider<uint> dateTimeProvider, IValueProvider<byte> tenMsProvider = null)
    {
        _dateTimeProvider = dateTimeProvider;
        _tenMsProvider = tenMsProvider;
    }
}
