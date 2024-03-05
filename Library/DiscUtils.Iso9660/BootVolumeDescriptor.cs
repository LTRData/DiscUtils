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

namespace DiscUtils.Iso9660;

internal class BootVolumeDescriptor : BaseVolumeDescriptor
{
    public const string ElToritoSystemIdentifier = "EL TORITO SPECIFICATION";

    public BootVolumeDescriptor(uint catalogSector)
        : base(VolumeDescriptorType.Boot, 1)
    {
        CatalogSector = catalogSector;
    }

    public BootVolumeDescriptor(ReadOnlySpan<byte> src)
        : base(src)
    {
        SystemId = EndianUtilities.BytesToZString(src.Slice(0x7, 0x20));
        CatalogSector = EndianUtilities.ToUInt32LittleEndian(src.Slice(0x47));
    }

    public uint CatalogSector { get; }

    public string SystemId { get; }

    internal override void WriteTo(Span<byte> buffer)
    {
        base.WriteTo(buffer);

        EncodingUtilities
            .GetLatin1Encoding()
            .GetBytes(ElToritoSystemIdentifier, buffer.Slice(7, 0x20));

        EndianUtilities.WriteBytesLittleEndian(CatalogSector, buffer.Slice(0x47));
    }
}