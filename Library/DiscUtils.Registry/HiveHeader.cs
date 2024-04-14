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
using System.IO;
using System.Text;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Registry;

public class HiveHeader : IByteArraySerializable
{
    public const int HeaderSize = 512;

    public const uint Signature = 0x66676572;
    public uint Checksum;
    public Guid Guid1;
    public Guid Guid2;
    public int Length;
    public int MajorVersion;
    public int MinorVersion;
    public string Path;
    public int RootCell;

    public int Sequence1;
    public int Sequence2;
    public DateTime Timestamp;

    public HiveHeader()
    {
        Sequence1 = 1;
        Sequence2 = 1;
        Timestamp = DateTime.UtcNow;
        MajorVersion = 1;
        MinorVersion = 3;
        RootCell = -1;
        Path = string.Empty;
        Guid1 = Guid.NewGuid();
        Guid2 = Guid.NewGuid();
    }

    public int Size => HeaderSize;

    public int ReadFrom(ReadOnlySpan<byte> buffer) => ReadFrom(buffer, throwOnInvalidData: true);

    public int ReadFrom(ReadOnlySpan<byte> buffer, bool throwOnInvalidData)
    {
        var sig = EndianUtilities.ToUInt32LittleEndian(buffer);
        if (sig != Signature)
        {
            if (throwOnInvalidData)
            {
                throw new IOException("Invalid signature for registry hive");
            }
            else
            {
                return 0;
            }
        }

        Sequence1 = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x0004));
        Sequence2 = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x0008));

        Timestamp = DateTime.FromFileTimeUtc(EndianUtilities.ToInt64LittleEndian(buffer.Slice(0x000C)));

        MajorVersion = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x0014));
        MinorVersion = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x0018));

        var isLog = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x001C));

        RootCell = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x0024));
        Length = EndianUtilities.ToInt32LittleEndian(buffer.Slice(0x0028));

        Path = EndianUtilities.LittleEndianUnicodeBytesToString(buffer.Slice(0x0030, 0x0040)).Trim('\0');

        Guid1 = EndianUtilities.ToGuidLittleEndian(buffer.Slice(0x0070));
        Guid2 = EndianUtilities.ToGuidLittleEndian(buffer.Slice(0x0094));

        Checksum = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x01FC));

        if (Checksum != CalcChecksum(buffer))
        {
            if (throwOnInvalidData)
            {
                throw new IOException("Invalid checksum on registry file");
            }
            else
            {
                return 0;
            }
        }

        return HeaderSize;
    }

    public void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian(Signature, buffer);
        EndianUtilities.WriteBytesLittleEndian(Sequence1, buffer.Slice(0x0004));
        EndianUtilities.WriteBytesLittleEndian(Sequence2, buffer.Slice(0x0008));
        EndianUtilities.WriteBytesLittleEndian(Timestamp.ToFileTimeUtc(), buffer.Slice(0x000C));
        EndianUtilities.WriteBytesLittleEndian(MajorVersion, buffer.Slice(0x0014));
        EndianUtilities.WriteBytesLittleEndian(MinorVersion, buffer.Slice(0x0018));

        EndianUtilities.WriteBytesLittleEndian((uint)1, buffer.Slice(0x0020)); // Unknown - seems to be '1'

        EndianUtilities.WriteBytesLittleEndian(RootCell, buffer.Slice(0x0024));
        EndianUtilities.WriteBytesLittleEndian(Length, buffer.Slice(0x0028));

        Encoding.Unicode.GetBytes(Path.AsSpan(), buffer.Slice(0x0030));
        EndianUtilities.WriteBytesLittleEndian((ushort)0, buffer.Slice(0x0030 + Path.Length * 2));

        EndianUtilities.WriteBytesLittleEndian(Guid1, buffer.Slice(0x0070));
        EndianUtilities.WriteBytesLittleEndian(Guid2, buffer.Slice(0x0094));

        EndianUtilities.WriteBytesLittleEndian(CalcChecksum(buffer), buffer.Slice(0x01FC));
    }

    public static uint CalcChecksum(ReadOnlySpan<byte> buffer)
    {
        uint sum = 0;

        for (var i = 0; i < 0x01FC; i += 4)
        {
            sum ^= EndianUtilities.ToUInt32LittleEndian(buffer.Slice(i));
        }

        return sum;
    }
}