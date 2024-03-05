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
using System.Buffers;
using DiscUtils.Internal;
using DiscUtils.Streams;

namespace DiscUtils.Partitions;

internal class GptHeader
{
    public const string GptSignature = "EFI PART";
    public long AlternateHeaderLba;

    public byte[] Buffer;
    public uint Crc;
    public Guid DiskGuid;
    public uint EntriesCrc;
    public long FirstUsable;
    public long HeaderLba;
    public int HeaderSize;
    public long LastUsable;
    public long PartitionEntriesLba;
    public uint PartitionEntryCount;
    public int PartitionEntrySize;

    public string Signature;
    public uint Version;

    public GptHeader(int sectorSize)
    {
        Signature = GptSignature;
        Version = 0x00010000;
        HeaderSize = 92;
        Buffer = new byte[sectorSize];
    }

    public GptHeader(GptHeader toCopy)
    {
        Signature = toCopy.Signature;
        Version = toCopy.Version;
        HeaderSize = toCopy.HeaderSize;
        Crc = toCopy.Crc;
        HeaderLba = toCopy.HeaderLba;
        AlternateHeaderLba = toCopy.AlternateHeaderLba;
        FirstUsable = toCopy.FirstUsable;
        LastUsable = toCopy.LastUsable;
        DiskGuid = toCopy.DiskGuid;
        PartitionEntriesLba = toCopy.PartitionEntriesLba;
        PartitionEntryCount = toCopy.PartitionEntryCount;
        PartitionEntrySize = toCopy.PartitionEntrySize;
        EntriesCrc = toCopy.EntriesCrc;

        Buffer = new byte[toCopy.Buffer.Length];
        System.Buffer.BlockCopy(toCopy.Buffer, 0, Buffer, 0, Buffer.Length);
    }

    public bool ReadFrom(ReadOnlySpan<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        Signature = latin1Encoding.GetString(buffer.Slice(0, 8));
        Version = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(8));
        HeaderSize = EndianUtilities.ToInt32LittleEndian(buffer.Slice(12));
        Crc = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(16));
        HeaderLba = EndianUtilities.ToInt64LittleEndian(buffer.Slice(24));
        AlternateHeaderLba = EndianUtilities.ToInt64LittleEndian(buffer.Slice(32));
        FirstUsable = EndianUtilities.ToInt64LittleEndian(buffer.Slice(40));
        LastUsable = EndianUtilities.ToInt64LittleEndian(buffer.Slice(48));
        DiskGuid = EndianUtilities.ToGuidLittleEndian(buffer.Slice(56));
        PartitionEntriesLba = EndianUtilities.ToInt64LittleEndian(buffer.Slice(72));
        PartitionEntryCount = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(80));
        PartitionEntrySize = EndianUtilities.ToInt32LittleEndian(buffer.Slice(84));
        EntriesCrc = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(88));

        // Reject obviously invalid data
        if (Signature != GptSignature || HeaderSize <= 0)
        {
            return false;
        }

        // In case the header has new fields unknown to us, store the entire header
        // as a byte array
        Buffer = new byte[HeaderSize];
        buffer.Slice(0, HeaderSize).CopyTo(Buffer);

        return Crc == CalcCrc(Buffer, 0, HeaderSize);
    }

    public void WriteTo(Span<byte> buffer)
    {
        // First, copy the cached header to allow for unknown fields
        Buffer.CopyTo(buffer);

        // Next, write the fields
        EncodingUtilities
            .GetLatin1Encoding()
            .GetBytes(Signature, buffer.Slice(0, 8));

        EndianUtilities.WriteBytesLittleEndian(Version, buffer.Slice(8));
        EndianUtilities.WriteBytesLittleEndian(HeaderSize, buffer.Slice(12));
        EndianUtilities.WriteBytesLittleEndian((uint)0, buffer.Slice(16));
        EndianUtilities.WriteBytesLittleEndian(HeaderLba, buffer.Slice(24));
        EndianUtilities.WriteBytesLittleEndian(AlternateHeaderLba, buffer.Slice(32));
        EndianUtilities.WriteBytesLittleEndian(FirstUsable, buffer.Slice(40));
        EndianUtilities.WriteBytesLittleEndian(LastUsable, buffer.Slice(48));
        EndianUtilities.WriteBytesLittleEndian(DiskGuid, buffer.Slice(56));
        EndianUtilities.WriteBytesLittleEndian(PartitionEntriesLba, buffer.Slice(72));
        EndianUtilities.WriteBytesLittleEndian(PartitionEntryCount, buffer.Slice(80));
        EndianUtilities.WriteBytesLittleEndian(PartitionEntrySize, buffer.Slice(84));
        EndianUtilities.WriteBytesLittleEndian(EntriesCrc, buffer.Slice(88));

        // Calculate & write the CRC
        EndianUtilities.WriteBytesLittleEndian(CalcCrc(buffer.Slice(0, HeaderSize)), buffer.Slice(16));

        // Update the cached copy - re-allocate the buffer to allow for HeaderSize potentially having changed
        Buffer = new byte[HeaderSize];
        buffer.Slice(0, HeaderSize).CopyTo(Buffer);
    }

    internal static uint CalcCrc(byte[] buffer, int offset, int count)
        => CalcCrc(buffer.AsSpan(offset, count));

    internal static uint CalcCrc(ReadOnlySpan<byte> buffer)
    {
        var temp = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            buffer.CopyTo(temp);

            // Reset CRC field
            EndianUtilities.WriteBytesLittleEndian((uint)0, temp, 16);

            return Crc32LittleEndian.Compute(Crc32Algorithm.Common, temp.AsSpan(0, buffer.Length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(temp);
        }
    }
}