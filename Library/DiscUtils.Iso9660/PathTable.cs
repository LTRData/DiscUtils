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

namespace DiscUtils.Iso9660;

internal class PathTable : BuilderExtent
{
    private readonly bool _byteSwap;
    private readonly List<BuildDirectoryInfo> _dirs;
    private readonly Encoding _enc;
    private readonly Dictionary<BuildDirectoryMember, uint> _locations;

    private byte[] _readCache;

    public PathTable(bool byteSwap, Encoding enc, List<BuildDirectoryInfo> dirs,
                     Dictionary<BuildDirectoryMember, uint> locations, long start)
        : base(start, CalcLength(enc, dirs))
    {
        _byteSwap = byteSwap;
        _enc = enc;
        _dirs = dirs;
        _locations = locations;
    }

    protected override void Dispose(bool disposing) {}

    public override void PrepareForRead()
    {
        _readCache = new byte[Length];
        var pos = 0;

        var sortedList = new List<BuildDirectoryInfo>(_dirs);
        sortedList.Sort(BuildDirectoryInfo.PathTableSortComparison);

        var dirNumbers = new Dictionary<BuildDirectoryInfo, ushort>(_dirs.Count);
        ushort i = 1;
        foreach (var di in sortedList)
        {
            dirNumbers[di] = i++;
            var ptr = new PathTableRecord();
            ptr.DirectoryIdentifier = di.PickName(null, _enc);
            ptr.LocationOfExtent = _locations[di];
            ptr.ParentDirectoryNumber = dirNumbers[di.Parent];

            pos += ptr.Write(_byteSwap, _enc, _readCache.AsSpan(pos));
        }
    }

    public override int Read(long diskOffset, byte[] buffer, int offset, int count)
    {
        var relPos = diskOffset - Start;

        var numRead = (int)Math.Min(count, _readCache.Length - relPos);

        System.Buffer.BlockCopy(_readCache, (int)relPos, buffer, offset, numRead);

        return numRead;
    }

    public override int Read(long diskOffset, Span<byte> buffer)
    {
        var relPos = diskOffset - Start;

        var numRead = (int)Math.Min(buffer.Length, _readCache.Length - relPos);

        _readCache.AsSpan((int)relPos, numRead).CopyTo(buffer);

        return numRead;
    }

    public override void DisposeReadState()
    {
        _readCache = null;
    }

    private static uint CalcLength(Encoding enc, List<BuildDirectoryInfo> dirs)
    {
        uint length = 0;
        foreach (var di in dirs)
        {
            length += di.GetPathTableEntrySize(enc);
        }

        return length;
    }
}