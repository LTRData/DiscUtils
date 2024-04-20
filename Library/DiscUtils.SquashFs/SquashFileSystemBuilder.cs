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
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using DiscUtils.Compression;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Buffers;

namespace DiscUtils.SquashFs;

/// <summary>
/// Class that creates SquashFs file systems.
/// </summary>
public sealed class SquashFileSystemBuilder : StreamBuilder, IFileSystemBuilder
{
    private const int DefaultBlockSize = 131072;
    private BuilderContext _context;
    private uint _nextInode;

    private BuilderDirectory _rootDir;

    /// <summary>
    /// Initializes a new instance of the SquashFileSystemBuilder class.
    /// </summary>
    public SquashFileSystemBuilder()
    {
        DefaultFilePermissions = UnixFilePermissions.OwnerRead | UnixFilePermissions.OwnerWrite |
                                 UnixFilePermissions.GroupRead | UnixFilePermissions.GroupWrite;
        DefaultDirectoryPermissions = UnixFilePermissions.OwnerAll | UnixFilePermissions.GroupRead |
                                      UnixFilePermissions.GroupExecute | UnixFilePermissions.OthersRead |
                                      UnixFilePermissions.OthersExecute;
        DefaultUser = 0;
        DefaultGroup = 0;
    }

    /// <summary>
    /// Gets or sets the default permissions used for new directories.
    /// </summary>
    public UnixFilePermissions DefaultDirectoryPermissions { get; set; }

    /// <summary>
    /// Gets or sets the default permissions used for new files.
    /// </summary>
    public UnixFilePermissions DefaultFilePermissions { get; set; }

    /// <summary>
    /// Gets or sets the default group id used for new files and directories.
    /// </summary>
    public int DefaultGroup { get; set; }

    /// <summary>
    /// Gets or sets the default user id used for new files and directories.
    /// </summary>
    public int DefaultUser { get; set; }

    string IFileSystemBuilder.VolumeIdentifier { get; set; }

    /// <summary>
    /// Adds a file to the file system.
    /// </summary>
    /// <param name="path">The full path to the file.</param>
    /// <param name="content">The content of the file.</param>
    /// <remarks>
    /// <para>The created file with have the default owner, group, permissions and the
    /// current time as it's modification time.  Any missing parent directories will be
    /// created, with default owner, group and directory permissions.</para>
    /// </remarks>
    public void AddFile(string path, Stream content)
    {
        AddFile(path, content, DefaultUser, DefaultGroup, DefaultFilePermissions, DateTime.Now);
    }

    /// <summary>
    /// Adds a file to the file system.
    /// </summary>
    /// <param name="path">The full path to the file.</param>
    /// <param name="content">The content of the file.</param>
    /// <remarks>
    /// <para>The created file with have the default owner, group, permissions and the
    /// current time as it's modification time.  Any missing parent directories will be
    /// created, with default owner, group and directory permissions.</para>
    /// </remarks>
    public void AddFile(string path, byte[] content)
    {
        AddFile(path, content, DefaultUser, DefaultGroup, DefaultFilePermissions, DateTime.Now);
    }

    /// <summary>
    /// Adds a file to the file system.
    /// </summary>
    /// <param name="path">The full path to the file.</param>
    /// <param name="contentPath">Local file system path to the file to add.</param>
    /// <remarks>
    /// <para>The created file with have the default owner, group, permissions and the
    /// current time as it's modification time.  Any missing parent directories will be
    /// created with default owner, group and directory permissions.</para>
    /// </remarks>
    public void AddFile(string path, string contentPath)
    {
        AddFile(path, contentPath, DefaultUser, DefaultGroup, DefaultFilePermissions, DateTime.Now);
    }

    /// <summary>
    /// Adds a file to the file system.
    /// </summary>
    /// <param name="path">The full path to the file.</param>
    /// <param name="content">The content of the file.</param>
    /// <param name="user">The owner of the file.</param>
    /// <param name="group">The group of the file.</param>
    /// <param name="permissions">The access permission of the file.</param>
    /// <param name="modificationTime">The modification time of the file.</param>
    /// <remarks>
    /// <para>Any missing parent directories will be created with the specified owner and group,
    /// default directory permissions and the current time as the modification time.</para>
    /// </remarks>
    public void AddFile(string path, Stream content, int user, int group, UnixFilePermissions permissions,
                        DateTime modificationTime)
    {
        var file = new BuilderFile(content)
        {
            UserId = user,
            GroupId = group,
            Mode = permissions,
            ModificationTime = modificationTime
        };

        var dirNode = CreateDirectory(
            Utilities.GetDirectoryFromPath(path),
            user,
            group,
            DefaultDirectoryPermissions);
        dirNode.AddChild(Utilities.GetFileFromPath(path), file);
    }

    /// <summary>
    /// Adds a file to the file system.
    /// </summary>
    /// <param name="path">The full path to the file.</param>
    /// <param name="content">The content of the file.</param>
    /// <param name="user">The owner of the file.</param>
    /// <param name="group">The group of the file.</param>
    /// <param name="permissions">The access permission of the file.</param>
    /// <param name="modificationTime">The modification time of the file.</param>
    /// <remarks>
    /// <para>Any missing parent directories will be created with the specified owner and group,
    /// default directory permissions and the current time as the modification time.</para>
    /// </remarks>
    public void AddFile(string path, byte[] content, int user, int group, UnixFilePermissions permissions,
                        DateTime modificationTime)
    {
        var file = new BuilderFile(content)
        {
            UserId = user,
            GroupId = group,
            Mode = permissions,
            ModificationTime = modificationTime
        };

        var dirNode = CreateDirectory(
            Utilities.GetDirectoryFromPath(path),
            user,
            group,
            DefaultDirectoryPermissions);
        dirNode.AddChild(Utilities.GetFileFromPath(path), file);
    }

    /// <summary>
    /// Adds a file to the file system.
    /// </summary>
    /// <param name="path">The full path to the file.</param>
    /// <param name="contentPath">Local file system path to the file to add.</param>
    /// <param name="user">The owner of the file.</param>
    /// <param name="group">The group of the file.</param>
    /// <param name="permissions">The access permission of the file.</param>
    /// <param name="modificationTime">The modification time of the file.</param>
    /// <remarks>
    /// <para>Any missing parent directories will be created with the specified owner and group,
    /// default directory permissions and the current time as the modification time.</para>
    /// </remarks>
    public void AddFile(string path, string contentPath, int user, int group, UnixFilePermissions permissions,
                        DateTime modificationTime)
    {
        var file = new BuilderFile(contentPath)
        {
            UserId = user,
            GroupId = group,
            Mode = permissions,
            ModificationTime = modificationTime
        };

        var dirNode = CreateDirectory(
            Utilities.GetDirectoryFromPath(path),
            user,
            group,
            DefaultDirectoryPermissions);
        dirNode.AddChild(Utilities.GetFileFromPath(path), file);
    }

    /// <summary>
    /// Adds a directory to the file system.
    /// </summary>
    /// <param name="path">The full path to the directory.</param>
    /// <remarks>
    /// <para>The created directory with have the default owner, group, permissions and the
    /// current time as it's modification time.  Any missing parent directories will be
    /// created with default owner, group and directory permissions.</para>
    /// </remarks>
    public void AddDirectory(string path)
    {
        AddDirectory(path, DefaultUser, DefaultGroup, DefaultDirectoryPermissions, DateTime.Now);
    }

    /// <summary>
    /// Adds a directory to the file system.
    /// </summary>
    /// <param name="path">The full path to the directory.</param>
    /// <param name="user">The owner of the directory.</param>
    /// <param name="group">The group of the directory.</param>
    /// <param name="permissions">The access permission of the directory.</param>
    /// <param name="modificationTime">The modification time of the directory.</param>
    /// <remarks>
    /// <para>The created directory with have the default owner, group, permissions and the
    /// current time as it's modification time.  Any missing parent directories will be
    /// created with the specified owner, group, and directory permissions.  The current time
    /// will be used as the modification time.</para>
    /// </remarks>
    public void AddDirectory(string path, int user, int group, UnixFilePermissions permissions,
                             DateTime modificationTime)
    {
        var dir = new BuilderDirectory
        {
            UserId = user,
            GroupId = group,
            Mode = permissions,
            ModificationTime = modificationTime
        };

        var parentDir = CreateDirectory(
            Utilities.GetDirectoryFromPath(path),
            user,
            group,
            permissions);
        parentDir.AddChild(Utilities.GetFileFromPath(path), dir);
    }

    void IFileSystemBuilder.AddDirectory(
        string name, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
    {
        AddDirectory(name, 0, 0, Utilities.UnixFilePermissionsFromFileAttributes(attributes), writtenTime);
    }

    void IFileSystemBuilder.AddFile(string name, byte[] buffer, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
    {
        AddFile(name, buffer, 0, 0, Utilities.UnixFilePermissionsFromFileAttributes(attributes), writtenTime);
    }

    void IFileSystemBuilder.AddFile(string name, string sourcefile, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
    {
        AddFile(name, sourcefile, 0, 0, Utilities.UnixFilePermissionsFromFileAttributes(attributes), writtenTime);
    }

    void IFileSystemBuilder.AddFile(string name, Stream stream, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
    {
        AddFile(name, stream, 0, 0, Utilities.UnixFilePermissionsFromFileAttributes(attributes), writtenTime);
    }

    /// <summary>
    /// Builds the file system, returning a new stream.
    /// </summary>
    /// <returns>The stream containing the file system.</returns>
    /// <remarks>
    /// This method uses a temporary file to construct the file system, use of
    /// the <c>Build(Stream)</c> or <c>Build(string)</c> variant is recommended
    /// when the file system will be written to a file.
    /// </remarks>
    public override Stream Build()
    {
        Stream stream = new FileStream(Path.GetTempFileName(), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, bufferSize: 2 * 1024 * 1024, FileOptions.DeleteOnClose);
        try
        {
            Build(stream);
            return stream;
        }
        catch (Exception ex)
        {
            stream?.Dispose();

            throw new Exception("SquashFs build failed", ex);
        }
    }

    /// <summary>
    /// Builds the file system, returning a new stream.
    /// </summary>
    /// <returns>The stream containing the file system.</returns>
    /// <remarks>
    /// This method uses a temporary file to construct the file system, use of
    /// the <c>Build(Stream)</c> or <c>Build(string)</c> variant is recommended
    /// when the file system will be written to a file.
    /// </remarks>
    public override async Task<Stream> BuildAsync(CancellationToken cancellationToken)
    {
        Stream stream = new FileStream(Path.GetTempFileName(), FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, bufferSize: 2 * 1024 * 1024, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        try
        {
            await BuildAsync(stream, cancellationToken).ConfigureAwait(false);
            return stream;
        }
        catch (Exception ex)
        {
            stream?.Dispose();

            throw new Exception("SquashFs build failed", ex);
        }
    }

    /// <summary>
    /// Writes the file system to an existing stream.
    /// </summary>
    /// <param name="output">The stream to write to.</param>
    /// <remarks>The <c>output</c> stream must support seeking and writing.</remarks>
    public override void Build(Stream output)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (!output.CanWrite)
        {
            throw new ArgumentException("Output stream must be writable", nameof(output));
        }

        if (!output.CanSeek)
        {
            throw new ArgumentException("Output stream must support seeking", nameof(output));
        }

        _context = new BuilderContext
        {
            RawStream = output,
            DataBlockSize = DefaultBlockSize,
            IoBuffer = new byte[DefaultBlockSize]
        };

        var inodeWriter = new MetablockWriter();
        var dirWriter = new MetablockWriter();
        var fragWriter = new FragmentWriter(_context);
        var idWriter = new IdTableWriter(_context);

        _context.AllocateInode = AllocateInode;
        _context.AllocateId = idWriter.AllocateId;
        _context.WriteDataBlock = WriteDataBlock;
        _context.WriteFragment = fragWriter.WriteFragment;
        _context.InodeWriter = inodeWriter;
        _context.DirectoryWriter = dirWriter;

        _nextInode = 1;

        var superBlock = new SuperBlock
        {
            Magic = SuperBlock.SquashFsMagic,
            CreationTime = DateTime.Now,
            BlockSize = (uint)_context.DataBlockSize,
            Compression = SuperBlock.CompressionType.ZLib
        };
        superBlock.BlockSizeLog2 = (ushort)MathUtilities.Log2(superBlock.BlockSize);
        superBlock.MajorVersion = 4;
        superBlock.MinorVersion = 0;

        output.Position = superBlock.Size;

        GetRoot().Reset();
        GetRoot().Write(_context);
        fragWriter.Flush();
        superBlock.RootInode = GetRoot().InodeRef;
        superBlock.InodesCount = _nextInode - 1;
        superBlock.FragmentsCount = (uint)fragWriter.FragmentBlocksCount;
        superBlock.UidGidCount = (ushort)idWriter.IdCount;
        superBlock.Flags = SuperBlock.SuperBlockFlags.NoXAttrs | SuperBlock.SuperBlockFlags.FragmentsAlwaysGenerated;

        superBlock.InodeTableStart = output.Position;
        inodeWriter.Persist(output);

        superBlock.DirectoryTableStart = output.Position;
        dirWriter.Persist(output);

        superBlock.FragmentTableStart = fragWriter.Persist();
        superBlock.LookupTableStart = -1;
        superBlock.UidGidTableStart = idWriter.Persist();
        superBlock.ExtendedAttrsTableStart = -1;

        superBlock.BytesUsed = output.Position;

        // Pad to 4KB
        var end = MathUtilities.RoundUp(output.Position, 4 * Sizes.OneKiB);
        if (end != output.Position)
        {
            Span<byte> padding = stackalloc byte[(int)(end - output.Position)];
            padding.Clear();
            output.Write(padding);
        }

        // Go back and write the superblock
        output.Position = 0;
        Span<byte> buffer = stackalloc byte[superBlock.Size];
        superBlock.WriteTo(buffer);
        output.Write(buffer);
        output.Position = end;
    }

    public override Task BuildAsync(Stream output, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Build(output);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Allocates a unique inode identifier.
    /// </summary>
    /// <returns>The inode identifier.</returns>
    private uint AllocateInode()
    {
        return _nextInode++;
    }

    /// <summary>
    /// Writes a block of file data, possibly compressing it.
    /// </summary>
    /// <param name="buffer">The data to write.</param>
    /// <param name="offset">Offset of the first byte to write.</param>
    /// <param name="count">The number of bytes to write.</param>
    /// <returns>
    /// The 'length' of the (possibly compressed) data written, including
    /// a flag indicating compression (or not).
    /// </returns>
    private uint WriteDataBlock(byte[] buffer, int offset, int count)
        => WriteDataBlock(buffer.AsSpan(offset, count));

    private uint WriteDataBlock(ReadOnlySpan<byte> buffer)
    {
        const int SQUASHFS_COMPRESSED_BIT = 1 << 24;

        var compressed = new MemoryStream();
        using (var compStream = new ZlibStream(compressed, CompressionMode.Compress, true))
        {
            compStream.Write(buffer);
        }

        ReadOnlySpan<byte> writeData;
        int returnValue;

        if (compressed.Length < buffer.Length)
        {
            var compressedData = compressed.AsSpan();
            compressedData[1] = 0xda;
            writeData = compressedData;
            returnValue = writeData.Length;
        }
        else
        {
            writeData = buffer;
            returnValue = writeData.Length | SQUASHFS_COMPRESSED_BIT; // Flag to indicate uncompressed buffer
        }

        _context.RawStream.Write(writeData);

        return (uint)returnValue;
    }

    /// <summary>
    /// Delayed root construction, to permit default permissions / identity info to be
    /// set before root is created.
    /// </summary>
    /// <returns>The root directory.</returns>
    private BuilderDirectory GetRoot()
    {
        _rootDir ??= new BuilderDirectory
            {
                Mode = DefaultDirectoryPermissions
            };

        return _rootDir;
    }

    private BuilderDirectory CreateDirectory(string path, int user, int group, UnixFilePermissions permissions)
    {
        var currentDir = GetRoot();
        var elems = path.Split(Utilities.PathSeparators, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < elems.Length; ++i)
        {
            var nextDirAsNode = currentDir.GetChild(elems[i]);
            var nextDir = nextDirAsNode as BuilderDirectory;

            if (nextDirAsNode == null)
            {
                nextDir = new BuilderDirectory
                {
                    UserId = user,
                    GroupId = group,
                    Mode = permissions,
                    ModificationTime = DateTime.Now
                };

                currentDir.AddChild(elems[i], nextDir);
            }
            else if (nextDir == null)
            {
                throw new FileNotFoundException($"Found {nextDirAsNode.Inode.Type}, expecting Directory",
                    string.Join("\\", elems, 0, i + 1));
            }

            currentDir = nextDir;
        }

        return currentDir;
    }

    public bool Exists(string path)
    {
        var currentDir = GetRoot();
        var elems = path.Split(Utilities.PathSeparators, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < elems.Length; ++i)
        {
            var nextDirAsNode = currentDir.GetChild(elems[i]);

            if (nextDirAsNode is not BuilderDirectory nextDir)
            {
                return false;
            }

            currentDir = nextDir;
        }

        return true;
    }

    public int FileCount
    {
        get
        {
            var n = 0;
            foreach (var entry in GetRoot().EnumerateTreeEntries())
            {
                n++;
            }

            return n;
        }
    }

    public long TotalSize
    {
        get
        {
            var n = 0L;
            foreach (var entry in GetRoot().EnumerateTreeEntries())
            {
                if (entry is BuilderFile file)
                {
                    n += file.Inode.FileSize;
                }
            }

            return n;
        }
    }

    public IFileSystem GenerateFileSystem() => new SquashFileSystemReader(Build());

    protected override List<BuilderExtent> FixExtents(out long totalLength) => throw new NotImplementedException();
}