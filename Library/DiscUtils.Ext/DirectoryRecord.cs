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
using System.Text;
using DiscUtils.CoreCompat;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Ext;

internal struct DirectoryRecord : IByteArraySerializable
{
    public const byte FileTypeUnknown = 0;
    public const byte FileTypeRegularFile = 1;
    public const byte FileTypeDirectory = 2;
    public const byte FileTypeCharacterDevice = 3;
    public const byte FileTypeBlockDevice = 4;
    public const byte FileTypeFifo = 5;
    public const byte FileTypeSocket = 6;
    public const byte FileTypeSymlink = 7;

    private readonly Encoding _nameEncoding;
    public byte FileType;

    public uint Inode;
    public string Name;

    public DirectoryRecord(Encoding nameEncoding) : this()
    {
        _nameEncoding = nameEncoding;
    }

    public int Size => MathUtilities.RoundUp(8 + Name.Length, 4);

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Inode = EndianUtilities.ToUInt32LittleEndian(buffer);
        var recordLen = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(4));
        int nameLen = buffer[6];
        FileType = buffer[7];
        Name = _nameEncoding.GetString(buffer.Slice(8, nameLen)).SanitizeFileName();

        return recordLen;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}