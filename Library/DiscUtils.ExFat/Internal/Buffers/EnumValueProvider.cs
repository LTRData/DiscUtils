// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System.Diagnostics;

namespace DiscUtils.ExFat.Internal.Buffers;
/// <summary>
/// enum mapper, with variable sizez
/// </summary>
/// <typeparam name="TEnum">The type of the enum.</typeparam>
/// <typeparam name="TBacking">The type of the backing.</typeparam>
/// <seealso cref="IValueProvider{TEnum}" />
[DebuggerDisplay("{" + nameof(Value) + "}")]
public class EnumValueProvider<TEnum, TBacking> : IValueProvider<TEnum>
{
    private readonly IValueProvider<TBacking> backingValueProvider;

    /// <summary>
    /// Gets or sets the value.
    /// </summary>
    /// <value>
    /// The value.
    /// </value>
    public TEnum Value
    {
        // the casts are a bit dirty here, however they do the job
        get => (TEnum)(object)backingValueProvider.Value;
        set => backingValueProvider.Value = (TBacking)(object)value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnumValueProvider{TEnum, TBacking}"/> class.
    /// </summary>
    /// <param name="backingValueProvider">The backing value provider.</param>
    public EnumValueProvider(IValueProvider<TBacking> backingValueProvider)
    {
        this.backingValueProvider = backingValueProvider;
    }
}