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

namespace DiscUtils.HfsPlus;

internal sealed class CatalogFileInfo : CommonCatalogFileInfo
{
    public ForkData DataFork;
    public FileInfo FileInfo;
    public ushort Flags;
    public ForkData ResourceFork;

    public override int Size => throw new NotImplementedException();

    public override int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        base.ReadFrom(buffer);

        Flags = EndianUtilities.ToUInt16BigEndian(buffer.Slice(2));
        FileInfo = EndianUtilities.ToStruct<FileInfo>(buffer.Slice(48));

        DataFork = EndianUtilities.ToStruct<ForkData>(buffer.Slice(88));
        ResourceFork = EndianUtilities.ToStruct<ForkData>(buffer.Slice(168));

        return 0;
    }

    public override void WriteTo(Span<byte> buffer)
    {
        throw new NotImplementedException();
    }
}