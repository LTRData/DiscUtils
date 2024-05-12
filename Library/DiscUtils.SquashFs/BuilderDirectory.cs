//
// Copyright (c) 2008-2024, Kenneth Bell, Olof Lagerkvist and contributors
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

using DiscUtils.Internal;
using System;
using System.Collections.Generic;
using System.IO;

namespace DiscUtils.SquashFs;

internal sealed class BuilderDirectory : BuilderNode
{
    private readonly List<Entry> _children;
    private readonly FastDictionary<Entry> _index;
    private DirectoryInode _inode;

    public BuilderDirectory()
    {
        _children = [];
        _index = new FastDictionary<Entry>(StringComparer.Ordinal, entry => entry.Name);
    }

    public override Inode Inode => _inode;

    public void AddChild(string name, BuilderNode node)
    {
        if (name.IndexOfAny(Utilities.PathSeparators) >= 0)
        {
            throw new ArgumentException("Single level of path must be provided", nameof(name));
        }

        if (_index.ContainsKey(name))
        {
            throw new IOException($"The directory entry '{name}' already exists");
        }

        var newEntry = new Entry { Name = name, Node = node };
        _children.Add(newEntry);
        _index.Add(newEntry);
    }

    public BuilderNode GetChild(string name)
    {
        if (_index.TryGetValue(name, out var result))
        {
            return result.Node;
        }

        return null;
    }

    public IEnumerable<KeyValuePair<string, BuilderNode>> EnumerateTree()
    {
        foreach (var entry in _index)
        {
            if (entry.Node is BuilderDirectory subdir)
            {
                foreach (var subdir_entry in subdir.EnumerateTree())
                {
                    yield return new KeyValuePair<string, BuilderNode>(Path.Combine(entry.Name, subdir_entry.Key), subdir_entry.Value);
                }
            }

            yield return new KeyValuePair<string, BuilderNode>(entry.Name, entry.Node);
        }
    }

    public IEnumerable<BuilderNode> EnumerateTreeEntries()
    {
        foreach (var entry in _index)
        {
            if (entry.Node is BuilderDirectory subdir)
            {
                foreach (var subdir_entry in subdir.EnumerateTreeEntries())
                {
                    yield return subdir_entry;
                }
            }

            yield return entry.Node;
        }
    }

    public override void Reset()
    {
        foreach (var entry in _children)
        {
            entry.Node.Reset();
        }

        _inode = new DirectoryInode();
    }

    public override void Write(BuilderContext context)
    {
        if (_written)
        {
            return;
        }

        _children.Sort();

        foreach (var entry in _children)
        {
            entry.Node.Write(context);
        }

        WriteDirectory(context);

        WriteInode(context);

        _written = true;
    }

    private void WriteDirectory(BuilderContext context)
    {
        var startPos = context.DirectoryWriter.Position;

        var currentChild = 0;
        var numDirs = 0;
        while (currentChild < _children.Count)
        {
            var thisBlock = _children[currentChild].Node.InodeRef.Block;
            var firstInode = _children[currentChild].Node.InodeNumber;

            var count = 1;
            while (currentChild + count < _children.Count
                   && _children[currentChild + count].Node.InodeRef.Block == thisBlock
                   && _children[currentChild + count].Node.InodeNumber - firstInode < 0x7FFF)
            {
                ++count;
            }

            var hdr = new DirectoryHeader
            {
                Count = count - 1,
                InodeNumber = firstInode,
                StartBlock = (int)thisBlock
            };

            hdr.WriteTo(context.IoBuffer);
            context.DirectoryWriter.Write(context.IoBuffer, 0, hdr.Size);

            for (var i = 0; i < count; ++i)
            {
                var child = _children[currentChild + i];
                var record = new DirectoryRecord
                {
                    Offset = (ushort)child.Node.InodeRef.Offset,
                    InodeNumber = (short)(child.Node.InodeNumber - firstInode),
                    Type = child.Node.Inode.Type,
                    Name = child.Name
                };

                record.WriteTo(context.IoBuffer);
                context.DirectoryWriter.Write(context.IoBuffer, 0, record.Size);

                if (child.Node.Inode.Type is InodeType.Directory
                    or InodeType.ExtendedDirectory)
                {
                    ++numDirs;
                }
            }

            currentChild += count;
        }

        var size = context.DirectoryWriter.DistanceFrom(startPos);
        if (size > uint.MaxValue)
        {
            throw new NotImplementedException("Writing large directories");
        }

        NumLinks = numDirs + 2; // +1 for self, +1 for parent

        _inode.StartBlock = (uint)startPos.Block;
        _inode.Offset = (ushort)startPos.Offset;
        _inode.FileSize = (uint)size + 3; // For some reason, always +3
    }

    private void WriteInode(BuilderContext context)
    {
        FillCommonInodeData(context);
        _inode.Type = InodeType.Directory;

        InodeRef = context.InodeWriter.Position;

        _inode.WriteTo(context.IoBuffer);

        context.InodeWriter.Write(context.IoBuffer, 0, _inode.Size);
    }

    private class Entry : IComparable<Entry>
    {
        public string Name;
        public BuilderNode Node;

        public int CompareTo(Entry other)
        {
            return string.CompareOrdinal(Name, other.Name);
        }
    }
}