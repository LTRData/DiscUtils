// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

namespace DiscUtils.ExFat.Internal.Buffers;

/// <summary>
/// Creates a cache value provider
/// </summary>
/// <typeparam name="TValue">The type of the value.</typeparam>
/// <seealso cref="IValueProvider{TValue}" />
public class CacheValueProvider<TValue> : IValueProvider<TValue>
{
    private readonly IValueProvider<TValue> valueProvider;

    private bool _valueSet;
    private TValue _value;

    public TValue Value
    {
        get
        {
            if (!_valueSet)
            {
                _value = valueProvider.Value;
                _valueSet = true;
            }

            return _value;
        }
        set => valueProvider.Value = _value = value;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheValueProvider{TValue}"/> class.
    /// </summary>
    /// <param name="valueProvider">The value provider.</param>
    public CacheValueProvider(IValueProvider<TValue> valueProvider)
    {
        this.valueProvider = valueProvider;
    }
}