//
// Copyright (c) 2008-2024, Kenneth Bell, Olof Lagerkvist
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

namespace DiscUtils.SquashFs;

internal class SuperBlock : IByteArraySerializable
{
    public enum CompressionType : ushort
    {
        Unknown,
        ZLib,
        LZMA,
        Lzo,
        Xz,
        Lz4,
        ZStd
    }

    [Flags]
    public enum SuperBlockFlags : ushort
    {
        UncompressedInodes = 0x0001,
        UncompressedBlocks = 0x0002,
        UncompressedFragments = 0x0008,
        FragmentsNotUsed = 0x0010,
        FragmentsAlwaysGenerated = 0x0020,
        DeduplicatedData = 0x0040,
        NFSExportTableExists = 0x0080,
        UncompressedXAttrs = 0x0100,
        NoXAttrs = 0x0200,
        CompressorOptionsPresent = 0x0400,
        UncompressedIdTable = 0x0800
    }

    public const uint SquashFsMagic = 0x73717368;
    public uint BlockSize;
    public ushort BlockSizeLog2;
    public long BytesUsed;
    public CompressionType Compression;
    public DateTime CreationTime;
    public long DirectoryTableStart;
    public long ExtendedAttrsTableStart;
    public SuperBlockFlags Flags;
    public uint FragmentsCount;
    public long FragmentTableStart;
    public uint InodesCount;
    public long InodeTableStart;
    public long LookupTableStart;

    public uint Magic;
    public ushort MajorVersion;
    public ushort MinorVersion;
    public MetadataRef RootInode;
    public ushort UidGidCount;
    public long UidGidTableStart;

    public int Size
    {
        get { return 96; }
    }

    public int ReadFrom(ReadOnlySpan<byte> buffer)
    {
        Magic = EndianUtilities.ToUInt32LittleEndian(buffer);
        if (Magic != SquashFsMagic) return Size;

        InodesCount = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(4));
        CreationTime = DateTimeOffset.FromUnixTimeSeconds(EndianUtilities.ToUInt32LittleEndian(buffer.Slice(8))).DateTime;
        BlockSize = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(12));
        FragmentsCount = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(16));
        Compression = (CompressionType)EndianUtilities.ToUInt16LittleEndian(buffer.Slice(20));
        BlockSizeLog2 = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(22));
        Flags = (SuperBlockFlags)EndianUtilities.ToUInt16LittleEndian(buffer.Slice(24));
        UidGidCount = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(26));
        MajorVersion = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(28));
        MinorVersion = EndianUtilities.ToUInt16LittleEndian(buffer.Slice(30));
        RootInode = new MetadataRef(EndianUtilities.ToInt64LittleEndian(buffer.Slice(32)));
        BytesUsed = EndianUtilities.ToInt64LittleEndian(buffer.Slice(40));
        UidGidTableStart = EndianUtilities.ToInt64LittleEndian(buffer.Slice(48));
        ExtendedAttrsTableStart = EndianUtilities.ToInt64LittleEndian(buffer.Slice(56));
        InodeTableStart = EndianUtilities.ToInt64LittleEndian(buffer.Slice(64));
        DirectoryTableStart = EndianUtilities.ToInt64LittleEndian(buffer.Slice(72));
        FragmentTableStart = EndianUtilities.ToInt64LittleEndian(buffer.Slice(80));
        LookupTableStart = EndianUtilities.ToInt64LittleEndian(buffer.Slice(88));

        return Size;
    }

    public void WriteTo(Span<byte> buffer)
    {
        EndianUtilities.WriteBytesLittleEndian(Magic, buffer);
        EndianUtilities.WriteBytesLittleEndian(InodesCount, buffer.Slice(4));
        EndianUtilities.WriteBytesLittleEndian(Convert.ToUInt32(new DateTimeOffset(CreationTime).ToUnixTimeSeconds()), buffer.Slice(8));
        EndianUtilities.WriteBytesLittleEndian(BlockSize, buffer.Slice(12));
        EndianUtilities.WriteBytesLittleEndian(FragmentsCount, buffer.Slice(16));
        EndianUtilities.WriteBytesLittleEndian((ushort)Compression, buffer.Slice(20));
        EndianUtilities.WriteBytesLittleEndian(BlockSizeLog2, buffer.Slice(22));
        EndianUtilities.WriteBytesLittleEndian((ushort)Flags, buffer.Slice(24));
        EndianUtilities.WriteBytesLittleEndian(UidGidCount, buffer.Slice(26));
        EndianUtilities.WriteBytesLittleEndian(MajorVersion, buffer.Slice(28));
        EndianUtilities.WriteBytesLittleEndian(MinorVersion, buffer.Slice(30));
        EndianUtilities.WriteBytesLittleEndian(RootInode.Value, buffer.Slice(32));
        EndianUtilities.WriteBytesLittleEndian(BytesUsed, buffer.Slice(40));
        EndianUtilities.WriteBytesLittleEndian(UidGidTableStart, buffer.Slice(48));
        EndianUtilities.WriteBytesLittleEndian(ExtendedAttrsTableStart, buffer.Slice(56));
        EndianUtilities.WriteBytesLittleEndian(InodeTableStart, buffer.Slice(64));
        EndianUtilities.WriteBytesLittleEndian(DirectoryTableStart, buffer.Slice(72));
        EndianUtilities.WriteBytesLittleEndian(FragmentTableStart, buffer.Slice(80));
        EndianUtilities.WriteBytesLittleEndian(LookupTableStart, buffer.Slice(88));
    }
}