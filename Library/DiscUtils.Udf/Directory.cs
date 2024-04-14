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
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using DiscUtils.Vfs;

namespace DiscUtils.Udf;

internal class Directory : File, IVfsDirectory<FileIdentifier, File>
{
    private readonly FastDictionary<FileIdentifier> _entries;

    public Directory(UdfContext context, LogicalPartition partition, FileEntry fileEntry)
        : base(context, partition, fileEntry, (uint)partition.LogicalBlockSize)
    {
        if (FileContent.Capacity > int.MaxValue)
        {
            throw new NotImplementedException("Very large directory");
        }

        _entries = new(StringComparer.OrdinalIgnoreCase, entry => entry.Name);

        var contentBytes = FileContent.ReadExactly(0, (int)FileContent.Capacity);

        var pos = 0;
        while (pos < contentBytes.Length)
        {
            var id = new FileIdentifier();
            var size = id.ReadFrom(contentBytes, pos);

            if ((id.FileCharacteristics & (FileCharacteristic.Deleted | FileCharacteristic.Parent)) == 0)
            {
                _entries.Add(id);
            }

            pos += size;
        }
    }

    public IReadOnlyDictionary<string, FileIdentifier> AllEntries => _entries;

    public FileIdentifier Self => null;

    public FileIdentifier CreateNewFile(string name)
    {
        throw new NotSupportedException();
    }

    public FileIdentifier GetEntryByName(string name)
        => AllEntries.TryGetValue(name, out var entry) ? entry : null;
}