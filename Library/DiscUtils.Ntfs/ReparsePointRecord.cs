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

namespace DiscUtils.Ntfs;

internal sealed class ReparsePointRecord : IByteArraySerializable, IDiagnosticTraceable
{
    public byte[] Content;
    public uint Tag;

    public int Size => 8 + Content.Length;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Tag = EndianUtilities.ToUInt32LittleEndian(buffer);
        var length = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(4));
        Content = buffer.Slice(8, length).ToArray();
        return 8 + length;
    }

    public void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian(Tag, buffer);
        EndianUtilities.WriteBytesLittleEndian((ushort)Content.Length, buffer.Slice(4));
        EndianUtilities.WriteBytesLittleEndian((ushort)0, buffer.Slice(6));
        Content.AsSpan().CopyTo(buffer.Slice(8));
    }

    public void Dump(TextWriter writer, string linePrefix)
    {
        writer.WriteLine($"{linePrefix}                Tag: {Tag:x}");

        var hex = string.Empty;
        for (var i = 0; i < Math.Min(Content.Length, 32); ++i)
        {
            hex = $"{hex} {Content[i]:X2}";
        }

        writer.WriteLine($"{linePrefix}               Data:{hex}{(Content.Length > 32 ? "..." : string.Empty)}");
    }
}