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
using System.IO.Compression;
using DiscUtils.Compression;
using DiscUtils.Streams;
using DiscUtils.Vfs;

namespace DiscUtils.SquashFs;

internal class VfsSquashFileSystemReader : VfsReadOnlyFileSystem<DirectoryEntry, File, Directory, Context>,
                                           IUnixFileSystem
{
    public override bool IsCaseSensitive => true;

    public const int MetadataBufferSize = 8 * 1024;
    private readonly BlockCache<Block> _blockCache;

    private readonly Context _context;
    private byte[] _ioBuffer;
    private readonly BlockCache<Metablock> _metablockCache;

    public VfsSquashFileSystemReader(Stream stream)
        : base(new DiscFileSystemOptions())
    {
        _context = new Context
        {
            SuperBlock = new SuperBlock(),
            RawStream = stream
        };

        // Read superblock
        stream.Position = 0;
        _context.SuperBlock.ReadFrom(stream, _context.SuperBlock.Size);

        if (_context.SuperBlock.Magic != SuperBlock.SquashFsMagic)
        {
            throw new IOException("Invalid SquashFS filesystem - magic mismatch");
        }

        if (_context.SuperBlock.Compression != SuperBlock.CompressionType.ZLib)
        {
            throw new IOException("Unsupported compression used");
        }

        if (_context.SuperBlock.ExtendedAttrsTableStart != -1)
        {
            throw new IOException("Unsupported extended attributes present");
        }

        if (_context.SuperBlock.MajorVersion != 4)
        {
            throw new IOException($"Unsupported file system version: {_context.SuperBlock.MajorVersion}.{_context.SuperBlock.MinorVersion}");
        }

        // Create block caches, used to reduce the amount of I/O and decompression activity.
        _blockCache = new BlockCache<Block>((int)_context.SuperBlock.BlockSize, 20);
        _metablockCache = new BlockCache<Metablock>(MetadataBufferSize, 20);
        _context.ReadBlock = ReadBlock;
        _context.ReadMetaBlock = ReadMetaBlock;

        _context.InodeReader = new MetablockReader(_context, _context.SuperBlock.InodeTableStart);
        _context.DirectoryReader = new MetablockReader(_context, _context.SuperBlock.DirectoryTableStart);

        if (_context.SuperBlock.FragmentTableStart != -1)
        {
            _context.FragmentTableReaders = LoadIndirectReaders(
                _context.SuperBlock.FragmentTableStart,
                (int)_context.SuperBlock.FragmentsCount,
                FragmentRecord.RecordSize);
        }

        if (_context.SuperBlock.UidGidTableStart != -1)
        {
            _context.UidGidTableReaders = LoadIndirectReaders(
                _context.SuperBlock.UidGidTableStart,
                _context.SuperBlock.UidGidCount,
                4);
        }

        // Bootstrap the root directory
        _context.InodeReader.SetPosition(_context.SuperBlock.RootInode);
        var dirInode = (DirectoryInode)Inode.Read(_context.InodeReader);
        RootDirectory = new Directory(_context, dirInode, _context.SuperBlock.RootInode);
    }

    public override string FriendlyName
    {
        get { return "SquashFs"; }
    }

    public override string VolumeLabel
    {
        get { return string.Empty; }
    }

    public UnixFileSystemInfo GetUnixFileInfo(string path)
    {
        var file = GetFile(path);
        var inode = file.Inode;

        var info = new UnixFileSystemInfo
        {
            FileType = FileTypeFromInodeType(inode.Type),
            UserId = GetId(inode.UidKey),
            GroupId = GetId(inode.GidKey),
            Permissions = (UnixFilePermissions)inode.Mode,
            Inode = inode.InodeNumber,
            LinkCount = inode.NumLinks,
            DeviceId = inode is not DeviceInode devInod ? 0 : devInod.DeviceId
        };

        return info;
    }

    /// <summary>
    /// Size of the Filesystem in bytes
    /// </summary>
    public override long Size
    {
        get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
    }

    /// <summary>
    /// Used space of the Filesystem in bytes
    /// </summary>
    public override long UsedSpace
    {
        get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
    }

    /// <summary>
    /// Available space of the Filesystem in bytes
    /// </summary>
    public override long AvailableSpace
    {
        get { throw new NotSupportedException("Filesystem size is not (yet) supported"); }
    }

    public override bool SupportsUsedAvailableSpace => false;

    internal static UnixFileType FileTypeFromInodeType(InodeType inodeType)
    {
        return inodeType switch
        {
            InodeType.BlockDevice or InodeType.ExtendedBlockDevice => UnixFileType.Block,
            InodeType.CharacterDevice or InodeType.ExtendedCharacterDevice => UnixFileType.Character,
            InodeType.Directory or InodeType.ExtendedDirectory => UnixFileType.Directory,
            InodeType.Fifo or InodeType.ExtendedFifo => UnixFileType.Fifo,
            InodeType.File or InodeType.ExtendedFile => UnixFileType.Regular,
            InodeType.Socket or InodeType.ExtendedSocket => UnixFileType.Socket,
            InodeType.Symlink or InodeType.ExtendedSymlink => UnixFileType.Link,
            _ => throw new NotSupportedException($"Unrecognized inode type: {inodeType}"),
        };
    }

    protected override File ConvertDirEntryToFile(DirectoryEntry dirEntry)
    {
        var inodeRef = dirEntry.InodeReference;
        _context.InodeReader.SetPosition(inodeRef);
        var inode = Inode.Read(_context.InodeReader);

        if (dirEntry.IsSymlink)
        {
            return new Symlink(_context, inode, inodeRef);
        }
        if (dirEntry.IsDirectory)
        {
            return new Directory(_context, inode, inodeRef);
        }
        return new File(_context, inode, inodeRef);
    }

    private MetablockReader[] LoadIndirectReaders(long pos, int count, int recordSize)
    {
        _context.RawStream.Position = pos;
        var numBlocks = MathUtilities.Ceil(count * recordSize, MetadataBufferSize);

        var tableBytes = _context.RawStream.ReadExactly(numBlocks * 8);
        var result = new MetablockReader[numBlocks];
        for (var i = 0; i < numBlocks; ++i)
        {
            var block = EndianUtilities.ToInt64LittleEndian(tableBytes, i * 8);
            result[i] = new MetablockReader(_context, block);
        }

        return result;
    }

    private int GetId(ushort idKey)
    {
        var recordsPerBlock = MetadataBufferSize / 4;
        var block = idKey / recordsPerBlock;
        var offset = idKey % recordsPerBlock;

        var reader = _context.UidGidTableReaders[block];
        reader.SetPosition(0, offset * 4);
        return reader.ReadInt();
    }

    private Block ReadBlock(long pos, int diskLen)
    {
        var block = _blockCache.GetBlock(pos);
        if (block.Available >= 0)
        {
            return block;
        }

        var stream = _context.RawStream;
        stream.Position = pos;

        var readLen = diskLen & 0x00FFFFFF;
        var isCompressed = (diskLen & 0x01000000) == 0;

        if (isCompressed)
        {
            if (_ioBuffer == null || readLen > _ioBuffer.Length)
            {
                _ioBuffer = new byte[readLen];
            }

            stream.ReadExactly(_ioBuffer, 0, readLen);

            using var zlibStream = new ZlibStream(new MemoryStream(_ioBuffer, 0, readLen, false),
                    CompressionMode.Decompress, true);
            block.Available = zlibStream.ReadMaximum(block.Data, 0, (int)_context.SuperBlock.BlockSize);
        }
        else
        {
            stream.ReadExactly(block.Data, 0, readLen);
            block.Available = readLen;
        }

        return block;
    }

    private Metablock ReadMetaBlock(long pos)
    {
        var block = _metablockCache.GetBlock(pos);
        if (block.Available >= 0)
        {
            return block;
        }

        var stream = _context.RawStream;
        stream.Position = pos;

        Span<byte> buffer = stackalloc byte[2];
        stream.ReadExactly(buffer);

        int readLen = EndianUtilities.ToUInt16LittleEndian(buffer);
        var isCompressed = (readLen & 0x8000) == 0;
        readLen &= 0x7FFF;
        if (readLen == 0)
        {
            readLen = 0x8000;
        }

        block.NextBlockStart = pos + readLen + 2;

        if (isCompressed)
        {
            if (_ioBuffer == null || readLen > _ioBuffer.Length)
            {
                _ioBuffer = new byte[readLen];
            }

            stream.ReadExactly(_ioBuffer, 0, readLen);

            using var zlibStream = new ZlibStream(new MemoryStream(_ioBuffer, 0, readLen, false),
                    CompressionMode.Decompress, true);
            block.Available = zlibStream.ReadMaximum(block.Data, 0, MetadataBufferSize);
        }
        else
        {
            block.Available = stream.ReadMaximum(block.Data, 0, readLen);
        }

        return block;
    }
}
