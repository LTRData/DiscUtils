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

internal abstract class PartitionMap : IByteArraySerializable
{
    public byte Type;

    public abstract int Size { get; }

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Type = buffer[0];
        return Parse(buffer);
    }

    void IByteArraySerializable.WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }

    public static PartitionMap CreateFrom(ReadOnlySpan<byte> buffer)
    {
        PartitionMap result = null;

        var type = buffer[0];
        if (type == 1)
        {
            result = new Type1PartitionMap();
        }
        else if (type == 2)
        {
            EntityIdentifier id = EndianUtilities.ToStruct<UdfEntityIdentifier>(buffer.Slice(4));
            result = id.Identifier switch
            {
                "*UDF Virtual Partition" => new VirtualPartitionMap(),
                "*UDF Sparable Partition" => new SparablePartitionMap(),
                "*UDF Metadata Partition" => new MetadataPartitionMap(),
                _ => throw new InvalidDataException($"Unrecognized partition map entity id: {id}"),
            };
        }

        result?.ReadFrom(buffer);

        return result;
    }

    protected abstract int Parse(ReadOnlySpan<byte> buffer);
}