//
// Copyright (c) 2017, Bianco Veigel
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
using System.Text;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Swap;

public class SwapHeader : IByteArraySerializable
{
    public static readonly string Magic1 = "SWAP-SPACE";
    public static readonly string Magic2 = "SWAPSPACE2";
    public static readonly int PageShift = 12;
    public static readonly int PageSize = 1 << PageShift;

    public uint Version { get; private set; }

    public uint LastPage { get; private set; }

    public uint BadPages { get; private set; }

    public Guid Uuid { get; private set; }

    public string Volume { get; private set; }

    public string Magic { get; private set; }

    public int Size
    {
        get { return PageSize; }
    }

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Magic = EncodingUtilities
            .GetLatin1Encoding()
            .GetString(buffer.Slice(PageSize - 10, 10));

        if (Magic != Magic1 && Magic != Magic2)
        {
            return Size;
        }

        Version = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x400));
        LastPage = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x404));
        BadPages = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0x408));
        Uuid = EndianUtilities.ToGuidLittleEndian(buffer.Slice(0x40c));

        var volume = buffer.Slice(0x41c, 16);
        var nullIndex = volume.IndexOf((byte)0);

        if (nullIndex > 0)
        {
            Volume = Encoding.UTF8.GetString(volume.Slice(0, nullIndex));
        }

        return Size;
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}
