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
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.Vdi;

internal class HeaderRecord
{
    private FileVersion _fileVersion;
    public int BlockCount;
    public int BlockExtraSize;
    public int BlocksAllocated;
    public int BlockSize;
    public uint BlocksOffset;
    public string Comment;
    public uint DataOffset;
    public long DiskSize;
    public ImageFlags Flags;
    public uint HeaderSize;
    public ImageType ImageType;
    public GeometryRecord LChsGeometry;
    public GeometryRecord LegacyGeometry;
    public Guid ModificationId;
    public Guid ParentId;
    public Guid ParentModificationId;
    public Guid UniqueId;

    public static HeaderRecord Initialized(ImageType type, ImageFlags flags, long size, int blockSize,
                                           int blockExtra)
    {
        var result = new HeaderRecord
        {
            _fileVersion = new FileVersion(0x00010001),
            HeaderSize = 400,
            ImageType = type,
            Flags = flags,
            Comment = "Created by .NET DiscUtils",
            LegacyGeometry = new GeometryRecord(),
            DiskSize = size,
            BlockSize = blockSize,
            BlockExtraSize = blockExtra,
            BlockCount = (int)((size + blockSize - 1) / blockSize),
            BlocksAllocated = 0
        };

        result.BlocksOffset = (PreHeaderRecord.Size + result.HeaderSize + 511) / 512 * 512;
        result.DataOffset = (uint)((result.BlocksOffset + result.BlockCount * 4 + 511) / 512 * 512);

        result.UniqueId = Guid.NewGuid();
        result.ModificationId = Guid.NewGuid();

        result.LChsGeometry = new GeometryRecord();

        return result;
    }

    public void Read(FileVersion version, Stream s)
    {
        int headerSize;

        _fileVersion = version;

        // Determine header size...
        if (version.Major == 0)
        {
            headerSize = 348;
        }
        else
        {
            var savedPos = s.Position;
            Span<byte> headerSizeBytes = stackalloc byte[4];
            s.ReadExactly(headerSizeBytes);
            headerSize = EndianUtilities.ToInt32LittleEndian(headerSizeBytes);
            s.Position = savedPos;
        }

        var buffer = s.ReadExactly(headerSize);
        Read(version, buffer);
    }

    public int Read(FileVersion version, ReadOnlySpan<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        if (version.Major == 0)
        {
            ImageType = (ImageType)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0));
            Flags = (ImageFlags)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(4));
            Comment = latin1Encoding.GetString(buffer.Slice(8, 256)).TrimEnd('\0');
            LegacyGeometry = new GeometryRecord();
            LegacyGeometry.Read(buffer.Slice(264));
            DiskSize = EndianUtilities.ToInt64LittleEndian(buffer.Slice(280));
            BlockSize = EndianUtilities.ToInt32LittleEndian(buffer.Slice(288));
            BlockCount = EndianUtilities.ToInt32LittleEndian(buffer.Slice(292));
            BlocksAllocated = EndianUtilities.ToInt32LittleEndian(buffer.Slice(296));
            UniqueId = EndianUtilities.ToGuidLittleEndian(buffer.Slice(300));
            ModificationId = EndianUtilities.ToGuidLittleEndian(buffer.Slice(316));
            ParentId = EndianUtilities.ToGuidLittleEndian(buffer.Slice(332));
            HeaderSize = 348;
            BlocksOffset = HeaderSize + PreHeaderRecord.Size;
            DataOffset = (uint)(BlocksOffset + BlockCount * 4);
            BlockExtraSize = 0;
            ParentModificationId = Guid.Empty;
        }
        else if (version.Major == 1 && version.Minor == 1)
        {
            HeaderSize = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(0));
            ImageType = (ImageType)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(4));
            Flags = (ImageFlags)EndianUtilities.ToUInt32LittleEndian(buffer.Slice(8));
            Comment = latin1Encoding.GetString(buffer.Slice(12, 256)).TrimEnd('\0');
            BlocksOffset = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(268));
            DataOffset = EndianUtilities.ToUInt32LittleEndian(buffer.Slice(272));
            LegacyGeometry = new GeometryRecord();
            LegacyGeometry.Read(buffer.Slice(276));
            DiskSize = EndianUtilities.ToInt64LittleEndian(buffer.Slice(296));
            BlockSize = EndianUtilities.ToInt32LittleEndian(buffer.Slice(304));
            BlockExtraSize = EndianUtilities.ToInt32LittleEndian(buffer.Slice(308));
            BlockCount = EndianUtilities.ToInt32LittleEndian(buffer.Slice(312));
            BlocksAllocated = EndianUtilities.ToInt32LittleEndian(buffer.Slice(316));
            UniqueId = EndianUtilities.ToGuidLittleEndian(buffer.Slice(320));
            ModificationId = EndianUtilities.ToGuidLittleEndian(buffer.Slice(336));
            ParentId = EndianUtilities.ToGuidLittleEndian(buffer.Slice(352));
            ParentModificationId = EndianUtilities.ToGuidLittleEndian(buffer.Slice(368));

            if (HeaderSize > 384)
            {
                LChsGeometry = new GeometryRecord();
                LChsGeometry.Read(buffer.Slice(384));
            }
        }
        else
        {
            throw new IOException($"Unrecognized file version: {version}");
        }

        return (int)HeaderSize;
    }

    public void Write(Stream s)
    {
        Span<byte> buffer = stackalloc byte[(int)HeaderSize];
        Write(buffer);
        s.Write(buffer.Slice(0, (int)HeaderSize));
    }

    public async ValueTask WriteAsync(Stream s, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent((int)HeaderSize);
        try
        {
            Write(buffer);
            await s.WriteAsync(buffer.AsMemory(0, (int)HeaderSize), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public int Write(Span<byte> buffer)
    {
        var latin1Encoding = EncodingUtilities.GetLatin1Encoding();

        if (_fileVersion.Major == 0)
        {
            EndianUtilities.WriteBytesLittleEndian((uint)ImageType, buffer.Slice(0));
            EndianUtilities.WriteBytesLittleEndian((uint)Flags, buffer.Slice(4));
            latin1Encoding.GetBytes(Comment.AsSpan(), buffer.Slice(8, 256));
            LegacyGeometry.Write(buffer.Slice(264));
            EndianUtilities.WriteBytesLittleEndian(DiskSize, buffer.Slice(280));
            EndianUtilities.WriteBytesLittleEndian(BlockSize, buffer.Slice(288));
            EndianUtilities.WriteBytesLittleEndian(BlockCount, buffer.Slice(292));
            EndianUtilities.WriteBytesLittleEndian(BlocksAllocated, buffer.Slice(296));
            EndianUtilities.WriteBytesLittleEndian(UniqueId, buffer.Slice(300));
            EndianUtilities.WriteBytesLittleEndian(ModificationId, buffer.Slice(316));
            EndianUtilities.WriteBytesLittleEndian(ParentId, buffer.Slice(332));
        }
        else if (_fileVersion.Major == 1 && _fileVersion.Minor == 1)
        {
            EndianUtilities.WriteBytesLittleEndian(HeaderSize, buffer.Slice(0));
            EndianUtilities.WriteBytesLittleEndian((uint)ImageType, buffer.Slice(4));
            EndianUtilities.WriteBytesLittleEndian((uint)Flags, buffer.Slice(8));
            latin1Encoding.GetBytes(Comment.AsSpan(), buffer.Slice(12, 256));
            EndianUtilities.WriteBytesLittleEndian(BlocksOffset, buffer.Slice(268));
            EndianUtilities.WriteBytesLittleEndian(DataOffset, buffer.Slice(272));
            LegacyGeometry.Write(buffer.Slice(276));
            EndianUtilities.WriteBytesLittleEndian(DiskSize, buffer.Slice(296));
            EndianUtilities.WriteBytesLittleEndian(BlockSize, buffer.Slice(304));
            EndianUtilities.WriteBytesLittleEndian(BlockExtraSize, buffer.Slice(308));
            EndianUtilities.WriteBytesLittleEndian(BlockCount, buffer.Slice(312));
            EndianUtilities.WriteBytesLittleEndian(BlocksAllocated, buffer.Slice(316));
            EndianUtilities.WriteBytesLittleEndian(UniqueId, buffer.Slice(320));
            EndianUtilities.WriteBytesLittleEndian(ModificationId, buffer.Slice(336));
            EndianUtilities.WriteBytesLittleEndian(ParentId, buffer.Slice(352));
            EndianUtilities.WriteBytesLittleEndian(ParentModificationId, buffer.Slice(368));

            if (HeaderSize > 384)
            {
                LChsGeometry.Write(buffer.Slice(384));
            }
        }
        else
        {
            throw new IOException($"Unrecognized file version: {_fileVersion}");
        }

        return (int)HeaderSize;
    }
}