// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System.Collections;
using System.Collections.Generic;

namespace DiscUtils.ExFat.Internal;
/// <summary>
/// Cache dictionary
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TValue">The type of the value.</typeparam>
/// <seealso cref="IDictionary{TKey, TValue}" />
public class Cache<TKey, TValue> : IDictionary<TKey, TValue>
{
    private readonly int capacity;
    private readonly IDictionary<TKey, TValue> dictionary = new Dictionary<TKey, TValue>();
    private readonly IList<TKey> orderedKeys = new List<TKey>();

    /// <inheritdoc />
    /// <summary>
    /// Gets the number of elements contained in the <see cref="T:System.Collections.Generic.ICollection`1" />.
    /// </summary>
    public int Count => dictionary.Count;
    /// <inheritdoc />
    /// <summary>
    /// Gets a value indicating whether the <see cref="T:System.Collections.Generic.ICollection`1" /> is read-only.
    /// </summary>
    public bool IsReadOnly => dictionary.IsReadOnly;

    /// <inheritdoc />
    /// <summary>
    /// Gets or sets the <see cref="!:TValue" /> with the specified key.
    /// </summary>
    /// <value>
    /// The <see cref="!:TValue" />.
    /// </value>
    /// <param name="key">The key.</param>
    /// <returns></returns>
    public TValue this[TKey key]
    {
        get
        {
            var value = dictionary[key];
            Touch(key);
            return value;
        }
        set
        {
            dictionary[key] = value;
            Touch(key);
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Gets an <see cref="T:System.Collections.Generic.ICollection`1" /> containing the keys of the <see cref="T:System.Collections.Generic.IDictionary`2" />.
    /// </summary>
    public ICollection<TKey> Keys => dictionary.Keys;
    /// <inheritdoc />
    /// <summary>
    /// Gets an <see cref="T:System.Collections.Generic.ICollection`1" /> containing the values in the <see cref="T:System.Collections.Generic.IDictionary`2" />.
    /// </summary>
    public ICollection<TValue> Values => dictionary.Values;

    /// <summary>
    /// Initializes a new instance of the <see cref="Cache{TKey, TValue}"/> class.
    /// </summary>
    /// <param name="capacity">The capacity.</param>
    public Cache(int capacity)
    {
        this.capacity = capacity;
    }

    /// <summary>
    /// Touches the specified key.
    /// </summary>
    /// <param name="key">The key.</param>
    private void Touch(TKey key)
    {
        // remove from anywhere
        orderedKeys.Remove(key);
        // place at end
        orderedKeys.Add(key);
        // on capacity overflow
        while (orderedKeys.Count >= capacity)
        {
            // oldest key is first
            var lastKey = orderedKeys[0];
            orderedKeys.RemoveAt(0);
            dictionary.Remove(lastKey);
        }
    }

    /// <inheritdoc />
    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    /// <returns>
    /// A <see cref="T:System.Collections.Generic.IEnumerator`1" /> that can be used to iterate through the collection.
    /// </returns>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => dictionary.GetEnumerator();

    /// <inheritdoc />
    /// <summary>
    /// Returns an enumerator that iterates through a collection.
    /// </summary>
    /// <returns>
    /// An <see cref="T:System.Collections.IEnumerator" /> object that can be used to iterate through the collection.
    /// </returns>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    /// <summary>
    /// Adds an item to the <see cref="T:System.Collections.Generic.ICollection`1" />.
    /// </summary>
    /// <param name="item">The object to add to the <see cref="T:System.Collections.Generic.ICollection`1" />.</param>
    public void Add(KeyValuePair<TKey, TValue> item)
    {
        dictionary.Add(item);
        Touch(item.Key);
    }

    /// <inheritdoc />
    /// <summary>
    /// Removes all items from the <see cref="T:System.Collections.Generic.ICollection`1" />.
    /// </summary>
    public void Clear()
    {
        dictionary.Clear();
        orderedKeys.Clear();
    }

    /// <inheritdoc />
    /// <summary>
    /// Determines whether the <see cref="T:System.Collections.Generic.ICollection`1" /> contains a specific value.
    /// </summary>
    /// <param name="item">The object to locate in the <see cref="T:System.Collections.Generic.ICollection`1" />.</param>
    /// <returns>
    /// true if <paramref name="item" /> is found in the <see cref="T:System.Collections.Generic.ICollection`1" />; otherwise, false.
    /// </returns>
    public bool Contains(KeyValuePair<TKey, TValue> item) => dictionary.Contains(item);

    /// <inheritdoc />
    /// <summary>
    /// Copies the elements of the <see cref="T:System.Collections.Generic.ICollection`1" /> to an <see cref="T:System.Array" />, starting at a particular <see cref="T:System.Array" /> index.
    /// </summary>
    /// <param name="array">The one-dimensional <see cref="T:System.Array" /> that is the destination of the elements copied from <see cref="T:System.Collections.Generic.ICollection`1" />. The <see cref="T:System.Array" /> must have zero-based indexing.</param>
    /// <param name="arrayIndex">The zero-based index in <paramref name="array" /> at which copying begins.</param>
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => dictionary.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    /// <summary>
    /// Removes the first occurrence of a specific object from the <see cref="T:System.Collections.Generic.ICollection`1" />.
    /// </summary>
    /// <param name="item">The object to remove from the <see cref="T:System.Collections.Generic.ICollection`1" />.</param>
    /// <returns>
    /// true if <paramref name="item" /> was successfully removed from the <see cref="T:System.Collections.Generic.ICollection`1" />; otherwise, false. This method also returns false if <paramref name="item" /> is not found in the original <see cref="T:System.Collections.Generic.ICollection`1" />.
    /// </returns>
    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        dictionary.Remove(item);
        return orderedKeys.Remove(item.Key);
    }

    /// <inheritdoc />
    /// <summary>
    /// Determines whether the <see cref="T:System.Collections.Generic.IDictionary`2" /> contains an element with the specified key.
    /// </summary>
    /// <param name="key">The key to locate in the <see cref="T:System.Collections.Generic.IDictionary`2" />.</param>
    /// <returns>
    /// true if the <see cref="T:System.Collections.Generic.IDictionary`2" /> contains an element with the key; otherwise, false.
    /// </returns>
    public bool ContainsKey(TKey key) => dictionary.ContainsKey(key);

    /// <inheritdoc />
    /// <summary>
    /// Adds an element with the provided key and value to the <see cref="T:System.Collections.Generic.IDictionary`2" />.
    /// </summary>
    /// <param name="key">The object to use as the key of the element to add.</param>
    /// <param name="value">The object to use as the value of the element to add.</param>
    public void Add(TKey key, TValue value)
    {
        dictionary.Add(key, value);
        Touch(key);
    }

    /// <inheritdoc />
    /// <summary>
    /// Removes the element with the specified key from the <see cref="T:System.Collections.Generic.IDictionary`2" />.
    /// </summary>
    /// <param name="key">The key of the element to remove.</param>
    /// <returns>
    /// true if the element is successfully removed; otherwise, false.  This method also returns false if <paramref name="key" /> was not found in the original <see cref="T:System.Collections.Generic.IDictionary`2" />.
    /// </returns>
    public bool Remove(TKey key)
    {
        dictionary.Remove(key);
        return orderedKeys.Remove(key);
    }

    /// <inheritdoc />
    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key whose value to get.</param>
    /// <param name="value">When this method returns, the value associated with the specified key, if the key is found; otherwise, the default value for the type of the <paramref name="value" /> parameter. This parameter is passed uninitialized.</param>
    /// <returns>
    /// true if the object that implements <see cref="T:System.Collections.Generic.IDictionary`2" /> contains an element with the specified key; otherwise, false.
    /// </returns>
    public bool TryGetValue(TKey key, out TValue value)
    {
        if (!dictionary.TryGetValue(key, out value))
        {
            return false;
        }

        Touch(key);
        return true;
    }
}