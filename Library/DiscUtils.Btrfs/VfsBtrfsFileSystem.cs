//
// Copyright (c) 2017, Bianco Veigel
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
using DiscUtils.Btrfs.Base;
using DiscUtils.Btrfs.Base.Items;
using DiscUtils.Streams;
using DiscUtils.Vfs;

namespace DiscUtils.Btrfs;

internal sealed class VfsBtrfsFileSystem : VfsReadOnlyFileSystem<DirEntry, File, Directory, Context>,
    IUnixFileSystem, IAllocationExtentsFileSystem
{
    public override bool IsCaseSensitive => true;

    public VfsBtrfsFileSystem(Stream stream) 
        :this(stream, new BtrfsFileSystemOptions())
    {
        
    }

    public VfsBtrfsFileSystem(Stream stream, BtrfsFileSystemOptions options)
        :base(options)
    {
        Context = new Context(options)
        {
            RawStream = stream,
        };
        foreach (var offset in BtrfsFileSystem.SuperblockOffsets)
        {
            if (offset + SuperBlock.Length > stream.Length)
            {
                break;
            }

            stream.Position = offset;
            var superblockData = stream.ReadExactly(SuperBlock.Length);
            var superblock = new SuperBlock();
            superblock.ReadFrom(superblockData);

            if (superblock.Magic != SuperBlock.BtrfsMagic)
            {
                throw new IOException("Invalid Superblock Magic");
            }

            if (Context.SuperBlock == null)
            {
                Context.SuperBlock = superblock;
            }
            else if (Context.SuperBlock.Generation < superblock.Generation)
            {
                Context.SuperBlock = superblock;
            }

            Context.VerifyChecksum(superblock.Checksum, superblockData.AsSpan(0x20, 0x1000 - 0x20));
        }

        if (Context.SuperBlock == null)
        {
            throw new IOException("No Superblock detected");
        }

        Context.ChunkTreeRoot = Context.ReadTree(Context.SuperBlock.ChunkRoot, Context.SuperBlock.ChunkRootLevel);
        Context.RootTreeRoot = Context.ReadTree(Context.SuperBlock.Root, Context.SuperBlock.RootLevel);

        var rootDir = (DirItem)Context.FindKey(Context.SuperBlock.RootDirObjectid, ItemType.DirItem);
        RootItem fsTreeLocation;
        if (!options.UseDefaultSubvolume)
        {
            fsTreeLocation = (RootItem)Context.FindKey(options.SubvolumeId, ItemType.RootItem);
        }
        else
        {
            fsTreeLocation = (RootItem)Context.FindKey(rootDir.ChildLocation.ObjectId, rootDir.ChildLocation.ItemType);
        }

        var rootDirObjectId = fsTreeLocation.RootDirId;
        Context.FsTrees.Add(rootDir.ChildLocation.ObjectId, Context.ReadTree(fsTreeLocation.ByteNr, fsTreeLocation.Level));

        var dirEntry = new DirEntry(rootDir.ChildLocation.ObjectId, rootDirObjectId);
        RootDirectory = new Directory(dirEntry, Context);
    }

    public override string FriendlyName
    {
        get { return "Btrfs"; }
    }

    /// <inheritdoc />
    public override string VolumeLabel { get { return Context.SuperBlock.Label; } }
    
    /// <summary>
    /// Size of the Filesystem in bytes
    /// </summary>
    public override long Size
    {
        get { return (long) Context.SuperBlock.TotalBytes; }
    }

    /// <summary>
    /// Used space of the Filesystem in bytes
    /// </summary>
    public override long UsedSpace
    {
        get { return (long)Context.SuperBlock.BytesUsed; }
    }

    /// <summary>
    /// Available space of the Filesystem in bytes
    /// </summary>
    public override long AvailableSpace
    {
        get { return Size - UsedSpace; }
    }

    public IEnumerable<Subvolume> GetSubvolumes()
    {
        var volumes = Context.RootTreeRoot.Find<RootRef>(new Key(ReservedObjectId.FsTree, ItemType.RootRef), Context);
        foreach (var volume in volumes)
        {
            yield return new Subvolume { Id = volume.Key.Offset, Name = volume.Name };
        }
    }

    protected override File ConvertDirEntryToFile(DirEntry dirEntry)
    {
        if (dirEntry.IsDirectory)
        {
            return dirEntry.CachedDirectory ?? (dirEntry.CachedDirectory = new Directory(dirEntry, Context));
        }
        else if (dirEntry.IsSymlink)
        {
            return new Symlink(dirEntry, Context);
        }
        else if (dirEntry.Type == DirItemChildType.RegularFile)
        {
            return new File(dirEntry, Context);
        }
        else
        {
            throw new NotSupportedException($"Type {dirEntry.Type} is not supported in btrfs");
        }
    }

    public IEnumerable<StreamExtent> PathToExtents(string path)
    {
        var file = GetFile(path);

        return file.EnumerateAllocationExtents();
    }

    public UnixFileSystemInfo GetUnixFileInfo(string path)
    {
        return new UnixFileSystemInfo
        {
            FileType = UnixFileType.Regular,
            Permissions = UnixFilePermissions.OwnerRead | UnixFilePermissions.GroupRead | UnixFilePermissions.OthersRead |
                UnixFilePermissions.OwnerExecute | UnixFilePermissions.GroupExecute | UnixFilePermissions.OthersExecute,
            UserId = 0,
            GroupId = 0,
            Inode = 0,
            LinkCount = 1
        };
    }
}
