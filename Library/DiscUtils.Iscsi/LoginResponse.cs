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

internal class LoginResponse : BaseResponse
{
    public byte ActiveVersion;
    public bool Continue;
    public LoginStages CurrentStage;

    public byte MaxVersion;
    public LoginStages NextStage;
    public byte StatusClass;
    public LoginStatusCode StatusCode;

    public ushort TargetSessionId;
    public uint TargetTransferTag; // TTT from offset 24
    public byte[] TextData;
    public bool Transit;

    public override void Parse(ProtocolDataUnit pdu)
    {
        Parse(pdu.HeaderData, pdu.ContentData);
    }

    public void Parse(ReadOnlySpan<byte> headerData, byte[] bodyData)
    {
        var _headerSegment = new BasicHeaderSegment();
        _headerSegment.ReadFrom(headerData);

        if (_headerSegment.OpCode != OpCode.LoginResponse)
        {
            throw new InvalidProtocolException($"Invalid opcode in response, expected {OpCode.LoginResponse} was {_headerSegment.OpCode}");
        }

        UnpackState(headerData[1]);
        MaxVersion = headerData[2];
        ActiveVersion = headerData[3];
        TargetSessionId = EndianUtilities.ToUInt16BigEndian(headerData[14..]);
        StatusPresent = true;

        // RFC 3720: offset 24 = Target Transfer Tag (TTT), offset 28 = StatSN
        TargetTransferTag = EndianUtilities.ToUInt32BigEndian(headerData[24..]);
        StatusSequenceNumber = EndianUtilities.ToUInt32BigEndian(headerData[28..]);

        ExpectedCommandSequenceNumber = EndianUtilities.ToUInt32BigEndian(headerData[32..]);
        MaxCommandSequenceNumber = EndianUtilities.ToUInt32BigEndian(headerData[36..]);
        StatusClass = headerData[36];
        StatusCode = (LoginStatusCode)EndianUtilities.ToUInt16BigEndian(headerData[36..]);

        TextData = bodyData;
    }

    private void UnpackState(byte value)
    {
        Transit = (value & 0x80) != 0;
        Continue = (value & 0x40) != 0;
        CurrentStage = (LoginStages)((value >> 2) & 0x3);
        NextStage = (LoginStages)(value & 0x3);
    }
}