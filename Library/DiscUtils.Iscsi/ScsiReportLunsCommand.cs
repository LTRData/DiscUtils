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

internal class ScsiReportLunsCommand : ScsiCommand
{
    public const int InitialResponseSize = 16;

    private readonly uint _expected;

    public ScsiReportLunsCommand(uint expected)
        : base(0)
    {
        _expected = expected;
    }

    public override int Size => 12;

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        throw new NotImplementedException();
    }

    public override void WriteTo(Span<byte> buffer)
    {
        buffer[0] = 0xA0;
        buffer[1] = 0; // Reserved
        buffer[2] = 0; // Report Type = 0
        buffer[3] = 0; // Reserved
        buffer[4] = 0; // Reserved
        buffer[5] = 0; // Reserved
        EndianUtilities.WriteBytesBigEndian(_expected, buffer.Slice(6));
        buffer[10] = 0; // Reserved
        buffer[11] = 0; // Control
    }
}