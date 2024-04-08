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
using System.IO;
using DiscUtils.Streams;

namespace DiscUtils.Ntfs;

internal class IndexNode
{
    private readonly List<IndexEntry> _entries;

    private readonly Index _index;
    private readonly bool _isRoot;
    private readonly int _storageOverhead;
    private readonly IndexNodeSaveFn _store;

    public IndexNode(IndexNodeSaveFn store, int storeOverhead, Index index, bool isRoot, uint allocatedSize)
    {
        _store = store;
        _storageOverhead = storeOverhead;
        _index = index;
        _isRoot = isRoot;
        Header = new IndexHeader(allocatedSize);
        TotalSpaceAvailable = allocatedSize;

        var endEntry = new IndexEntry(_index.IsFileIndex);
        endEntry.Flags |= IndexEntryFlags.End;

        _entries = new List<IndexEntry>();
        _entries.Add(endEntry);

        Header.OffsetToFirstEntry = (uint)(IndexHeader.Size + storeOverhead);
        Header.TotalSizeOfEntries = (uint)(Header.OffsetToFirstEntry + endEntry.Size);
    }

    public IndexNode(IndexNodeSaveFn store, int storeOverhead, Index index, bool isRoot, ReadOnlySpan<byte> buffer)
    {
        _store = store;
        _storageOverhead = storeOverhead;
        _index = index;
        _isRoot = isRoot;
        Header = new IndexHeader(buffer);
        TotalSpaceAvailable = Header.AllocatedSizeOfEntries;

        _entries = new List<IndexEntry>();
        var pos = (int)Header.OffsetToFirstEntry;
        while (pos < Header.TotalSizeOfEntries)
        {
            var entry = new IndexEntry(index.IsFileIndex);
            entry.Read(buffer.Slice(pos));
            _entries.Add(entry);

            if ((entry.Flags & IndexEntryFlags.End) != 0)
            {
                break;
            }

            pos += entry.Size;
        }
    }

    public IEnumerable<IndexEntry> Entries
    {
        get { return _entries; }
    }

    public IndexHeader Header { get; }

    private long SpaceFree
    {
        get
        {
            long entriesTotal = 0;
            for (var i = 0; i < _entries.Count; ++i)
            {
                entriesTotal += _entries[i].Size;
            }

            var firstEntryOffset = MathUtilities.RoundUp(IndexHeader.Size + _storageOverhead, 8);

            return TotalSpaceAvailable - (entriesTotal + firstEntryOffset);
        }
    }

    internal long TotalSpaceAvailable { get; set; }

    public void AddEntry(byte[] key, byte[] data)
    {
        var overflowEntry = AddEntry(new IndexEntry(key, data, _index.IsFileIndex));
        if (overflowEntry != null)
        {
            throw new IOException("Error adding entry - root overflowed");
        }
    }

    public void UpdateEntry(byte[] key, byte[] data)
    {
        for (var i = 0; i < _entries.Count; ++i)
        {
            var focus = _entries[i];
            var compVal = _index.Compare(key, focus.KeyBuffer);
            if (compVal == 0)
            {
                var newEntry = new IndexEntry(focus, key, data);
                if (_entries[i].Size != newEntry.Size)
                {
                    throw new NotImplementedException("Changing index entry sizes");
                }

                _entries[i] = newEntry;
                _store();
                return;
            }
        }

        throw new IOException("No such index entry");
    }

    public bool TryFindEntry(byte[] key, out IndexEntry entry, out IndexNode node)
    {
        foreach (var focus in _entries)
        {
            if ((focus.Flags & IndexEntryFlags.End) != 0)
            {
                if ((focus.Flags & IndexEntryFlags.Node) != 0)
                {
                    var subNode = _index.GetSubBlock(focus);
                    return subNode.Node.TryFindEntry(key, out entry, out node);
                }

                break;
            }

            var compVal = _index.Compare(key, focus.KeyBuffer);
            if (compVal == 0)
            {
                entry = focus;
                node = this;
                return true;
            }

            if (compVal < 0 && (focus.Flags & (IndexEntryFlags.End | IndexEntryFlags.Node)) != 0)
            {
                var subNode = _index.GetSubBlock(focus);
                return subNode.Node.TryFindEntry(key, out entry, out node);
            }
        }

        entry = null;
        node = null;
        return false;
    }

    public virtual ushort WriteTo(Span<byte> buffer)
    {
        var haveSubNodes = false;
        uint totalEntriesSize = 0;
        foreach (var entry in _entries)
        {
            totalEntriesSize += (uint)entry.Size;
            haveSubNodes |= (entry.Flags & IndexEntryFlags.Node) != 0;
        }

        Header.OffsetToFirstEntry = (uint)MathUtilities.RoundUp(IndexHeader.Size + _storageOverhead, 8);
        Header.TotalSizeOfEntries = totalEntriesSize + Header.OffsetToFirstEntry;
        Header.HasChildNodes = (byte)(haveSubNodes ? 1 : 0);
        Header.WriteTo(buffer);

        var pos = (int)Header.OffsetToFirstEntry;
        foreach (var entry in _entries)
        {
            entry.WriteTo(buffer.Slice(pos));
            pos += entry.Size;
        }

        return IndexHeader.Size;
    }

    public int CalcEntriesSize()
    {
        var totalEntriesSize = 0;
        foreach (var entry in _entries)
        {
            totalEntriesSize += entry.Size;
        }

        return totalEntriesSize;
    }

    public virtual int CalcSize()
    {
        var firstEntryOffset = MathUtilities.RoundUp(IndexHeader.Size + _storageOverhead, 8);
        return firstEntryOffset + CalcEntriesSize();
    }

    public int GetEntry(byte[] key, out bool exactMatch)
    {
        for (var i = 0; i < _entries.Count; ++i)
        {
            var focus = _entries[i];
            int compVal;

            if ((focus.Flags & IndexEntryFlags.End) != 0)
            {
                exactMatch = false;
                return i;
            }

            compVal = _index.Compare(key, focus.KeyBuffer);
            if (compVal <= 0)
            {
                exactMatch = compVal == 0;
                return i;
            }
        }

        throw new IOException("Corrupt index node - no End entry");
    }

    public bool RemoveEntry(byte[] key, out IndexEntry newParentEntry)
    {
        var entryIndex = GetEntry(key, out var exactMatch);
        var entry = _entries[entryIndex];

        if (exactMatch)
        {
            if ((entry.Flags & IndexEntryFlags.Node) != 0)
            {
                var childNode = _index.GetSubBlock(entry).Node;
                var rLeaf = childNode.FindLargestLeaf();

                var newKey = rLeaf.KeyBuffer;
                var newData = rLeaf.DataBuffer;

                childNode.RemoveEntry(newKey, out var newEntry);
                entry.KeyBuffer = newKey;
                entry.DataBuffer = newData;

                if (newEntry != null)
                {
                    InsertEntryThisNode(newEntry);
                }

                newEntry = LiftNode(entryIndex);
                if (newEntry != null)
                {
                    InsertEntryThisNode(newEntry);
                }

                newEntry = PopulateEnd();
                if (newEntry != null)
                {
                    InsertEntryThisNode(newEntry);
                }

                // New entry could be larger than old, so may need
                // to divide this node...
                newParentEntry = EnsureNodeSize();
            }
            else
            {
                _entries.RemoveAt(entryIndex);
                newParentEntry = null;
            }

            _store();
            return true;
        }

        if ((entry.Flags & IndexEntryFlags.Node) != 0)
        {
            var childNode = _index.GetSubBlock(entry).Node;
            if (childNode.RemoveEntry(key, out var newEntry))
            {
                if (newEntry != null)
                {
                    InsertEntryThisNode(newEntry);
                }

                newEntry = LiftNode(entryIndex);
                if (newEntry != null)
                {
                    InsertEntryThisNode(newEntry);
                }

                newEntry = PopulateEnd();
                if (newEntry != null)
                {
                    InsertEntryThisNode(newEntry);
                }

                // New entry could be larger than old, so may need
                // to divide this node...
                newParentEntry = EnsureNodeSize();

                _store();
                return true;
            }
        }

        newParentEntry = null;
        return false;
    }

    /// <summary>
    /// Only valid on the root node, this method moves all entries into a
    /// single child node.
    /// </summary>
    /// <returns>Whether any changes were made.</returns>
    internal bool Depose()
    {
        if (!_isRoot)
        {
            throw new InvalidOperationException("Only valid on root node");
        }

        if (_entries.Count == 1)
        {
            return false;
        }

        var newRootEntry = new IndexEntry(_index.IsFileIndex)
        {
            Flags = IndexEntryFlags.End
        };

        var newBlock = _index.AllocateBlock(newRootEntry);

        // Set the deposed entries into the new node.  Note we updated the parent
        // pointers first, because it's possible SetEntries may need to further
        // divide the entries to fit into nodes.  We mustn't overwrite any changes.
        newBlock.Node.SetEntries(_entries, 0, _entries.Count);

        _entries.Clear();
        _entries.Add(newRootEntry);

        return true;
    }

    /// <summary>
    /// Removes redundant nodes (that contain only an 'End' entry).
    /// </summary>
    /// <param name="entryIndex">The index of the entry that may have a redundant child.</param>
    /// <returns>An entry that needs to be promoted to the parent node (if any).</returns>
    private IndexEntry LiftNode(int entryIndex)
    {
        if ((_entries[entryIndex].Flags & IndexEntryFlags.Node) != 0)
        {
            var childNode = _index.GetSubBlock(_entries[entryIndex]).Node;
            if (childNode._entries.Count == 1)
            {
                var freeBlock = _entries[entryIndex].ChildrenVirtualCluster;
                _entries[entryIndex].Flags = (_entries[entryIndex].Flags & ~IndexEntryFlags.Node) |
                                             (childNode._entries[0].Flags & IndexEntryFlags.Node);
                _entries[entryIndex].ChildrenVirtualCluster = childNode._entries[0].ChildrenVirtualCluster;

                _index.FreeBlock(freeBlock);
            }

            if ((_entries[entryIndex].Flags & (IndexEntryFlags.Node | IndexEntryFlags.End)) == 0)
            {
                var entry = _entries[entryIndex];
                _entries.RemoveAt(entryIndex);

                var nextNode = _index.GetSubBlock(_entries[entryIndex]).Node;
                return nextNode.AddEntry(entry);
            }
        }

        return null;
    }

    private IndexEntry PopulateEnd()
    {
        if (_entries.Count > 1
            && _entries[_entries.Count - 1].Flags == IndexEntryFlags.End
            && (_entries[_entries.Count - 2].Flags & IndexEntryFlags.Node) != 0)
        {
            var old = _entries[_entries.Count - 2];
            _entries.RemoveAt(_entries.Count - 2);
            _entries[_entries.Count - 1].ChildrenVirtualCluster = old.ChildrenVirtualCluster;
            _entries[_entries.Count - 1].Flags |= IndexEntryFlags.Node;
            old.ChildrenVirtualCluster = 0;
            old.Flags = IndexEntryFlags.None;
            return _index.GetSubBlock(_entries[_entries.Count - 1]).Node.AddEntry(old);
        }

        return null;
    }

    private void InsertEntryThisNode(IndexEntry newEntry)
    {
        var index = GetEntry(newEntry.KeyBuffer, out var exactMatch);

        if (exactMatch)
        {
            throw new InvalidOperationException("Entry already exists");
        }

        _entries.Insert(index, newEntry);
    }

    private IndexEntry AddEntry(IndexEntry newEntry)
    {
        var index = GetEntry(newEntry.KeyBuffer, out var exactMatch);

        if (exactMatch)
        {
            throw new InvalidOperationException("Entry already exists");
        }

        if ((_entries[index].Flags & IndexEntryFlags.Node) != 0)
        {
            var ourNewEntry = _index.GetSubBlock(_entries[index]).Node.AddEntry(newEntry);
            if (ourNewEntry == null)
            {
                // No change to this node
                return null;
            }

            InsertEntryThisNode(ourNewEntry);
        }
        else
        {
            _entries.Insert(index, newEntry);
        }

        // If there wasn't enough space, we may need to
        // divide this node
        var newParentEntry = EnsureNodeSize();

        _store();

        return newParentEntry;
    }

    private IndexEntry EnsureNodeSize()
    {
        // While the node is too small to hold the entries, we need to reduce
        // the number of entries.
        if (SpaceFree < 0)
        {
            if (_isRoot)
            {
                Depose();
            }
            else
            {
                return Divide();
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the largest leaf entry in this tree.
    /// </summary>
    /// <returns>The index entry of the largest leaf.</returns>
    private IndexEntry FindLargestLeaf()
    {
        if ((_entries[_entries.Count - 1].Flags & IndexEntryFlags.Node) != 0)
        {
            return _index.GetSubBlock(_entries[_entries.Count - 1]).Node.FindLargestLeaf();
        }

        if (_entries.Count > 1 && (_entries[_entries.Count - 2].Flags & IndexEntryFlags.Node) == 0)
        {
            return _entries[_entries.Count - 2];
        }

        throw new IOException("Invalid index node found");
    }

    /// <summary>
    /// Only valid on non-root nodes, this method divides the node in two,
    /// adding the new node to the current parent.
    /// </summary>
    /// <returns>An entry that needs to be promoted to the parent node (if any).</returns>
    private IndexEntry Divide()
    {
        var midEntryIdx = _entries.Count / 2;
        var midEntry = _entries[midEntryIdx];

        // The terminating entry (aka end) for the new node
        var newTerm = new IndexEntry(_index.IsFileIndex);
        newTerm.Flags |= IndexEntryFlags.End;

        // The set of entries in the new node
        var newEntries = new List<IndexEntry>(midEntryIdx + 1);
        for (var i = 0; i < midEntryIdx; ++i)
        {
            newEntries.Add(_entries[i]);
        }

        newEntries.Add(newTerm);

        // Copy the node pointer from the elected 'mid' entry to the new node
        if ((midEntry.Flags & IndexEntryFlags.Node) != 0)
        {
            newTerm.ChildrenVirtualCluster = midEntry.ChildrenVirtualCluster;
            newTerm.Flags |= IndexEntryFlags.Node;
        }

        // Set the new entries into the new node
        var newBlock = _index.AllocateBlock(midEntry);

        // Set the entries into the new node.  Note we updated the parent
        // pointers first, because it's possible SetEntries may need to further
        // divide the entries to fit into nodes.  We mustn't overwrite any changes.
        newBlock.Node.SetEntries(newEntries, 0, newEntries.Count);

        // Forget about the entries moved into the new node, and the entry about
        // to be promoted as the new node's pointer
        _entries.RemoveRange(0, midEntryIdx + 1);

        // Promote the old mid entry
        return midEntry;
    }

    private void SetEntries(IList<IndexEntry> newEntries, int offset, int count)
    {
        _entries.Clear();
        for (var i = 0; i < count; ++i)
        {
            _entries.Add(newEntries[i + offset]);
        }

        // Add an end entry, if not present
        if (count == 0 || (_entries[_entries.Count - 1].Flags & IndexEntryFlags.End) == 0)
        {
            var end = new IndexEntry(_index.IsFileIndex)
            {
                Flags = IndexEntryFlags.End
            };
            _entries.Add(end);
        }

        // Ensure the node isn't over-filled
        if (SpaceFree < 0)
        {
            throw new IOException("Error setting node entries - oversized for node");
        }

        // Persist the new entries to disk
        _store();
    }
}