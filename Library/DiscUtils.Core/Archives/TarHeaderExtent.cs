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


using DiscUtils.Streams;
using System;

namespace DiscUtils.Archives;
internal sealed class TarHeaderExtent : BuilderBufferExtent
{
    private string _fileName;
    private long _fileLength;
    private UnixFilePermissions _mode;
    private int _ownerId;
    private int _groupId;
    private DateTime _modificationTime;

    public TarHeaderExtent(long start, string fileName, long fileLength, UnixFilePermissions mode, int ownerId, int groupId, DateTime modificationTime)
        : base(start, 512)
    {
        _fileName = fileName;
        _fileLength = fileLength;
        _mode = mode;
        _ownerId = ownerId;
        _groupId = groupId;
        _modificationTime = modificationTime;
    }

    public TarHeaderExtent(long start, string fileName, long fileLength)
        : this(start, fileName, fileLength, 0, 0, 0, DateTimeOffset.FromUnixTimeMilliseconds(0).UtcDateTime)
    {
    }

    protected override byte[] GetBuffer()
    {
        var buffer = new byte[TarHeader.Length];

        var header = new TarHeader(
            fileName: _fileName,
            fileLength: _fileLength,
            fileMode: _mode,
            ownerId: _ownerId,
            groupId: _groupId,
            modificationTime: _modificationTime);

        header.WriteTo(buffer);

        return buffer;
    }

    public override string ToString() => _fileName;
}
