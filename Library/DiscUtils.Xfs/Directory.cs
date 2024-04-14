//
// Copyright (c) 2008-2011, Kenneth Bell
// Copyright (c) 2016, Bianco Veigel
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
using System.Collections.Generic;
using DiscUtils.Vfs;
using DiscUtils.Streams;
using DiscUtils.Internal;
using DiscUtils.CoreCompat;

namespace DiscUtils.Xfs;
internal class Directory : File, IVfsDirectory<DirEntry, File>
{
    public Directory(Context context, Inode inode)
        : base(context, inode)
    {
    }

    private FastDictionary<DirEntry> _allEntries;

    public IReadOnlyDictionary<string, DirEntry> AllEntries
    {
        get
        {
            if (_allEntries is null)
            {
                var result = new FastDictionary<DirEntry>(StringComparer.Ordinal, entry => entry.FileName);
                if (Inode.Format == InodeFormat.Local)
                {
                    //shortform directory
                    var sfDir = new ShortformDirectory(Context);
                    sfDir.ReadFrom(Inode.DataFork);
                    foreach (var entry in sfDir.Entries)
                    {
                        result.Add(new DirEntry(entry, Context));
                    }
                }
                else if (Inode.Format == InodeFormat.Extents)
                {
                    if (Inode.Extents == 1)
                    {
                        var blockDir = new BlockDirectory(Context);
                        if (Context.SuperBlock.SbVersion == 5)
                        {
                            blockDir = new BlockDirectoryV5(Context);
                        }

                        var dirContent = Inode.GetContentBuffer(Context);
                        var buffer = dirContent.ReadAll();
                        blockDir.ReadFrom(buffer);
                        if (!blockDir.HasValidMagic)
                        {
                            throw new IOException("invalid block directory magic");
                        }

                        AddDirEntries(blockDir.Entries, result);
                    }
                    else
                    {
                        var extents = Inode.GetExtents();
                        AddLeafDirExtentEntries(extents, result);
                    }
                }
                else
                {
                    var header = new BTreeExtentRoot();
                    header.ReadFrom(Inode.DataFork);
                    header.LoadBtree(Context);
                    var extents = header.GetExtents();
                    AddLeafDirExtentEntries(extents, result);
                }

                _allEntries = result;
            }

            return _allEntries;
        }
    }

    private void AddLeafDirExtentEntries(IEnumerable<Extent> extents, FastDictionary<DirEntry> target)
    {
        var leafOffset = LeafDirectory.LeafOffset / Context.SuperBlock.Blocksize;

        foreach (var extent in extents)
        {
            if (extent.StartOffset < leafOffset)
            {
                for (long i = 0; i < extent.BlockCount; i++)
                {
                    var buffer = extent.GetData(Context, i* Context.SuperBlock.DirBlockSize, Context.SuperBlock.DirBlockSize);
                    var leafDir = new LeafDirectory(Context);
                    if (Context.SuperBlock.SbVersion == 5)
                    {
                        leafDir = new LeafDirectoryV5(Context);
                    }

                    leafDir.ReadFrom(buffer);
                    if (!leafDir.HasValidMagic)
                    {
                        throw new IOException("invalid leaf directory magic");
                    }

                    AddDirEntries(leafDir.Entries, target);
                }

            }
        }
    }

    private void AddDirEntries(IEnumerable<BlockDirectoryData> entries, FastDictionary<DirEntry> target)
    {
        foreach (var entry in entries)
        {
            if (entry is not IDirectoryEntry dirEntry)
            {
                continue;
            }

            var name = Context.Options.FileNameEncoding.GetString(dirEntry.Name).SanitizeFileName();
            if (name is "." or "..")
            {
                continue;
            }

            target.Add(new DirEntry(dirEntry, Context));
        }
    }

    public DirEntry Self => null;

    public DirEntry GetEntryByName(string name)
        => AllEntries.TryGetValue(name, out var entry) ? entry : null;

    public DirEntry CreateNewFile(string name)
    {
        throw new NotImplementedException();
    }
}
