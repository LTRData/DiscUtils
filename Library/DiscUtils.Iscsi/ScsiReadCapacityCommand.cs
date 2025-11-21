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

internal class ScsiReadCapacity10Command : ScsiCommand
{
    public const int ResponseDataLength = 8;

    public ScsiReadCapacity10Command(ulong targetLun)
        : base(targetLun) { }

    public override int Size => 10;

    public override TaskAttributes TaskAttributes => TaskAttributes.Simple;

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        throw new NotImplementedException();
    }

    public override void WriteTo(Span<byte> buffer)
    {
        buffer.Slice(0, 10).Clear();
        buffer[0] = (byte)ScsiOpCode.ReadCapacity10; // OpCode
    }
}

internal class ScsiReadCapacity16Command : ScsiCommand
{
    public const int ResponseDataLength = 32;

    public ScsiReadCapacity16Command(ulong targetLun)
        : base(targetLun) { }

    public override int Size => 16;

    public override TaskAttributes TaskAttributes => TaskAttributes.Simple;

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        throw new NotImplementedException();
    }

    public override void WriteTo(Span<byte> buffer)
    {
        buffer.Slice(0, 16).Clear();
        buffer[0] = (byte)ScsiOpCode.ServiceActionIn; // OpCode
        buffer[1] = (byte)ScsiOpServiceAction.ReadCapacity16; // OpCode
        EndianUtilities.WriteBytesBigEndian(32, buffer.Slice(10));
    }
}

