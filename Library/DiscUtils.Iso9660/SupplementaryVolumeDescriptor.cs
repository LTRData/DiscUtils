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
using System.Text;
using DiscUtils.Streams;

namespace DiscUtils.Iso9660;

internal class SupplementaryVolumeDescriptor : CommonVolumeDescriptor
{
    public SupplementaryVolumeDescriptor(ReadOnlySpan<byte> src)
        : base(src, IsoUtilities.EncodingFromBytes(src.Slice(88))) {}

    public SupplementaryVolumeDescriptor(
        uint volumeSpaceSize,
        uint pathTableSize,
        uint typeLPathTableLocation,
        uint typeMPathTableLocation,
        uint rootDirExtentLocation,
        uint rootDirDataLength,
        DateTime buildTime,
        Encoding enc)
        : base(
            VolumeDescriptorType.Supplementary, 1, volumeSpaceSize, pathTableSize, typeLPathTableLocation,
            typeMPathTableLocation, rootDirExtentLocation, rootDirDataLength, buildTime, enc) {}

    internal override void WriteTo(Span<byte> buffer)
    {
        base.WriteTo(buffer);
        IsoUtilities.WriteA1Chars(buffer.Slice(8, 32), SystemIdentifier.AsSpan(), CharacterEncoding);
        IsoUtilities.WriteString(buffer.Slice(40, 32), pad: true, VolumeIdentifier.AsSpan(), CharacterEncoding, true);
        IsoUtilities.ToBothFromUInt32(buffer.Slice(80), VolumeSpaceSize);
        IsoUtilities.EncodingToBytes(CharacterEncoding, buffer.Slice(88));
        IsoUtilities.ToBothFromUInt16(buffer.Slice(120), VolumeSetSize);
        IsoUtilities.ToBothFromUInt16(buffer.Slice(124), VolumeSequenceNumber);
        IsoUtilities.ToBothFromUInt16(buffer.Slice(128), LogicalBlockSize);
        IsoUtilities.ToBothFromUInt32(buffer.Slice(132), PathTableSize);
        IsoUtilities.ToBytesFromUInt32(buffer.Slice(140), TypeLPathTableLocation);
        IsoUtilities.ToBytesFromUInt32(buffer.Slice(144), OptionalTypeLPathTableLocation);
        EndianUtilities.WriteBytesBigEndian(TypeMPathTableLocation, buffer.Slice(148));
        EndianUtilities.WriteBytesBigEndian(OptionalTypeMPathTableLocation, buffer.Slice(152));
        RootDirectory.WriteTo(buffer.Slice(156), CharacterEncoding);
        IsoUtilities.WriteD1Chars(buffer.Slice(190, 129), VolumeSetIdentifier.AsSpan(), CharacterEncoding);
        IsoUtilities.WriteA1Chars(buffer.Slice(318, 129), PublisherIdentifier.AsSpan(), CharacterEncoding);
        IsoUtilities.WriteA1Chars(buffer.Slice(446, 129), DataPreparerIdentifier.AsSpan(), CharacterEncoding);
        IsoUtilities.WriteA1Chars(buffer.Slice(574, 129), ApplicationIdentifier.AsSpan(), CharacterEncoding);
        IsoUtilities.WriteD1Chars(buffer.Slice(702, 37), CopyrightFileIdentifier.AsSpan(), CharacterEncoding); // ToDo
        IsoUtilities.WriteD1Chars(buffer.Slice(739, 37), AbstractFileIdentifier.AsSpan(), CharacterEncoding); // ToDo
        IsoUtilities.WriteD1Chars(buffer.Slice(776, 37), BibliographicFileIdentifier.AsSpan(), CharacterEncoding);

        // FIXME!!
        IsoUtilities.ToVolumeDescriptorTimeFromUTC(buffer.Slice(813), CreationDateAndTime);
        IsoUtilities.ToVolumeDescriptorTimeFromUTC(buffer.Slice(830), ModificationDateAndTime);
        IsoUtilities.ToVolumeDescriptorTimeFromUTC(buffer.Slice(847), ExpirationDateAndTime);
        IsoUtilities.ToVolumeDescriptorTimeFromUTC(buffer.Slice(864), EffectiveDateAndTime);
        buffer[881] = FileStructureVersion;
    }
}