// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace DiscUtils.ExFat.Internal.Buffers;

/// <summary>
/// Anything that provides a value from stored data.
/// Provider implementations can be nested.
/// </summary>
/// <typeparam name="TValue">The type of the value.</typeparam>
public interface IValueProvider<TValue>
{
    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    TValue Value { get; set; }
}