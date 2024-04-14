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

using DiscUtils.Streams;
using System;

namespace DiscUtils.Udf;

internal sealed class SparablePartitionMap : PartitionMap
{
    public uint[] LocationsOfSparingTables;
    public byte NumSparingTables;
    public ushort PacketLength;
    public ushort PartitionNumber;
    public uint SparingTableSize;
    public ushort VolumeSequenceNumber;

    public override int Size => 64;

    protected override int Parse(ReadOnlySpan<byte> buffer)
    {
        VolumeSequenceNumber = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(36));
        PartitionNumber = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(38));
        PacketLength = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(40));
        NumSparingTables = buffer[42];
        SparingTableSize = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(44));
        LocationsOfSparingTables = new uint[NumSparingTables];
        for (var i = 0; i < NumSparingTables; ++i)
        {
            LocationsOfSparingTables[i] = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(48 + 4 * i));
        }

        return 64;
    }
}