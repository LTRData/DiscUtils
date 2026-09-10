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


using System.IO;
using System.Text;
using DiscUtils.Vfs;

namespace DiscUtils.SquashFs;

internal class Symlink : File, IVfsSymlink<DirectoryEntry, File>
{
    /// <summary>
    /// The format's maximum target length: SQUASHFS_SYMLINK_MAX in squashfs_fs.h (squashfs-tools and the Linux
    /// driver), 65535 bytes. mksquashfs enforces it when it reads a link (a 65536-byte readlink buffer, and a
    /// result that fills it is refused), so no well-formed image stores a longer target.
    /// </summary>
    public const int MaxTargetLength = 65535;

    private string _targetPath;

    public Symlink(Context context, Inode inode, MetadataRef inodeRef)
        : base(context, inode, inodeRef) { }

    /// <summary>
    /// The link's target as stored: the UTF-8 path that follows the inode in the inode table, relative to the
    /// link's directory unless it starts with a slash. The length comes from the image, so it is checked against
    /// <see cref="MaxTargetLength"/> before anything is allocated, and the bytes must lie within the inode table,
    /// which the directory table follows directly. An extended inode keeps its xattr index after the target.
    /// </summary>
    public string TargetPath
    {
        get
        {
            if (_targetPath is null)
            {
                var inode = (SymlinkInode)Inode;
                if (inode.SymlinkSize > MaxTargetLength)
                {
                    throw new IOException($"Corrupt symlink inode: target length {inode.SymlinkSize} exceeds {MaxTargetLength}");
                }

                var reader = Context.InodeReader;
                reader.SetPosition(InodeRef);
                reader.Skip(inode.Size);
                var target = new byte[inode.SymlinkSize];
                var read = reader.Read(target);
                if (inode is ExtendedSymlinkInode extended)
                {
                    extended.XattrIndex = reader.ReadUInt();
                }

                if (read != target.Length || reader.CurrentBlockStart >= Context.SuperBlock.DirectoryTableStart)
                {
                    throw new IOException("Corrupt symlink inode: target runs past the inode table");
                }

                _targetPath = Encoding.UTF8.GetString(target);
            }

            return _targetPath;
        }
    }
}
