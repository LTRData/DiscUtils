using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace DiscUtils.Internal;

internal sealed class FastDictionary<T> : KeyedCollection<string, T>, IReadOnlyDictionary<string, T>
{
    private readonly Func<T, string> _keySelector;

    protected override string GetKeyForItem(T item) => _keySelector(item);

    public FastDictionary(IEqualityComparer<string> comparer, Func<T, string> keySelector)
        : base(comparer)
    {
        _keySelector = keySelector;
    }

    public IEnumerable<string> Keys => this.Select<T, string>(GetKeyForItem);

    public IEnumerable<T> Values => this;

    public bool ContainsKey(string key) => Contains(key);

#if !NETSTANDARD2_1_OR_GREATER && !NETCOREAPP
#pragma warning disable CS8767 // Nullability of reference types in type of parameter doesn't match implicitly implemented member
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out T value)
#pragma warning restore CS8767
    {
        if (Contains(key))
        {
            value = this[key];
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    [return: MaybeNull]
    public T GetValueOrDefault(string key)
    {
        if (Contains(key))
        {
            return this[key];
        }
        else
        {
            return default;
        }
    }
#endif

    IEnumerator<KeyValuePair<string, T>> IEnumerable<KeyValuePair<string, T>>.GetEnumerator()
        => this.Select<T, KeyValuePair<string, T>>(item => new(GetKeyForItem(item), item)).GetEnumerator();
}
