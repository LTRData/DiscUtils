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
using DiscUtils.Streams;

namespace DiscUtils.Registry;

internal sealed class SubKeyHashedListCell : ListCell
{
    private string _hashType;
    private readonly RegistryHive _hive;
    private List<uint> _nameHashes;
    private short _numElements;
    private List<int> _subKeyIndexes;

    public SubKeyHashedListCell(RegistryHive hive, string hashType)
        : base(-1)
    {
        _hive = hive;
        _hashType = hashType;
        _subKeyIndexes = new List<int>();
        _nameHashes = new List<uint>();
    }

    public SubKeyHashedListCell(RegistryHive hive, int index)
        : base(index)
    {
        _hive = hive;
    }

    internal override int Count
    {
        get { return _subKeyIndexes.Count; }
    }

    public override int Size
    {
        get { return 0x4 + _numElements * 0x8; }
    }

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        _hashType = EndianUtilities.BytesToString(buffer.Slice(0, 2));
        _numElements = EndianUtilities.ToInt16LittleEndian(buffer.Slice(2));

        _subKeyIndexes = new List<int>(_numElements);
        _nameHashes = new List<uint>(_numElements);
        for (var i = 0; i < _numElements; ++i)
        {
            _subKeyIndexes.Add(EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x4 + i * 0x8)));
            _nameHashes.Add(EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x4 + i * 0x8 + 0x4)));
        }

        return 0x4 + _numElements * 0x8;
    }

    public override void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.StringToBytes(_hashType, buffer.Slice(0, 2));
        EndianUtilities.WriteBytesLittleEndian(_numElements, buffer.Slice(0x2));
        for (var i = 0; i < _numElements; ++i)
        {
            EndianUtilities.WriteBytesLittleEndian(_subKeyIndexes[i], buffer.Slice(0x4 + i * 0x8));
            EndianUtilities.WriteBytesLittleEndian(_nameHashes[i], buffer.Slice(0x4 + i * 0x8 + 0x4));
        }
    }

    /// <summary>
    /// Adds a new entry.
    /// </summary>
    /// <param name="name">The name of the subkey.</param>
    /// <param name="cellIndex">The cell index of the subkey.</param>
    /// <returns>The index of the new entry.</returns>
    internal int Add(string name, int cellIndex)
    {
        for (var i = 0; i < _numElements; ++i)
        {
            var cell = _hive.GetCell<KeyNodeCell>(_subKeyIndexes[i]);
            if (cell is not null &&
                string.Compare(cell.Name, name, StringComparison.OrdinalIgnoreCase) > 0)
            {
                _subKeyIndexes.Insert(i, cellIndex);
                _nameHashes.Insert(i, CalcHash(name));
                _numElements++;
                return i;
            }
        }

        _subKeyIndexes.Add(cellIndex);
        _nameHashes.Add(CalcHash(name));
        return _numElements++;
    }

    internal override int FindKey(string name, out int cellIndex)
    {
        // Check first and last, to early abort if the name is outside the range of this list
        var result = FindKeyAt(name, 0, out cellIndex);
        if (result <= 0)
        {
            return result;
        }

        result = FindKeyAt(name, _subKeyIndexes.Count - 1, out cellIndex);
        if (result >= 0)
        {
            return result;
        }

        var finder = new KeyFinder(_hive, name);
        var idx = _subKeyIndexes.BinarySearch(-1, finder);
        cellIndex = finder.CellIndex;
        return idx < 0 ? -1 : 0;
    }

    internal override IEnumerable<string> EnumerateKeyNames()
    {
        for (var i = 0; i < _subKeyIndexes.Count; ++i)
        {
            var cell = _hive.GetCell<KeyNodeCell>(_subKeyIndexes[i]);
            if (cell is not null)
            {
                yield return cell.Name;
            }
        }
    }

    internal override IEnumerable<KeyNodeCell> EnumerateKeys()
    {
        for (var i = 0; i < _subKeyIndexes.Count; ++i)
        {
            yield return _hive.GetCell<KeyNodeCell>(_subKeyIndexes[i]);
        }
    }

    internal override int LinkSubKey(string name, int cellIndex)
    {
        Add(name, cellIndex);
        return _hive.UpdateCell(this, true);
    }

    internal override int UnlinkSubKey(string name)
    {
        var index = IndexOf(name);
        if (index >= 0)
        {
            RemoveAt(index);
            return _hive.UpdateCell(this, true);
        }

        return Index;
    }

    /// <summary>
    /// Finds a subkey cell, returning it's index in this list.
    /// </summary>
    /// <param name="name">The name of the key to find.</param>
    /// <returns>The index of the found key, or <c>-1</c>.</returns>
    internal int IndexOf(string name)
    {
        foreach (var index in Find(name, 0))
        {
            var cell = _hive.GetCell<KeyNodeCell>(_subKeyIndexes[index]);

            if (cell.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    internal void RemoveAt(int index)
    {
        _nameHashes.RemoveAt(index);
        _subKeyIndexes.RemoveAt(index);
        _numElements--;
    }

    private uint CalcHash(string name)
    {
        uint hash = 0;
        if (_hashType == "lh")
        {
            for (var i = 0; i < name.Length; ++i)
            {
                hash *= 37;
                hash += char.ToUpperInvariant(name[i]);
            }
        }
        else
        {
            var hashStr = $"{name}\0\0\0\0";
            for (var i = 0; i < 4; ++i)
            {
                hash |= (uint)((hashStr[i] & 0xFF) << (i * 8));
            }
        }

        return hash;
    }

    private int FindKeyAt(string name, int listIndex, out int cellIndex)
    {
        var cell = _hive.GetCell<Cell>(_subKeyIndexes[listIndex]);
        if (cell == null)
        {
            cellIndex = 0;
            return -1;
        }

        if (cell is ListCell listCell)
        {
            return listCell.FindKey(name, out cellIndex);
        }

        cellIndex = _subKeyIndexes[listIndex];
        return string.Compare(name, ((KeyNodeCell)cell).Name, StringComparison.OrdinalIgnoreCase);
    }

    private IEnumerable<int> Find(string name, int start)
    {
        if (_hashType == "lh")
        {
            return FindByHash(name, start);
        }
        return FindByPrefix(name, start);
    }

    private IEnumerable<int> FindByHash(string name, int start)
    {
        var hash = CalcHash(name);

        for (var i = start; i < _nameHashes.Count; ++i)
        {
            if (_nameHashes[i] == hash)
            {
                yield return i;
            }
        }
    }

    private IEnumerable<int> FindByPrefix(string name, int start)
    {
        var compChars = Math.Min(name.Length, 4);
        var compStr = $"{name.Substring(0, compChars).ToUpperInvariant()}\0\0\0\0";

        for (var i = start; i < _nameHashes.Count; ++i)
        {
            var match = true;
            var hash = _nameHashes[i];

            for (var j = 0; j < 4; ++j)
            {
                var ch = (char)((hash >> (j * 8)) & 0xFF);
                if (char.ToUpperInvariant(ch) != compStr[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                yield return i;
            }
        }
    }

    private sealed class KeyFinder : IComparer<int>
    {
        private readonly RegistryHive _hive;
        private readonly string _searchName;

        public KeyFinder(RegistryHive hive, string searchName)
        {
            _hive = hive;
            _searchName = searchName;
        }

        public int CellIndex { get; set; }

        #region IComparer<int> Members

        public int Compare(int x, int y)
        {
            // TODO: Be more efficient at ruling out no-hopes by using the hash values
            var cell = _hive.GetCell<KeyNodeCell>(x);
            var result = string.Compare(cell?.Name, _searchName, StringComparison.OrdinalIgnoreCase);
            if (result == 0)
            {
                CellIndex = x;
            }

            return result;
        }

        #endregion
    }
}