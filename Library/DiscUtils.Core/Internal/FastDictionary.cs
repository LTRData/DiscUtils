using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    public bool TryGetValue(string key, out T value)
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
#endif

    IEnumerator<KeyValuePair<string, T>> IEnumerable<KeyValuePair<string, T>>.GetEnumerator()
        => this.Select<T, KeyValuePair<string, T>>(item => new(GetKeyForItem(item), item)).GetEnumerator();
}
