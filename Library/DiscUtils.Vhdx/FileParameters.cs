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

using DiscUtils.Streams;
using System;

namespace DiscUtils.Vhdx;

internal sealed class FileParameters : IByteArraySerializable
{
    public const uint DefaultFixedBlockSize = (uint)Sizes.OneMiB;
    public const uint DefaultBlockSize = 32 * (uint)Sizes.OneMiB;
    public const uint DefaultDifferencingBlockSize = 2 * (uint)Sizes.OneMiB;
    public const uint DefaultDynamicBlockSize = 32 * (uint)Sizes.OneMiB;

    public uint BlockSize;
    public FileParametersFlags Flags;

    public int Size => 8;

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        BlockSize = EndianUtilities.ToUInt32LittleEndian(buffer);
        Flags = (FileParametersFlags)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(4));

        return 8;
    }

    public void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian(BlockSize, buffer);
        EndianUtilities.WriteBytesLittleEndian((uint)Flags, buffer.Slice(4));
    }
}