//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Text;
using DiscUtils.Streams;
using DiscUtils.Internal;
using System.Linq;
using System.Buffers;
using System.IO;

namespace DiscUtils.Registry;

[Flags]
public enum RegistryValueOptions
{
    None = 0,
    DoNotExpandEnvironmentNames = 1
}

/// <summary>
/// A key within a registry hive.
/// </summary>
public sealed class RegistryKey
{
    private readonly KeyNodeCell _cell;
    private readonly RegistryHive _hive;

    internal RegistryKey(RegistryHive hive, KeyNodeCell cell)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(hive);
        ArgumentNullException.ThrowIfNull(cell);
        _hive = hive;
        _cell = cell;
#else
        _hive = hive ?? throw new ArgumentNullException(nameof(hive));
        _cell = cell ?? throw new ArgumentNullException(nameof(cell));
#endif
    }

    /// <summary>
    /// Gets the class name of this registry key.
    /// </summary>
    /// <remarks>Class name is rarely used.</remarks>
    public string ClassName
    {
        get
        {
            if (_cell.ClassNameIndex > 0)
            {
                Span<byte> buffer = stackalloc byte[_cell.ClassNameLength];
                return EndianUtilities.LittleEndianUnicodeBytesToString(_hive.RawCellData(_cell.ClassNameIndex, buffer));
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the flags of this registry key.
    /// </summary>
    public RegistryKeyFlags Flags => _cell.Flags;

    /// <summary>
    /// Gets the full path of this key within the hive.
    /// </summary>
    public string Name
    {
        get
        {
            var parent = Parent;
            if (parent != null && ((parent.Flags & RegistryKeyFlags.Root) == 0))
            {
                return $@"{parent.Name}\{_cell.Name}";
            }

            return _cell.Name;
        }
    }

    /// <summary>
    /// Gets the name of this key.
    /// </summary>
    public string KeyName => _cell.Name;

    /// <summary>
    /// Gets the full path of this key within the hive.
    /// </summary>
    public override string ToString() => Name;

    /// <summary>
    /// Gets the parent key, or <c>null</c> if this is the root key.
    /// </summary>
    public RegistryKey Parent
    {
        get
        {
            if ((_cell.Flags & RegistryKeyFlags.Root) == 0)
            {
                return new RegistryKey(_hive, _hive.GetCell<KeyNodeCell>(_cell.ParentIndex));
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the number of child keys.
    /// </summary>
    public int SubKeyCount => _cell.NumSubKeys;

    /// <summary>
    /// Gets an enumerator over all sub child keys.
    /// </summary>
    public IEnumerable<RegistryKey> SubKeys
    {
        get
        {
            if (_cell.NumSubKeys != 0)
            {
                var list = _hive.GetCell<ListCell>(_cell.SubKeysIndex);
                
                if (list is null)
                {
                    yield break;
                }

                foreach (var key in list.EnumerateKeys())
                {
                    if (key is not null)
                    {
                        yield return new RegistryKey(_hive, key);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Gets the time the key was last modified.
    /// </summary>
    public DateTime Timestamp => _cell.Timestamp;

    /// <summary>
    /// Gets the number of values in this key.
    /// </summary>
    public int ValueCount => _cell.NumValues;

    /// <summary>
    /// Gets an enumerator over all values in this key.
    /// </summary>
    private IEnumerable<RegistryValue> Values
    {
        get
        {
            if (_cell.NumValues != 0)
            {
                var valueListMem = ArrayPool<byte>.Shared.Rent(_cell.NumValues * 4);
                try
                {
                    var valueList = valueListMem.AsMemory(0, _hive.RawCellData(_cell.ValueListIndex, valueListMem.AsSpan(0, _cell.NumValues * 4)).Length);

                    for (var i = 0; i < _cell.NumValues; ++i)
                    {
                        var valueIndex = EndianUtilities.ToInt32LittleEndian(valueList.Span.Slice(i * 4));
                        yield return new RegistryValue(_hive, _hive.GetCell<ValueCell>(valueIndex));
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(valueListMem);
                }
            }
        }
    }

    /// <summary>
    /// Gets the Security Descriptor applied to the registry key.
    /// </summary>
    /// <returns>The security descriptor as a RegistrySecurity instance.</returns>
    public RegistrySecurity GetAccessControl()
    {
        if (_cell.SecurityIndex > 0)
        {
            var secCell = _hive.GetCell<SecurityCell>(_cell.SecurityIndex);
            return secCell.SecurityDescriptor;
        }

        return null;
    }

    /// <summary>
    /// Gets the names of all child sub keys.
    /// </summary>
    /// <returns>The names of the sub keys.</returns>
    public IEnumerable<string> GetSubKeyNames()
    {
        if (_cell.NumSubKeys != 0)
        {
            return _hive.GetCell<ListCell>(_cell.SubKeysIndex).EnumerateKeyNames();
        }

        return Enumerable.Empty<string>();
    }

    /// <summary>
    /// Gets a named value stored within this key.
    /// </summary>
    /// <param name="name">The name of the value to retrieve.</param>
    /// <returns>The value as a .NET object.</returns>
    /// <remarks>The mapping from registry type of .NET type is as follows:
    /// <list type="table">
    ///   <listheader>
    ///     <term>Value Type</term>
    ///     <term>.NET type</term>
    ///   </listheader>
    ///   <item>
    ///     <description>String</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>ExpandString</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>Link</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>DWord</description>
    ///     <description>uint</description>
    ///   </item>
    ///   <item>
    ///     <description>DWordBigEndian</description>
    ///     <description>uint</description>
    ///   </item>
    ///   <item>
    ///     <description>MultiString</description>
    ///     <description>string[]</description>
    ///   </item>
    ///   <item>
    ///     <description>QWord</description>
    ///     <description>ulong</description>
    ///   </item>
    /// </list>
    /// </remarks>
    public object GetValue(string name)
    {
        return GetValue(name, null, RegistryValueOptions.None);
    }

    /// <summary>
    /// Gets a named value stored within this key.
    /// </summary>
    /// <param name="name">The name of the value to retrieve.</param>
    /// <param name="defaultValue">The default value to return, if no existing value is stored.</param>
    /// <returns>The value as a .NET object.</returns>
    /// <remarks>The mapping from registry type of .NET type is as follows:
    /// <list type="table">
    ///   <listheader>
    ///     <term>Value Type</term>
    ///     <term>.NET type</term>
    ///   </listheader>
    ///   <item>
    ///     <description>String</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>ExpandString</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>Link</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>DWord</description>
    ///     <description>uint</description>
    ///   </item>
    ///   <item>
    ///     <description>DWordBigEndian</description>
    ///     <description>uint</description>
    ///   </item>
    ///   <item>
    ///     <description>MultiString</description>
    ///     <description>string[]</description>
    ///   </item>
    ///   <item>
    ///     <description>QWord</description>
    ///     <description>ulong</description>
    ///   </item>
    /// </list>
    /// </remarks>
    public object GetValue(string name, object defaultValue)
    {
        return GetValue(name, defaultValue, RegistryValueOptions.None);
    }

    /// <summary>
    /// Gets a named value stored within this key.
    /// </summary>
    /// <param name="name">The name of the value to retrieve.</param>
    /// <param name="defaultValue">The default value to return, if no existing value is stored.</param>
    /// <param name="options">Flags controlling how the value is processed before it's returned.</param>
    /// <returns>The value as a .NET object.</returns>
    /// <remarks>The mapping from registry type of .NET type is as follows:
    /// <list type="table">
    ///   <listheader>
    ///     <term>Value Type</term>
    ///     <term>.NET type</term>
    ///   </listheader>
    ///   <item>
    ///     <description>String</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>ExpandString</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>Link</description>
    ///     <description>string</description>
    ///   </item>
    ///   <item>
    ///     <description>DWord</description>
    ///     <description>uint</description>
    ///   </item>
    ///   <item>
    ///     <description>DWordBigEndian</description>
    ///     <description>uint</description>
    ///   </item>
    ///   <item>
    ///     <description>MultiString</description>
    ///     <description>string[]</description>
    ///   </item>
    ///   <item>
    ///     <description>QWord</description>
    ///     <description>ulong</description>
    ///   </item>
    /// </list>
    /// </remarks>
    public object GetValue(string name, object defaultValue, RegistryValueOptions options)
    {
        var regVal = GetRegistryValue(name);
        if (regVal != null)
        {
            if (regVal.DataType == RegistryValueType.ExpandString &&
                (options & RegistryValueOptions.DoNotExpandEnvironmentNames) == 0)
            {
                return Environment.ExpandEnvironmentVariables((string)regVal.Value);
            }

            return regVal.Value;
        }

        return defaultValue;
    }

    /// <summary>
    /// Sets a named value stored within this key.
    /// </summary>
    /// <param name="name">The name of the value to store.</param>
    /// <param name="value">The value to store.</param>
    public void SetValue(string name, object value)
    {
        RegistryValueType valueType;

        switch (value)
        {
            case int:
                valueType = RegistryValueType.Dword;
                break;
            
            case long:
                valueType = RegistryValueType.Qword;
                break;
            
            case byte[] or ReadOnlyMemory<byte> or Memory<byte>:
                valueType = RegistryValueType.Binary;
                break;
            
            case string[]:
                valueType = RegistryValueType.MultiString;
                break;
            
            default:
                valueType = RegistryValueType.String;
                break;
        }

        SetValue(name, value, valueType);
    }

    /// <summary>
    /// Sets a named value stored within this key.
    /// </summary>
    /// <param name="name">The name of the value to store.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="valueType">The registry type of the data.</param>
    public void SetValue(string name, object value, RegistryValueType valueType)
    {
        var valObj = GetRegistryValue(name) ?? AddRegistryValue(name);
        valObj.SetValue(value, valueType);
    }

    /// <summary>
    /// Deletes a named value stored within this key.
    /// </summary>
    /// <param name="name">The name of the value to delete.</param>
    public void DeleteValue(string name)
    {
        DeleteValue(name, true);
    }

    /// <summary>
    /// Deletes a named value stored within this key.
    /// </summary>
    /// <param name="name">The name of the value to delete.</param>
    /// <param name="throwOnMissingValue">Throws ArgumentException if <c>name</c> doesn't exist.</param>
    /// <returns>True if a value with specified name was found and deleted, false otherwise.</returns>
    public bool DeleteValue(string name, bool throwOnMissingValue)
    {
        var foundValue = false;

        if (_cell.NumValues != 0)
        {
            var valueListMem = ArrayPool<byte>.Shared.Rent(_cell.NumValues * 4);
            try
            {
                var valueList = _hive.RawCellData(_cell.ValueListIndex, valueListMem.AsSpan(0, _cell.NumValues * 4));

                var i = 0;
                while (i < _cell.NumValues)
                {
                    var valueIndex = EndianUtilities.ToInt32LittleEndian(valueList.Slice(i * 4));
                    var valueCell = _hive.GetCell<ValueCell>(valueIndex);
                    if (string.Equals(valueCell.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        foundValue = true;
                        _hive.FreeCell(valueIndex);
                        _cell.NumValues--;
                        _hive.UpdateCell(_cell, false);
                        break;
                    }

                    ++i;
                }

                // Move following value's to fill gap
                if (i < _cell.NumValues)
                {
                    valueList.Slice((i + 1) * 4, (_cell.NumValues - i) * 4).CopyTo(valueList.Slice(i * 4));

                    _hive.WriteRawCellData(_cell.ValueListIndex, valueList.Slice(0, _cell.NumValues * 4));
                }

                // TODO: Update maxbytes for value name and value content if this was the largest value for either.
                // Windows seems to repair this info, if not accurate, though.

            }
            finally
            {
                ArrayPool<byte>.Shared.Return(valueListMem);
            }
        }

        if (throwOnMissingValue && !foundValue)
        {
            throw new ArgumentException($"No such value: {name}", nameof(name));
        }

        return foundValue;
    }

    /// <summary>
    /// Gets the type of a named value.
    /// </summary>
    /// <param name="name">The name of the value to inspect.</param>
    /// <returns>The value's type.</returns>
    public RegistryValueType GetValueType(string name)
    {
        var regVal = GetRegistryValue(name);
        if (regVal != null)
        {
            return regVal.DataType;
        }

        return RegistryValueType.None;
    }

    /// <summary>
    /// Gets the names of all values in this key.
    /// </summary>
    /// <returns>An array of strings containing the value names.</returns>
    public IEnumerable<string> GetValueNames()
    {
        foreach (var value in Values)
        {
            yield return value.Name;
        }
    }

    /// <summary>
    /// Creates or opens a subkey.
    /// </summary>
    /// <param name="subkey">The relative path the the subkey.</param>
    /// <returns>The subkey.</returns>
    public RegistryKey CreateSubKey(string subkey)
    {
        if (string.IsNullOrEmpty(subkey))
        {
            return this;
        }

        var split = subkey.Split(Utilities.PathSeparators, 2);
        var cellIndex = FindSubKeyCell(split[0]);

        if (cellIndex < 0)
        {
            var newKeyCell = new KeyNodeCell(split[0], _cell.Index)
            {
                SecurityIndex = _cell.SecurityIndex
            };
            ReferenceSecurityCell(newKeyCell.SecurityIndex);
            _hive.UpdateCell(newKeyCell, true);

            LinkSubKey(split[0], newKeyCell.Index);

            if (split.Length == 1)
            {
                return new RegistryKey(_hive, newKeyCell);
            }

            return new RegistryKey(_hive, newKeyCell).CreateSubKey(split[1]);
        }

        var cell = _hive.GetCell<KeyNodeCell>(cellIndex);
        if (split.Length == 1)
        {
            return new RegistryKey(_hive, cell);
        }

        return new RegistryKey(_hive, cell).CreateSubKey(split[1]);
    }

    /// <summary>
    /// Opens a sub key.
    /// </summary>
    /// <param name="path">The relative path to the sub key.</param>
    /// <returns>The sub key, or <c>null</c> if not found.</returns>
    public RegistryKey OpenSubKey(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return this;
        }

        var split = path.Split(Utilities.PathSeparators, 2);
        var cellIndex = FindSubKeyCell(split[0]);

        if (cellIndex < 0)
        {
            return null;
        }

        var cell = _hive.GetCell<KeyNodeCell>(cellIndex);
        if (split.Length == 1)
        {
            return new RegistryKey(_hive, cell);
        }

        return new RegistryKey(_hive, cell).OpenSubKey(split[1]);
    }

    /// <summary>
    /// Deletes a subkey and any child subkeys recursively. The string subkey is not case-sensitive.
    /// </summary>
    /// <param name="subkey">The subkey to delete.</param>
    /// <returns>True if key was found and deleted, false otherwise.</returns>
    public bool DeleteSubKeyTree(string subkey)
    {
        var subKeyObj = OpenSubKey(subkey);

        if (subKeyObj == null)
        {
            return false;
        }

        if ((subKeyObj.Flags & RegistryKeyFlags.Root) != 0)
        {
            throw new ArgumentException("Attempt to delete root key");
        }

        foreach (var child in subKeyObj.GetSubKeyNames())
        {
            subKeyObj.DeleteSubKeyTree(child);
        }

        DeleteSubKey(subkey);

        return true;
    }

    /// <summary>
    /// Deletes the specified subkey. The string subkey is not case-sensitive.
    /// </summary>
    /// <param name="subkey">The subkey to delete.</param>
    public void DeleteSubKey(string subkey)
    {
        DeleteSubKey(subkey, true);
    }

    /// <summary>
    /// Deletes the specified subkey. The string subkey is not case-sensitive.
    /// </summary>
    /// <param name="subkey">The subkey to delete.</param>
    /// <param name="throwOnMissingSubKey"><c>true</c> to throw an argument exception if <c>subkey</c> doesn't exist.</param>
    /// <returns>True if a subkey was found and deleted, false otherwise.</returns>
    /// <exception cref="InvalidOperationException">The key has subkeys</exception>
    public bool DeleteSubKey(string subkey, bool throwOnMissingSubKey)
    {
        if (string.IsNullOrEmpty(subkey))
        {
            throw new ArgumentException("Invalid SubKey", nameof(subkey));
        }

        var split = subkey.Split(Utilities.PathSeparators, 2);

        var subkeyCellIndex = FindSubKeyCell(split[0]);
        if (subkeyCellIndex < 0)
        {
            if (throwOnMissingSubKey)
            {
                throw new ArgumentException("No such SubKey", nameof(subkey));
            }

            return false;
        }

        var subkeyCell = _hive.GetCell<KeyNodeCell>(subkeyCellIndex);

        if (split.Length == 1)
        {
            if (subkeyCell.NumSubKeys != 0)
            {
                throw new InvalidOperationException("The registry key has subkeys");
            }

            if (subkeyCell.ClassNameIndex != -1)
            {
                _hive.FreeCell(subkeyCell.ClassNameIndex);
                subkeyCell.ClassNameIndex = -1;
                subkeyCell.ClassNameLength = 0;
            }

            if (subkeyCell.SecurityIndex != -1)
            {
                DereferenceSecurityCell(subkeyCell.SecurityIndex);
                subkeyCell.SecurityIndex = -1;
            }

            if (subkeyCell.SubKeysIndex != -1)
            {
                FreeSubKeys(subkeyCell);
            }

            if (subkeyCell.ValueListIndex != -1)
            {
                FreeValues(subkeyCell);
            }

            UnlinkSubKey(subkey);
            _hive.FreeCell(subkeyCellIndex);
            _hive.UpdateCell(_cell, false);

            return true;
        }
        else
        {
            return new RegistryKey(_hive, subkeyCell)
                .DeleteSubKey(split[1], throwOnMissingSubKey);
        }
    }

    public RegistryValue GetRegistryValue(string name)
    {
        if (name != null && name.Length == 0)
        {
            name = null;
        }

        if (_cell.NumValues != 0)
        {
            var valueListMem = ArrayPool<byte>.Shared.Rent(_cell.NumValues * 4);
            try
            {
                var valueList = _hive.RawCellData(_cell.ValueListIndex, valueListMem.AsSpan(0, _cell.NumValues * 4));

                for (var i = 0; i < _cell.NumValues; ++i)
                {
                    var valueIndex = EndianUtilities.ToInt32LittleEndian(valueList.Slice(i * 4));
                    var cell = _hive.GetCell<ValueCell>(valueIndex);
                    if (cell is not null &&
                        string.Equals(cell.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return new RegistryValue(_hive, cell);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(valueListMem);
            }
        }

        return null;
    }

    public RegistryValue AddRegistryValue(string name)
    {
        var valueListMem = ArrayPool<byte>.Shared.Rent(_cell.NumValues * 4);
        try
        {
            var valueList = _hive.RawCellData(_cell.ValueListIndex, valueListMem.AsSpan(0, _cell.NumValues * 4));
            if (valueList == null)
            {
                valueList = Array.Empty<byte>();
            }

            var insertIdx = 0;
            while (insertIdx < _cell.NumValues)
            {
                var valueCellIndex = EndianUtilities.ToInt32LittleEndian(valueList.Slice(insertIdx * 4));
                var cell = _hive.GetCell<ValueCell>(valueCellIndex);
                if (string.Compare(name, cell.Name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    break;
                }

                ++insertIdx;
            }

            // Allocate a new value cell (note _hive.UpdateCell does actual allocation).
            var valueCell = new ValueCell(name);
            _hive.UpdateCell(valueCell, true);

            // Update the value list, re-allocating if necessary
            var newValueListMem = ArrayPool<byte>.Shared.Rent(_cell.NumValues * 4 + 4);
            var newValueList = newValueListMem.AsSpan(0, _cell.NumValues * 4 + 4);
            try
            {
                valueList.Slice(0, insertIdx * 4).CopyTo(newValueList);
                EndianUtilities.WriteBytesLittleEndian(valueCell.Index, newValueList.Slice(insertIdx * 4));
                valueList.Slice(insertIdx * 4, (_cell.NumValues - insertIdx) * 4).CopyTo(newValueList.Slice(insertIdx * 4 + 4));
                if (_cell.ValueListIndex == -1 ||
                    !_hive.WriteRawCellData(_cell.ValueListIndex, newValueList))
                {
                    var newListCellIndex = _hive.AllocateRawCell(MathUtilities.RoundUp(newValueList.Length, 8));
                    _hive.WriteRawCellData(newListCellIndex, newValueList);

                    if (_cell.ValueListIndex != -1)
                    {
                        _hive.FreeCell(_cell.ValueListIndex);
                    }

                    _cell.ValueListIndex = newListCellIndex;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(newValueListMem);
            }

            // Record the new value and save this cell
            _cell.NumValues++;
            _hive.UpdateCell(_cell, false);

            // Finally, set the data in the value cell
            return new RegistryValue(_hive, valueCell);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(valueListMem);
        }
    }

    private int FindSubKeyCell(string name)
    {
        if (_cell.NumSubKeys != 0)
        {
            var listCell = _hive.GetCell<ListCell>(_cell.SubKeysIndex)
                ?? throw new RegistryCorruptException($"Missing cell for subkeys for key '{Name}'");

            if (listCell.FindKey(name, out var cellIndex) == 0)
            {
                return cellIndex;
            }
        }

        return -1;
    }

    private void LinkSubKey(string name, int cellIndex)
    {
        if (_cell.SubKeysIndex == -1)
        {
            var newListCell = new SubKeyHashedListCell(_hive, "lf");
            newListCell.Add(name, cellIndex);
            _hive.UpdateCell(newListCell, true);
            _cell.NumSubKeys = 1;
            _cell.SubKeysIndex = newListCell.Index;
        }
        else
        {
            var list = _hive.GetCell<ListCell>(_cell.SubKeysIndex);
            _cell.SubKeysIndex = list.LinkSubKey(name, cellIndex);
            _cell.NumSubKeys++;
        }

        _hive.UpdateCell(_cell, false);
    }

    private void UnlinkSubKey(string name)
    {
        if (_cell.SubKeysIndex == -1 || _cell.NumSubKeys == 0)
        {
            throw new InvalidOperationException("No subkey list");
        }

        var list = _hive.GetCell<ListCell>(_cell.SubKeysIndex);
        _cell.SubKeysIndex = list.UnlinkSubKey(name);
        _cell.NumSubKeys--;
    }

    private void ReferenceSecurityCell(int cellIndex)
    {
        var sc = _hive.GetCell<SecurityCell>(cellIndex);
        sc.UsageCount++;
        _hive.UpdateCell(sc, false);
    }

    private void DereferenceSecurityCell(int cellIndex)
    {
        var sc = _hive.GetCell<SecurityCell>(cellIndex);
        sc.UsageCount--;
        if (sc.UsageCount == 0)
        {
            var prev = _hive.GetCell<SecurityCell>(sc.PreviousIndex);
            prev.NextIndex = sc.NextIndex;
            _hive.UpdateCell(prev, false);

            var next = _hive.GetCell<SecurityCell>(sc.NextIndex);
            next.PreviousIndex = sc.PreviousIndex;
            _hive.UpdateCell(next, false);

            _hive.FreeCell(cellIndex);
        }
        else
        {
            _hive.UpdateCell(sc, false);
        }
    }

    private void FreeValues(KeyNodeCell cell)
    {
        if (cell.NumValues != 0 && cell.ValueListIndex != -1)
        {
            var valueListMem = ArrayPool<byte>.Shared.Rent(cell.NumValues * 4);
            try
            {
                var valueList = _hive.RawCellData(cell.ValueListIndex, valueListMem.AsSpan(0, cell.NumValues * 4));

                for (var i = 0; i < cell.NumValues; ++i)
                {
                    var valueIndex = EndianUtilities.ToInt32LittleEndian(valueList.Slice(i * 4));
                    _hive.FreeCell(valueIndex);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(valueListMem);
            }

            _hive.FreeCell(cell.ValueListIndex);
            cell.ValueListIndex = -1;
            cell.NumValues = 0;
            cell.MaxValDataBytes = 0;
            cell.MaxValNameBytes = 0;
        }
    }

    private void FreeSubKeys(KeyNodeCell subkeyCell)
    {
        if (subkeyCell.SubKeysIndex == -1)
        {
            throw new InvalidOperationException("No subkey list");
        }

        var list = _hive.GetCell<Cell>(subkeyCell.SubKeysIndex);

        if (list is SubKeyIndirectListCell indirectList)
        {
            ////foreach (int listIndex in indirectList.CellIndexes)
            for (var i = 0; i < indirectList.CellIndexes.Count; ++i)
            {
                var listIndex = indirectList.CellIndexes[i];
                _hive.FreeCell(listIndex);
            }
        }

        _hive.FreeCell(list.Index);
    }
}