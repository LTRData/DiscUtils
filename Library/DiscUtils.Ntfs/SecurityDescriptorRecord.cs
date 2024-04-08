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

namespace DiscUtils.Ntfs;

internal sealed class SecurityDescriptorRecord : IByteArraySerializable
{
    public uint EntrySize;
    public uint Hash;
    public uint Id;
    public long OffsetInFile;
    public byte[] SecurityDescriptor;

    public int Size
    {
        get { return SecurityDescriptor.Length + 0x14; }
    }

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Read(buffer);
        return SecurityDescriptor.Length + 0x14;
    }

    public void WriteTo(Span<byte> buffer)
    {
        EntrySize = (uint)Size;

        EndianUtilities.WriteBytesLittleEndian(Hash, buffer);
        EndianUtilities.WriteBytesLittleEndian(Id, buffer.Slice(0x04));
        EndianUtilities.WriteBytesLittleEndian(OffsetInFile, buffer.Slice(0x08));
        EndianUtilities.WriteBytesLittleEndian(EntrySize, buffer.Slice(0x10));

        SecurityDescriptor.CopyTo(buffer.Slice(0x14));
    }

    public bool Read(ReadOnlySpan<byte> buffer)
    {
        Hash = EndianUtilities.ToUInt32LittleEndian(buffer);
        Id = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x04));
        OffsetInFile = EndianUtilities.ToInt64LittleEndian(buffer.Slice(0x08));
        EntrySize = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x10));

        if (EntrySize > 0)
        {
            SecurityDescriptor = buffer.Slice(0x14, (int)(EntrySize - 0x14)).ToArray();
            return true;
        }

        return false;
    }
}