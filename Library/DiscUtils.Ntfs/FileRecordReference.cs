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

internal struct FileRecordReference : IByteArraySerializable, IComparable<FileRecordReference>, IEquatable<FileRecordReference>
{
    public FileRecordReference(ulong val)
    {
        Value = val;
    }

    public FileRecordReference(long mftIndex, ushort sequenceNumber)
    {
        Value = (ulong)(mftIndex & 0x0000FFFFFFFFFFFFL) |
                ((ulong)sequenceNumber << 48 & 0xFFFF000000000000L);
    }

    public ulong Value { get; private set; }

    public long MftIndex => (long)(Value & 0x0000FFFFFFFFFFFFL);

    public ushort SequenceNumber => (ushort)((Value >> 48) & 0xFFFF);

    public int Size => 8;

    public bool IsNull => SequenceNumber == 0;

    public static bool operator ==(FileRecordReference a, FileRecordReference b)
    {
        return a.Value == b.Value;
    }

    public static bool operator !=(FileRecordReference a, FileRecordReference b)
    {
        return a.Value != b.Value;
    }

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Value = EndianUtilities.ToUInt64LittleEndian(buffer);
        return 8;
    }

    public void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian(Value, buffer);
    }

    public bool Equals(FileRecordReference other)
        => Value.Equals(other.Value);

    public override bool Equals(object obj)
        => obj is FileRecordReference record && Value == record.Value;

    public override int GetHashCode()
        => Value.GetHashCode();

    public int CompareTo(FileRecordReference other)
        => Value.CompareTo(other.Value);

    public override string ToString()
        => $"MFT:{MftIndex} (ver: {SequenceNumber})";
}