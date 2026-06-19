//
// Copyright (c) 2008-2012, Kenneth Bell
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

namespace DiscUtils;

/// <summary>
/// Bitmap where each bit can be marked as unused or in use, with methods
/// for finding contiguous in-use or unused bits.
/// </summary>
public sealed class AllocationBitmap
{
    private readonly byte[] _data;
    private readonly long _length;
    private readonly int _offset;

    /// <summary>
    /// Gets underlying bitmap for this instance.
    /// </summary>
    public ArraySegment<byte> Bitmap => new(_data, _offset, (int)_length);

    /// <summary>
    /// Create an instance over an existing bitmap stored in an array.
    /// </summary>
    /// <param name="data">Existing array</param>
    /// <param name="offset">Byte offset into array where bitmap starts</param>
    /// <param name="length">Number of bytes to use in the array</param>
    public AllocationBitmap(byte[] data, int offset, int length)
    {
        // Just trigger bound checks
        data.AsSpan(offset, length);

        _data = data;
        _offset = offset;
        _length = length;
    }

    /// <summary>
    /// Find number of contiguous bits
    /// </summary>
    /// <param name="first">Bit number where search should start</param>
    /// <param name="state">Returns bit state of bits in found sequence</param>
    /// <returns>Number of contiguous bits found</returns>
    public int ContiguousBits(int first, out bool state)
    {
        var matched = 0;
        var bitPos = first & 0x7;
        var bytePos = first >> 3;

        state = (_data[_offset + bytePos] & (1 << bitPos)) != 0;
        var matchByte = state ? (byte)0xFF : (byte)0;

        while (bytePos < _length)
        {
            if (_data[_offset + bytePos] == matchByte)
            {
                matched += 8 - bitPos;
                bytePos++;
                bitPos = 0;
            }
            else if ((_data[_offset + bytePos] & (1 << bitPos)) != 0 == state)
            {
                matched++;
                bitPos++;
                if (bitPos == 8)
                {
                    bitPos = 0;
                    bytePos++;
                }
            }
            else
            {
                break;
            }
        }

        return matched;
    }

    /// <summary>
    /// Find number of contiguous bits
    /// </summary>
    /// <param name="first">Bit number where search should start</param>
    /// <param name="state">Returns bit state of bits in found sequence</param>
    /// <returns>Number of contiguous bits found</returns>
    public long ContiguousBits(long first, out bool state)
    {
        var matched = 0L;
        var bitPos = (int)(first & 0x7);
        var bytePos = (int)(first >> 3);

        state = (_data[_offset + bytePos] & (1 << bitPos)) != 0;
        var matchByte = state ? (byte)0xFF : (byte)0;

        while (bytePos < _length)
        {
            if (_data[_offset + bytePos] == matchByte)
            {
                matched += 8 - bitPos;
                bytePos++;
                bitPos = 0;
            }
            else if ((_data[_offset + bytePos] & (1 << bitPos)) != 0 == state)
            {
                matched++;
                bitPos++;
                if (bitPos == 8)
                {
                    bitPos = 0;
                    bytePos++;
                }
            }
            else
            {
                break;
            }
        }

        return matched;
    }

    /// <summary>
    /// Marks bits as allocated
    /// </summary>
    /// <param name="first">Bit number of first bit to mark as allocated</param>
    /// <param name="count">Number of bits to mark, including the first one</param>
    /// <returns>True if any bit states were changed</returns>
    internal bool MarkBitsAllocated(long first, long count)
    {
        var changed = false;
        var marked = 0;
        var bitPos = (int)(first & 0x7);
        var bytePos = first / 8;

        while (marked < count)
        {
            if (bitPos == 0 && count - marked >= 8)
            {
                if (_data[_offset + bytePos] != 0xFF)
                {
                    _data[_offset + bytePos] = 0xFF;
                    changed = true;
                }

                marked += 8;
                bytePos++;
            }
            else
            {
                if ((_data[_offset + bytePos] & (1 << bitPos)) == 0)
                {
                    _data[_offset + bytePos] |= (byte)(1 << bitPos);
                    changed = true;
                }

                marked++;
                bitPos++;
                if (bitPos == 8)
                {
                    bitPos = 0;
                    bytePos++;
                }
            }
        }

        return changed;
    }
}