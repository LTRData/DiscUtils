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

namespace DiscUtils.Partitions;

internal class BiosPartitionRecord : IComparable<BiosPartitionRecord>
{
    private readonly uint _lbaOffset;

    public BiosPartitionRecord() {}

    public BiosPartitionRecord(ReadOnlySpan<byte> data, uint lbaOffset, int index)
    {
        _lbaOffset = lbaOffset;

        Status = data[0];
        StartHead = data[1];
        StartSector = (byte)(data[2] & 0x3F);
        StartCylinder = (ushort)(data[3] | ((data[2] & 0xC0) << 2));
        PartitionType = data[4];
        EndHead = data[5];
        EndSector = (byte)(data[6] & 0x3F);
        EndCylinder = (ushort)(data[7] | ((data[6] & 0xC0) << 2));
        LBAStart = EndianUtilities.ToUInt32LittleEndian(data.Slice(8));
        LBALength = EndianUtilities.ToUInt32LittleEndian(data.Slice(12));
        Index = index;
    }

    public ushort EndCylinder { get; set; }

    public byte EndHead { get; set; }

    public byte EndSector { get; set; }

    public string FriendlyPartitionType => BiosPartitionTypes.ToString(PartitionType);

    public int Index { get; }

    public bool IsValid => EndHead != 0 || EndSector != 0 || EndCylinder != 0 || LBALength != 0;

    public uint LBALength { get; set; }

    public uint LBAStart { get; set; }

    public uint LBAStartAbsolute => LBAStart + _lbaOffset;

    public byte PartitionType { get; set; }

    public ushort StartCylinder { get; set; }

    public byte StartHead { get; set; }

    public byte StartSector { get; set; }

    public byte Status { get; set; }

    public int CompareTo(BiosPartitionRecord other)
    {
        return LBAStartAbsolute.CompareTo(other.LBAStartAbsolute);
    }

    internal void WriteTo(Span<byte> buffer)
    {
        buffer[0] = Status;
        buffer[1] = StartHead;
        buffer[2] = (byte)((StartSector & 0x3F) | ((StartCylinder >> 2) & 0xC0));
        buffer[3] = (byte)StartCylinder;
        buffer[4] = PartitionType;
        buffer[5] = EndHead;
        buffer[6] = (byte)((EndSector & 0x3F) | ((EndCylinder >> 2) & 0xC0));
        buffer[7] = (byte)EndCylinder;
        EndianUtilities.WriteBytesLittleEndian(LBAStart, buffer.Slice(8));
        EndianUtilities.WriteBytesLittleEndian(LBALength, buffer.Slice(12));
    }
}