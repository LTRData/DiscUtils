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

namespace DiscUtils.Ntfs;

public class DataRun
{
    public DataRun() {}

    public DataRun(long offset, long length, bool isSparse)
    {
        RunOffset = offset;
        RunLength = length;
        IsSparse = isSparse;
    }

    public bool IsSparse { get; private set; }

    public long RunLength { get; set; }

    public long RunOffset { get; set; }

    internal int Size
    {
        get
        {
            var runLengthSize = VarLongSize(RunLength);
            var runOffsetSize = VarLongSize(RunOffset);
            return 1 + runLengthSize + runOffsetSize;
        }
    }

    public int Read(ReadOnlySpan<byte> buffer)
    {
        var runOffsetSize = (buffer[0] >> 4) & 0x0F;
        var runLengthSize = buffer[0] & 0x0F;

        RunLength = ReadVarLong(buffer.Slice(1, runLengthSize));
        RunOffset = ReadVarLong(buffer.Slice(1 + runLengthSize, runOffsetSize));
        IsSparse = runOffsetSize == 0;

        return 1 + runLengthSize + runOffsetSize;
    }

    public override string ToString() => $"{RunOffset:+##;-##;0}[+{RunLength}]";

    internal int Write(Span<byte> buffer)
    {
        var runLengthSize = WriteVarLong(buffer.Slice(1), RunLength);
        var runOffsetSize = IsSparse ? 0 : WriteVarLong(buffer.Slice(1 + runLengthSize), RunOffset);

        buffer[0] = (byte)((runLengthSize & 0x0F) | ((runOffsetSize << 4) & 0xF0));

        return 1 + runLengthSize + runOffsetSize;
    }

    private static long ReadVarLong(ReadOnlySpan<byte> buffer)
    {
        ulong val = 0;
        var signExtend = false;

        for (var i = 0; i < buffer.Length; ++i)
        {
            var b = buffer[i];
            val |= ((ulong)b << (i * 8));
            signExtend = (b & 0x80) != 0;
        }

        if (signExtend)
        {
            for (var i = buffer.Length; i < 8; ++i)
            {
                val |= ((ulong)0xFF << (i * 8));
            }
        }

        return (long)val;
    }

    private static int WriteVarLong(Span<byte> buffer, long val)
    {
        var isPositive = val >= 0;

        var pos = 0;
        do
        {
            buffer[pos] = (byte)(val & 0xFF);
            val >>= 8;
            pos++;
        } while (val is not 0 and not (-1));

        // Avoid appearing to have a negative number that is actually positive,
        // record an extra empty byte if needed.
        if (isPositive && (buffer[pos - 1] & 0x80) != 0)
        {
            buffer[pos] = 0;
            pos++;
        }
        else if (!isPositive && (buffer[pos - 1] & 0x80) != 0x80)
        {
            buffer[pos] = 0xFF;
            pos++;
        }

        return pos;
    }

    private static int VarLongSize(long val)
    {
        var isPositive = val >= 0;
        var len = 0;
        bool lastByteHighBitSet;
        do
        {
            lastByteHighBitSet = (val & 0x80) != 0;
            val >>= 8;
            len++;
        } while (val is not 0 and not (-1));

        if ((isPositive && lastByteHighBitSet) || (!isPositive && !lastByteHighBitSet))
        {
            len++;
        }

        return len;
    }
}