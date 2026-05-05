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
using System.IO;
using DiscUtils.Streams;

namespace DiscUtils.Udf;

internal abstract class TaggedDescriptor<T> : BaseTaggedDescriptor
    where T : BaseTaggedDescriptor, new()
{
    protected TaggedDescriptor(TagIdentifier id)
        : base(id) { }

    public static T FromStream(Stream stream, uint sector, uint sectorSize)
    {
        stream.Position = sector * (long)sectorSize;
        Span<byte> buffer = stackalloc byte[512];
        stream.ReadExactly(buffer);

        var result = new T();
        result.ReadFrom(buffer);
        if (result.Tag.TagIdentifier != result.RequiredTagIdentifier
            || result.Tag.TagLocation != sector)
        {
            throw new InvalidDataException($"Corrupt UDF file system, unable to read {result.RequiredTagIdentifier} tag at sector {sector}");
        }

        return result;
    }

    public abstract override int Parse(ReadOnlySpan<byte> buffer);
}