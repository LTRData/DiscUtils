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

namespace DiscUtils.Iscsi;

internal class ScsiWriteCommand : ScsiCommand
{
    private readonly uint _logicalBlockAddress;

    public ScsiWriteCommand(ulong targetLun, uint logicalBlockAddress, ushort numBlocks)
        : base(targetLun)
    {
        _logicalBlockAddress = logicalBlockAddress;
        NumBlocks = numBlocks;
    }

    public ushort NumBlocks { get; }

    public override int Size => 10;

    public override TaskAttributes TaskAttributes => TaskAttributes.Simple;

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        throw new NotImplementedException();
    }

    public override void WriteTo(Span<byte> buffer)
    {
        buffer[0] = 0x2A;
        buffer[1] = 0;
        EndianUtilities.WriteBytesBigEndian(_logicalBlockAddress, buffer.Slice(2));
        buffer[6] = 0;
        EndianUtilities.WriteBytesBigEndian(NumBlocks, buffer.Slice(7));
        buffer[9] = 0;
    }
}