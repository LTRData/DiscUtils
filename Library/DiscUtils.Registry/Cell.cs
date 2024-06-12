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
using System.Linq;
using System.Text;
using DiscUtils.Streams;

namespace DiscUtils.Registry;

/// <summary>
/// Base class for the different kinds of cell present in a hive.
/// </summary>
internal abstract class Cell : IByteArraySerializable
{
    public Cell(int index)
    {
        Index = index;
    }

    public int Index { get; set; }

    public abstract int Size { get; }

    public abstract int ReadFrom(ReadOnlySpan<byte> buffer);

    public abstract void WriteTo(Span<byte> buffer);

    internal static Cell Parse(RegistryHive hive, int index, ReadOnlySpan<byte> buffer)
    {
        var type = buffer.Slice(0, 2);

        Cell result;

        if (type.SequenceEqual("nk"u8))
        {
            result = new KeyNodeCell(index);
        }
        else if (type.SequenceEqual("sk"u8))
        {
            result = new SecurityCell(index);
        }
        else if (type.SequenceEqual("vk"u8))
        {
            result = new ValueCell(index);
        }
        else if (type.SequenceEqual("lh"u8) || type.SequenceEqual("lf"u8))
        {
            result = new SubKeyHashedListCell(hive, index);
        }
        else if (type.SequenceEqual("li"u8) || type.SequenceEqual("ri"u8))
        {
            result = new SubKeyIndirectListCell(hive, index);
        }
        else
        {
            throw new RegistryCorruptException($"Unknown cell type '{Encoding.ASCII.GetString(type)}'");
        }

        result.ReadFrom(buffer);

        return result;
    }
}