//
// Copyright (c) 2008-2012, Kenneth Bell
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
using DiscUtils.Internal;
using DiscUtils.Streams;

namespace DiscUtils.Vhdx;

internal sealed class VhdxHeader : IByteArraySerializable
{
    public const uint VhdxHeaderSignature = 0x64616568;
    private readonly byte[] _data = new byte[4096];
    public uint Checksum;
    public Guid DataWriteGuid;
    public Guid FileWriteGuid;
    public Guid LogGuid;
    public uint LogLength;
    public ulong LogOffset;
    public ushort LogVersion;
    public ulong SequenceNumber;

    public uint Signature = VhdxHeaderSignature;
    public ushort Version;

    public VhdxHeader() {}

    public VhdxHeader(VhdxHeader header)
    {
        System.Buffer.BlockCopy(header._data, 0, _data, 0, 4096);

        Signature = header.Signature;
        Checksum = header.Checksum;
        SequenceNumber = header.SequenceNumber;
        FileWriteGuid = header.FileWriteGuid;
        DataWriteGuid = header.DataWriteGuid;
        LogGuid = header.LogGuid;
        LogVersion = header.LogVersion;
        Version = header.Version;
        LogLength = header.LogLength;
        LogOffset = header.LogOffset;
    }

    public bool IsValid
    {
        get
        {
            if (Signature != VhdxHeaderSignature)
            {
                return false;
            }

            var checkData = new byte[4096];
            System.Buffer.BlockCopy(_data, 0, checkData, 0, 4096);
            EndianUtilities.WriteBytesLittleEndian((uint)0, checkData, 4);
            return Checksum == Crc32LittleEndian.Compute(Crc32Algorithm.Castagnoli, checkData, 0, 4096);
        }
    }

    public int Size => 4 * Sizes.OneKiB;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        buffer.Slice(0, 4096).CopyTo(_data);

        Signature = EndianUtilities.ToUInt32LittleEndian(_data, 0);
        Checksum = EndianUtilities.ToUInt32LittleEndian(_data, 4);

        SequenceNumber = EndianUtilities.ToUInt64LittleEndian(_data, 8);
        FileWriteGuid = EndianUtilities.ToGuidLittleEndian(_data, 16);
        DataWriteGuid = EndianUtilities.ToGuidLittleEndian(_data, 32);
        LogGuid = EndianUtilities.ToGuidLittleEndian(_data, 48);
        LogVersion = EndianUtilities.ToUInt16LittleEndian(_data, 64);
        Version = EndianUtilities.ToUInt16LittleEndian(_data, 66);
        LogLength = EndianUtilities.ToUInt32LittleEndian(_data, 68);
        LogOffset = EndianUtilities.ToUInt64LittleEndian(_data, 72);

        return Size;
    }

    public void WriteTo(Span<byte> buffer)
    {
        RefreshData();
        _data.AsSpan(0, 4 * Sizes.OneKiB).CopyTo(buffer);
    }

    public void CalcChecksum()
    {
        Checksum = 0;
        RefreshData();
        Checksum = Crc32LittleEndian.Compute(Crc32Algorithm.Castagnoli, _data, 0, 4096);
    }

    private void RefreshData()
    {
        EndianUtilities.WriteBytesLittleEndian(Signature, _data, 0);
        EndianUtilities.WriteBytesLittleEndian(Checksum, _data, 4);
        EndianUtilities.WriteBytesLittleEndian(SequenceNumber, _data, 8);
        EndianUtilities.WriteBytesLittleEndian(FileWriteGuid, _data, 16);
        EndianUtilities.WriteBytesLittleEndian(DataWriteGuid, _data, 32);
        EndianUtilities.WriteBytesLittleEndian(LogGuid, _data, 48);
        EndianUtilities.WriteBytesLittleEndian(LogVersion, _data, 64);
        EndianUtilities.WriteBytesLittleEndian(Version, _data, 66);
        EndianUtilities.WriteBytesLittleEndian(LogLength, _data, 68);
        EndianUtilities.WriteBytesLittleEndian(LogOffset, _data, 72);
    }
}