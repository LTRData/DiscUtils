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
using DiscUtils.Streams;

namespace DiscUtils.SquashFs;

/// <summary>
/// The extended form of a regular file inode (type 9): 64-bit start block and size, the number of bytes that are
/// sparse, the link count and an xattr index. mksquashfs uses it for any file larger than 4 GB, for sparse files
/// and for files with more than one hard link, so a disk image or a hard-linked file inside a SquashFS image is one.
/// The block list follows it exactly as for the basic form.
/// </summary>
internal sealed class ExtendedFileInode : RegularInode
{
    private long _fileSize;

    public long SparseBytes;
    public uint XattrIndex;

    public override long FileSize
    {
        get => _fileSize;
        set => _fileSize = value;
    }

    public override int Size => 56;

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        ReadHeader(buffer);
        StartBlock = EndianUtilities.ToInt64LittleEndian(buffer[16..]);
        _fileSize = EndianUtilities.ToInt64LittleEndian(buffer[24..]);
        SparseBytes = EndianUtilities.ToInt64LittleEndian(buffer[32..]);
        NumLinks = EndianUtilities.ToInt32LittleEndian(buffer[40..]);
        FragmentKey = EndianUtilities.ToUInt32LittleEndian(buffer[44..]);
        FragmentOffset = EndianUtilities.ToUInt32LittleEndian(buffer[48..]);
        XattrIndex = EndianUtilities.ToUInt32LittleEndian(buffer[52..]);
        return 56;
    }

    public override void WriteTo(Span<byte> buffer)
    {
        WriteHeader(buffer);
        EndianUtilities.WriteBytesLittleEndian(StartBlock, buffer[16..]);
        EndianUtilities.WriteBytesLittleEndian(_fileSize, buffer[24..]);
        EndianUtilities.WriteBytesLittleEndian(SparseBytes, buffer[32..]);
        EndianUtilities.WriteBytesLittleEndian(NumLinks, buffer[40..]);
        EndianUtilities.WriteBytesLittleEndian(FragmentKey, buffer[44..]);
        EndianUtilities.WriteBytesLittleEndian(FragmentOffset, buffer[48..]);
        EndianUtilities.WriteBytesLittleEndian(XattrIndex, buffer[52..]);
    }
}
