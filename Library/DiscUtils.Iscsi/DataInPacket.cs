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

namespace DiscUtils.Iscsi;

internal class DataInPacket : BaseResponse
{
    public bool Acknowledge;
    public uint BufferOffset;

    public uint DataSequenceNumber;
    public BasicHeaderSegment Header;
    public ulong Lun;
    public bool O;
    public byte[] ReadData;
    public uint ResidualCount;

    public ScsiStatus Status;
    public uint TargetTransferTag;
    public bool U;

    public override void Parse(ProtocolDataUnit pdu)
    {
        Parse(pdu.HeaderData, pdu.ContentData);
    }

    public void Parse(ReadOnlySpan<byte> headerData, byte[] bodyData)
    {
        Header = new BasicHeaderSegment();
        Header.ReadFrom(headerData);

        if (Header.OpCode != OpCode.ScsiDataIn)
        {
            throw new InvalidProtocolException($"Invalid opcode in response, expected {OpCode.ScsiDataIn} was {Header.OpCode}");
        }

        UnpackFlags(headerData[1]);
        if (StatusPresent)
        {
            Status = (ScsiStatus)headerData[3];
        }

        Lun = EndianUtilities.ToUInt64BigEndian(headerData[8..]);
        TargetTransferTag = EndianUtilities.ToUInt32BigEndian(headerData[20..]);
        StatusSequenceNumber = EndianUtilities.ToUInt32BigEndian(headerData[24..]);
        ExpectedCommandSequenceNumber = EndianUtilities.ToUInt32BigEndian(headerData[28..]);
        MaxCommandSequenceNumber = EndianUtilities.ToUInt32BigEndian(headerData[32..]);
        DataSequenceNumber = EndianUtilities.ToUInt32BigEndian(headerData[36..]);
        BufferOffset = EndianUtilities.ToUInt32BigEndian(headerData[40..]);
        ResidualCount = EndianUtilities.ToUInt32BigEndian(headerData[44..]);

        ReadData = bodyData;
    }

    private void UnpackFlags(byte value)
    {
        Acknowledge = (value & 0x40) != 0;
        O = (value & 0x04) != 0;
        U = (value & 0x02) != 0;
        StatusPresent = (value & 0x01) != 0;
    }
}