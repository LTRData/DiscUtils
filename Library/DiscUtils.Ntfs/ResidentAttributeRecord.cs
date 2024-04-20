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
using System.IO;
using System.Linq;
using System.Text;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using Buffer = DiscUtils.Streams.Buffer;

namespace DiscUtils.Ntfs;

internal sealed class ResidentAttributeRecord : AttributeRecord
{
    private byte _indexedFlag;
    private SparseMemoryBuffer _memoryBuffer;

    public ResidentAttributeRecord(ReadOnlySpan<byte> buffer, out int length)
    {
        Read(buffer, out length);
    }

    public ResidentAttributeRecord(AttributeType type, string name, ushort id, bool indexed, AttributeFlags flags)
        : base(type, name, id, flags)
    {
        _nonResidentFlag = 0;
        _indexedFlag = (byte)(indexed ? 1 : 0);
        _memoryBuffer = new SparseMemoryBuffer(1024);
    }

    public override long AllocatedLength
    {
        get => MathUtilities.RoundUp(DataLength, 8);
        set => throw new NotSupportedException();
    }

    public Buffer DataBuffer => _memoryBuffer;

    public override long DataLength
    {
        get => _memoryBuffer.Capacity;
        set => throw new NotSupportedException();
    }

    public int DataOffset
    {
        get
        {
            byte nameLength = 0;
            if (Name != null)
            {
                nameLength = (byte)Name.Length;
            }

            return MathUtilities.RoundUp(0x18 + nameLength * 2, 8);
        }
    }

    /// <summary>
    /// The amount of initialized data in the attribute (in bytes).
    /// </summary>
    public override long InitializedDataLength
    {
        get => DataLength;
        set => throw new NotSupportedException();
    }

    public override int Size
    {
        get
        {
            byte nameLength = 0;
            ushort nameOffset = 0x18;
            if (Name != null)
            {
                nameLength = (byte)Name.Length;
            }

            var dataOffset = (ushort)MathUtilities.RoundUp(nameOffset + nameLength * 2, 8);
            return (int)MathUtilities.RoundUp(dataOffset + _memoryBuffer.Capacity, 8);
        }
    }

    public override long StartVcn => 0;

    public override IBuffer GetReadOnlyDataBuffer(INtfsContext context)
    {
        return _memoryBuffer;
    }

    public override CookedDataRuns GetCookedDataRuns() => new();

    public override IEnumerable<Range<long, long>> GetClusters()
    {
        return Enumerable.Empty<Range<long, long>>();
    }

    public override int Write(Span<byte> buffer)
    {
        byte nameLength = 0;
        ushort nameOffset = 0;
        if (Name != null)
        {
            nameOffset = 0x18;
            nameLength = (byte)Name.Length;
        }

        var dataOffset = (ushort)MathUtilities.RoundUp(0x18 + nameLength * 2, 8);
        var length = (int)MathUtilities.RoundUp(dataOffset + _memoryBuffer.Capacity, 8);

        EndianUtilities.WriteBytesLittleEndian((uint)_type, buffer.Slice(0x00));
        EndianUtilities.WriteBytesLittleEndian(length, buffer.Slice(0x04));
        buffer[0x08] = _nonResidentFlag;
        buffer[0x09] = nameLength;
        EndianUtilities.WriteBytesLittleEndian(nameOffset, buffer.Slice(0x0A));
        EndianUtilities.WriteBytesLittleEndian((ushort)_flags, buffer.Slice(0x0C));
        EndianUtilities.WriteBytesLittleEndian(_attributeId, buffer.Slice(0x0E));
        EndianUtilities.WriteBytesLittleEndian((int)_memoryBuffer.Capacity, buffer.Slice(0x10));
        EndianUtilities.WriteBytesLittleEndian(dataOffset, buffer.Slice(0x14));
        buffer[0x16] = _indexedFlag;
        buffer[0x17] = 0; // Padding

        if (Name != null)
        {
            Encoding.Unicode.GetBytes(Name.AsSpan(), buffer.Slice(nameOffset, nameLength * 2));
        }

        _memoryBuffer.Read(0, buffer.Slice(dataOffset, (int)_memoryBuffer.Capacity));

        return length;
    }

    public override void Dump(TextWriter writer, string indent)
    {
        base.Dump(writer, indent);
        writer.WriteLine($"{indent}     Data Length: {DataLength}");
        writer.WriteLine($"{indent}         Indexed: {_indexedFlag}");
    }

    protected override void Read(ReadOnlySpan<byte> buffer, out int length)
    {
        base.Read(buffer, out length);

        var dataLength = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x10));
        var dataOffset = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(0x14));
        _indexedFlag = buffer[0x16];

        if (dataOffset + dataLength > length)
        {
            throw new IOException("Corrupt attribute, data outside of attribute");
        }

        _memoryBuffer = new SparseMemoryBuffer(1024);
        _memoryBuffer.Write(0, buffer.Slice(dataOffset, (int)dataLength));
    }
}