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

internal class BasicHeaderSegment : IByteArraySerializable
{
    public int DataSegmentLength; // In bytes!
    public bool FinalPdu;
    public bool Immediate;
    public uint InitiatorTaskTag;
    public OpCode OpCode;
    public byte TotalAhsLength; // In 4-byte words!

    #region IByteArraySerializable Members

    public int Size => 48;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Immediate = (buffer[0] & 0x40) != 0;
        OpCode = (OpCode)(buffer[0] & 0x3F);
        FinalPdu = (buffer[1] & 0x80) != 0;
        TotalAhsLength = buffer[4];
        DataSegmentLength = EndianUtilities.ToInt32BigEndian(buffer.Slice(4)) & 0x00FFFFFF;
        InitiatorTaskTag = EndianUtilities.ToUInt32BigEndian(buffer.Slice(16));
        return 48;
    }

    public void WriteTo(Span<byte> buffer)
    {
        buffer[0] = (byte)((Immediate ? 0x40 : 0x00) | ((int)OpCode & 0x3F));
        buffer[1] |= (byte)(FinalPdu ? 0x80 : 0x00);
        buffer[4] = TotalAhsLength;
        buffer[5] = (byte)((DataSegmentLength >> 16) & 0xFF);
        buffer[6] = (byte)((DataSegmentLength >> 8) & 0xFF);
        buffer[7] = (byte)(DataSegmentLength & 0xFF);
        EndianUtilities.WriteBytesBigEndian(InitiatorTaskTag, buffer.Slice(16));
    }

    #endregion
}