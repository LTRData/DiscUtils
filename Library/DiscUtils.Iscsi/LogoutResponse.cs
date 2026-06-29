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

internal class LogoutResponse : BaseResponse
{
    public LogoutResponseCode Response;
    public ushort Time2Retain;
    public ushort Time2Wait;

    public override void Parse(ProtocolDataUnit pdu)
    {
        Parse(pdu.HeaderData);
    }

    public void Parse(ReadOnlySpan<byte> headerData)
    {
        var _headerSegment = new BasicHeaderSegment();
        _headerSegment.ReadFrom(headerData);

        if (_headerSegment.OpCode != OpCode.LogoutResponse)
        {
            throw new InvalidProtocolException($"Invalid opcode in response, expected {OpCode.LogoutResponse} was {_headerSegment.OpCode}");
        }

        Response = (LogoutResponseCode)headerData[2];
        StatusSequenceNumber = EndianUtilities.ToUInt32BigEndian(headerData[24..]);
        ExpectedCommandSequenceNumber = EndianUtilities.ToUInt32BigEndian(headerData[28..]);
        MaxCommandSequenceNumber = EndianUtilities.ToUInt32BigEndian(headerData[32..]);
        Time2Wait = EndianUtilities.ToUInt16BigEndian(headerData[40..]);
        Time2Retain = EndianUtilities.ToUInt16BigEndian(headerData[42..]);
    }
}