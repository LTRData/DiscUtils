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
using System.IO;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Ntfs;

internal abstract class FixupRecordBase
{
    private int _sectorSize;
    private ushort[] _updateSequenceArray;

    public FixupRecordBase(string magic, int sectorSize)
    {
        Magic = magic;
        _sectorSize = sectorSize;
    }

    public FixupRecordBase(string magic, int sectorSize, int recordLength)
    {
        Initialize(magic, sectorSize, recordLength);
    }

    public string Magic { get; private set; }

    public int Size
    {
        get { return CalcSize(); }
    }

    public ushort UpdateSequenceCount { get; private set; }

    public ushort UpdateSequenceNumber { get; private set; }

    public ushort UpdateSequenceOffset { get; private set; }

    public int UpdateSequenceSize
    {
        get { return UpdateSequenceCount * 2; }
    }

    public void FromStream(Stream stream, int length, bool ignoreMagic = false)
    {
        byte[] allocated = null;

        var buffer = length <= 1024
            ? stackalloc byte[length]
            : (allocated = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);

        try
        {
            stream.ReadExactly(buffer);
            FromBytes(buffer, ignoreMagic);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    public void FromBytes(Span<byte> buffer, bool ignoreMagic = false)
    {
        var diskMagic = EndianUtilities.BytesToString(buffer.Slice(0x00, 4));
        if (Magic == null)
        {
            Magic = diskMagic;
        }
        else
        {
            if (diskMagic != Magic && ignoreMagic)
            {
                return;
            }

            if (diskMagic != Magic)
            {
                throw new IOException("Corrupt record");
            }
        }

        UpdateSequenceOffset = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x04));
        UpdateSequenceCount = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x06));

        UpdateSequenceNumber = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(UpdateSequenceOffset));
        _updateSequenceArray = new ushort[UpdateSequenceCount - 1];
        for (var i = 0; i < _updateSequenceArray.Length; ++i)
        {
            _updateSequenceArray[i] = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(UpdateSequenceOffset + 2 * (i + 1)));
        }

        UnprotectBuffer(buffer);

        Read(buffer);
    }

    public void ToBytes(Span<byte> buffer)
    {
        UpdateSequenceOffset = Write(buffer);

        ProtectBuffer(buffer);

        EndianUtilities.StringToBytes(Magic, buffer.Slice(0x00, 4));
        EndianUtilities.WriteBytesLittleEndian(UpdateSequenceOffset, buffer.Slice(0x04));
        EndianUtilities.WriteBytesLittleEndian(UpdateSequenceCount, buffer.Slice(0x06));

        EndianUtilities.WriteBytesLittleEndian(UpdateSequenceNumber, buffer.Slice(UpdateSequenceOffset));
        for (var i = 0; i < _updateSequenceArray.Length; ++i)
        {
            EndianUtilities.WriteBytesLittleEndian(_updateSequenceArray[i], buffer.Slice(UpdateSequenceOffset + 2 * (i + 1)));
        }
    }

    public void ToStream(Stream stream, int length)
    {
        byte[] allocated = null;

        var buffer = length <= 1024
            ? stackalloc byte[length]
            : (allocated = ArrayPool<byte>.Shared.Rent(length)).AsSpan(0, length);

        try
        {
            buffer.Clear();
            ToBytes(buffer);
            stream.Write(buffer);
        }
        finally
        {
            if (allocated is not null)
            {
                ArrayPool<byte>.Shared.Return(allocated);
            }
        }
    }

    protected void Initialize(string magic, int sectorSize, int recordLength)
    {
        Magic = magic;
        _sectorSize = sectorSize;
        UpdateSequenceCount = (ushort)(1 + MathUtilities.Ceil(recordLength, Sizes.Sector));
        UpdateSequenceNumber = 1;
        _updateSequenceArray = new ushort[UpdateSequenceCount - 1];
    }

    protected abstract void Read(ReadOnlySpan<byte> buffer);

    protected abstract ushort Write(Span<byte> buffer);

    protected abstract int CalcSize();

    private void UnprotectBuffer(Span<byte> buffer)
    {
        // First do validation check - make sure the USN matches on all sectors)
        for (var i = 0; i < _updateSequenceArray.Length; ++i)
        {
            if (UpdateSequenceNumber != EndianUtilities.ToUInt16LittleEndian(buffer.Slice(Sizes.Sector * (i + 1) - 2)))
            {
                throw new IOException("Corrupt file system record found");
            }
        }

        // Now replace the USNs with the actual data from the sequence array
        for (var i = 0; i < _updateSequenceArray.Length; ++i)
        {
            EndianUtilities.WriteBytesLittleEndian(_updateSequenceArray[i], buffer.Slice(Sizes.Sector * (i + 1) - 2));
        }
    }

    private void ProtectBuffer(Span<byte> buffer)
    {
        UpdateSequenceNumber++;

        // Read in the bytes that are replaced by the USN
        for (var i = 0; i < _updateSequenceArray.Length; ++i)
        {
            _updateSequenceArray[i] = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(Sizes.Sector * (i + 1) - 2));
        }

        // Overwrite the bytes that are replaced with the USN
        for (var i = 0; i < _updateSequenceArray.Length; ++i)
        {
            EndianUtilities.WriteBytesLittleEndian(UpdateSequenceNumber, buffer.Slice(Sizes.Sector * (i + 1) - 2));
        }
    }
}