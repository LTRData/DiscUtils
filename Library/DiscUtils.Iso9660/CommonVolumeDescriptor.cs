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

internal class CommonVolumeDescriptor : BaseVolumeDescriptor
{
    public string AbstractFileIdentifier;
    public string ApplicationIdentifier;
    public string BibliographicFileIdentifier;
    public Encoding CharacterEncoding;
    public string CopyrightFileIdentifier;
    public DateTime CreationDateAndTime;
    public string DataPreparerIdentifier;
    public DateTime EffectiveDateAndTime;
    public DateTime ExpirationDateAndTime;
    public byte FileStructureVersion;
    public ushort LogicalBlockSize;
    public DateTime ModificationDateAndTime;
    public uint OptionalTypeLPathTableLocation;
    public uint OptionalTypeMPathTableLocation;
    public uint PathTableSize;
    public string PublisherIdentifier;
    public DirectoryRecord RootDirectory;

    public string SystemIdentifier;
    public uint TypeLPathTableLocation;
    public uint TypeMPathTableLocation;
    public string VolumeIdentifier;
    public ushort VolumeSequenceNumber;
    public string VolumeSetIdentifier;
    public ushort VolumeSetSize;
    public uint VolumeSpaceSize;

    public CommonVolumeDescriptor(ReadOnlySpan<byte> src, Encoding enc)
        : base(src)
    {
        CharacterEncoding = enc;

        SystemIdentifier = IsoUtilities.ReadChars(src.Slice(8, 32), CharacterEncoding);
        VolumeIdentifier = IsoUtilities.ReadChars(src.Slice(40, 32), CharacterEncoding);
        VolumeSpaceSize = IsoUtilities.ToUInt32FromBoth(src.Slice(80));
        VolumeSetSize = IsoUtilities.ToUInt16FromBoth(src.Slice(120));
        VolumeSequenceNumber = IsoUtilities.ToUInt16FromBoth(src.Slice(124));
        LogicalBlockSize = IsoUtilities.ToUInt16FromBoth(src.Slice(128));
        PathTableSize = IsoUtilities.ToUInt32FromBoth(src.Slice(132));
        TypeLPathTableLocation = EndianUtilities.ToUInt32LittleEndian(src.Slice(140));
        OptionalTypeLPathTableLocation = EndianUtilities.ToUInt32LittleEndian(src.Slice(144));
        TypeMPathTableLocation = EndianUtilities.ToUInt32BigEndian(src.Slice(148));
        OptionalTypeMPathTableLocation = EndianUtilities.ToUInt32BigEndian(src.Slice(152));
        DirectoryRecord.ReadFrom(src.Slice(156), CharacterEncoding, out RootDirectory);
        VolumeSetIdentifier = IsoUtilities.ReadChars(src.Slice(190, 318 - 190), CharacterEncoding);
        PublisherIdentifier = IsoUtilities.ReadChars(src.Slice(318, 446 - 318), CharacterEncoding);
        DataPreparerIdentifier = IsoUtilities.ReadChars(src.Slice(446, 574 - 446), CharacterEncoding);
        ApplicationIdentifier = IsoUtilities.ReadChars(src.Slice(574, 702 - 574), CharacterEncoding);
        CopyrightFileIdentifier = IsoUtilities.ReadChars(src.Slice(702, 739 - 702), CharacterEncoding);
        AbstractFileIdentifier = IsoUtilities.ReadChars(src.Slice(739, 776 - 739), CharacterEncoding);
        BibliographicFileIdentifier = IsoUtilities.ReadChars(src.Slice(776, 813 - 776), CharacterEncoding);
        CreationDateAndTime = IsoUtilities.ToDateTimeFromVolumeDescriptorTime(src.Slice(813));
        ModificationDateAndTime = IsoUtilities.ToDateTimeFromVolumeDescriptorTime(src.Slice(830));
        ExpirationDateAndTime = IsoUtilities.ToDateTimeFromVolumeDescriptorTime(src.Slice(847));
        EffectiveDateAndTime = IsoUtilities.ToDateTimeFromVolumeDescriptorTime(src.Slice(864));
        FileStructureVersion = src[881];
    }

    public CommonVolumeDescriptor(
        VolumeDescriptorType type,
        byte version,
        uint volumeSpaceSize,
        uint pathTableSize,
        uint typeLPathTableLocation,
        uint typeMPathTableLocation,
        uint rootDirExtentLocation,
        uint rootDirDataLength,
        DateTime buildTime,
        Encoding enc)
        : base(type, version)
    {
        CharacterEncoding = enc;

        SystemIdentifier = string.Empty;
        VolumeIdentifier = string.Empty;
        VolumeSpaceSize = volumeSpaceSize;
        VolumeSetSize = 1;
        VolumeSequenceNumber = 1;
        LogicalBlockSize = IsoUtilities.SectorSize;
        PathTableSize = pathTableSize;
        TypeLPathTableLocation = typeLPathTableLocation;
        ////OptionalTypeLPathTableLocation = 0;
        TypeMPathTableLocation = typeMPathTableLocation;
        ////OptionalTypeMPathTableLocation = 0;
        RootDirectory = new DirectoryRecord
        {
            ExtendedAttributeRecordLength = 0,
            LocationOfExtent = rootDirExtentLocation,
            DataLength = rootDirDataLength,
            RecordingDateAndTime = buildTime,
            Flags = FileFlags.Directory,
            FileUnitSize = 0,
            InterleaveGapSize = 0,
            VolumeSequenceNumber = 1,
            FileIdentifier = "\0"
        };
        VolumeSetIdentifier = string.Empty;
        PublisherIdentifier = string.Empty;
        DataPreparerIdentifier = string.Empty;
        ApplicationIdentifier = string.Empty;
        CopyrightFileIdentifier = string.Empty;
        AbstractFileIdentifier = string.Empty;
        BibliographicFileIdentifier = string.Empty;
        CreationDateAndTime = buildTime;
        ModificationDateAndTime = buildTime;
        ExpirationDateAndTime = DateTime.MinValue;
        EffectiveDateAndTime = buildTime;
        FileStructureVersion = 1; // V1
    }
}