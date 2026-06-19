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

using DiscUtils.Btrfs.Base;
using DiscUtils.Btrfs.Base.Items;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using DiscUtils.Vfs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.ComTypes;

namespace DiscUtils.Btrfs;

internal class Context : VfsContext
{
    public Context(BtrfsFileSystemOptions options)
    {
        FsTrees = [];
        Options = options;
    }

    public BtrfsFileSystemOptions Options { get; private set; }

    public override Stream RawStream { get; set; }

    public SuperBlock SuperBlock { get; set; }

    internal NodeHeader ChunkTreeRoot { get; set; }

    internal NodeHeader RootTreeRoot { get; set; }

    internal Dictionary<ulong, NodeHeader> FsTrees { get; }

    internal DirEntry Initialize()
    {
        foreach (var offset in BtrfsFileSystem.SuperblockOffsets)
        {
            if (offset + SuperBlock.Length > RawStream.Length)
            {
                break;
            }

            RawStream.Position = offset;
            var superblockData = RawStream.ReadExactly(SuperBlock.Length);
            var superblock = new SuperBlock();
            superblock.ReadFrom(superblockData);

            if (superblock.Magic != SuperBlock.BtrfsMagic)
            {
                throw new IOException("Invalid Superblock Magic");
            }

            if (SuperBlock == null
                || SuperBlock.Generation < superblock.Generation)
            {
                SuperBlock = superblock;
            }

            VerifyChecksum(superblock.Checksum, superblockData.AsSpan(0x20, 0x1000 - 0x20));
        }

        if (SuperBlock == null)
        {
            throw new IOException("No Superblock detected");
        }

        ChunkTreeRoot = ReadTree(SuperBlock.ChunkRoot, SuperBlock.ChunkRootLevel);

        LoadChunkMap();

        RootTreeRoot = ReadTree(SuperBlock.Root, SuperBlock.RootLevel);

        var rootDir = (DirItem)FindKey(SuperBlock.RootDirObjectid, ItemType.DirItem);

        RootItem fsTreeLocation;

        if (!Options.UseDefaultSubvolume)
        {
            fsTreeLocation = (RootItem)FindKey(Options.SubvolumeId, ItemType.RootItem);
        }
        else
        {
            fsTreeLocation = (RootItem)FindKey(rootDir.ChildLocation.ObjectId, rootDir.ChildLocation.ItemType);
        }

        FsTrees.Add(rootDir.ChildLocation.ObjectId, ReadTree(fsTreeLocation.ByteNr, fsTreeLocation.Level));

        var rootDirObjectId = fsTreeLocation.RootDirId;

        var dirEntry = new DirEntry(rootDir.ChildLocation.ObjectId, rootDirObjectId);

        return dirEntry;
    }

    internal NodeHeader GetFsTree(ulong treeId)
    {
        if (FsTrees.TryGetValue(treeId, out var tree))
        {
            return tree;
        }

        var rootItem = RootTreeRoot.FindFirst<RootItem>(new Key(treeId, ItemType.RootItem), this);
        if (rootItem == null)
        {
            return null;
        }

        tree = ReadTree(rootItem.ByteNr, rootItem.Level);
        FsTrees[treeId] = tree;
        return tree;
    }

    private readonly List<ChunkItem> _chunkMap = [];

    private void LoadChunkMap()
    {
        _chunkMap.Clear();

        // Include the bootstrap system chunks too.
        foreach (var chunk in SuperBlock.SystemChunkArray)
        {
            if (chunk.Key.ItemType == ItemType.ChunkItem)
            {
                _chunkMap.Add(chunk);
            }
        }

        var seenNodes = new HashSet<ulong>();

        LoadChunkMapFromNode(ChunkTreeRoot, seenNodes);

        _chunkMap.Sort(static (x, y) => x.Key.Offset.CompareTo(y.Key.Offset));
    }

    private void LoadChunkMapFromNode(NodeHeader node, HashSet<ulong> seenNodes)
    {
        if (!seenNodes.Add(node.LogicalAddress))
        {
            throw new IOException($"Cycle while walking chunk tree at logical 0x{node.LogicalAddress:X}");
        }

        if (node is LeafNode leaf)
        {
            foreach (var item in leaf.NodeData)
            {
                if (item is ChunkItem chunk &&
                    chunk.Key.ItemType == ItemType.ChunkItem)
                {
                    _chunkMap.Add(chunk);
                }
            }

            return;
        }

        if (node is InternalNode internalNode)
        {
            if (node.Level == 0)
            {
                throw new IOException("Invalid internal chunk tree node with level 0");
            }

            foreach (var keyPtr in internalNode.KeyPointers)
            {
                // While _chunkMapLoaded == false, this must resolve through SystemChunkArray.
                var child = ReadTree(
                    keyPtr.BlockNumber,
                    checked((byte)(node.Level - 1)));

                LoadChunkMapFromNode(child, seenNodes);
            }

            return;
        }

        throw new IOException($"Unsupported chunk tree node type {node.GetType().Name}");
    }

    internal ulong MapToPhysical(ulong logical)
    {
        foreach (var chunk in _chunkMap)
        {
            if (ContainsLogical(chunk, logical))
            {
                return MapChunkToPhysical(chunk, logical);
            }
        }

        foreach (var chunk in SuperBlock.SystemChunkArray)
        {
            if (chunk.Key.ItemType != ItemType.ChunkItem)
            {
                continue;
            }

            if (ContainsLogical(chunk, logical))
            {
                return MapChunkToPhysical(chunk, logical);
            }
        }

        throw new IOException($"no matching ChunkItem found for logical 0x{logical:X}");
    }

    private static bool ContainsLogical(ChunkItem chunk, ulong logical)
    {
        return logical >= chunk.Key.Offset &&
               logical < chunk.Key.Offset + chunk.ChunkSize;
    }

    private static ulong MapChunkToPhysical(ChunkItem chunk, ulong logical)
    {
        CheckStriping(chunk.Type);

        if (chunk.StripeCount < 1)
        {
            throw new IOException("Invalid stripe count in ChunkItem");
        }

        var stripe = chunk.Stripes[0];
        return stripe.Offset + (logical - chunk.Key.Offset);
    }

    internal NodeHeader ReadTree(ulong logical, byte level)
    {
        var physical = MapToPhysical(logical);
        RawStream.Seek((long)physical, SeekOrigin.Begin);
        var dataSize = level > 0 ? SuperBlock.NodeSize : SuperBlock.LeafSize;
        Span<byte> buffer = stackalloc byte[checked((int)dataSize)];
        buffer = buffer.Slice(0, RawStream.Read(buffer));
        var result = NodeHeader.Create(buffer, physical);
        VerifyChecksum(result.Checksum, buffer.Slice(0x20, (int)dataSize - 0x20));
        return result;
    }

    internal void VerifyChecksum(ReadOnlySpan<byte> checksum, ReadOnlySpan<byte> data)
    {
        if (!Options.VerifyChecksums)
        {
            return;
        }

        if (SuperBlock.ChecksumType != ChecksumType.Crc32C)
        {
            throw new NotImplementedException($"Unsupported ChecksumType {SuperBlock.ChecksumType}");
        }

        var crc = new Crc32LittleEndian(Crc32Algorithm.Castagnoli);
        crc.Process(data);
        Span<byte> calculated = stackalloc byte[4];
        EndianUtilities.WriteBytesLittleEndian(crc.Value, calculated);
        for (var i = 0; i < calculated.Length; i++)
        {
            if (calculated[i] != checksum[i])
            {
                throw new IOException("Invalid checksum");
            }
        }
    }

    private static void CheckStriping(BlockGroupFlag flags)
    {
        if ((flags & BlockGroupFlag.Raid0) == BlockGroupFlag.Raid0)
        {
            throw new IOException("Raid0 not supported");
        }

        if ((flags & BlockGroupFlag.Raid10) == BlockGroupFlag.Raid0)
        {
            throw new IOException("Raid10 not supported");
        }

        if ((flags & BlockGroupFlag.Raid5) == BlockGroupFlag.Raid0)
        {
            throw new IOException("Raid5 not supported");
        }

        if ((flags & BlockGroupFlag.Raid6) == BlockGroupFlag.Raid0)
        {
            throw new IOException("Raid6 not supported");
        }
    }

    internal BaseItem FindKey(ReservedObjectId objectId, ItemType type)
    {
        return FindKey((ulong)objectId, type);
    }

    internal BaseItem FindKey(ulong objectId, ItemType type)
    {
        var key = new Key(objectId, type);
        return FindKey(key);
    }

    internal BaseItem FindKey(Key key)
    {
        return key.ItemType switch
        {
            ItemType.RootItem => RootTreeRoot.FindFirst(key, this),
            ItemType.DirItem => RootTreeRoot.FindFirst(key, this),
            _ => throw new NotImplementedException(),
        };
    }

    internal IEnumerable<BaseItem> FindKey(ulong treeId, Key key)
    {
        var tree = GetFsTree(treeId);
        return key.ItemType switch
        {
            ItemType.DirItem => tree.Find(key, this),
            _ => throw new NotImplementedException(),
        };
    }

    internal IEnumerable<T> FindKey<T>(ulong treeId, Key key) where T : BaseItem
    {
        var tree = GetFsTree(treeId);
        return key.ItemType switch
        {
            ItemType.DirItem or ItemType.ExtentData => tree.Find<T>(key, this),
            _ => throw new NotImplementedException(),
        };
    }
}
