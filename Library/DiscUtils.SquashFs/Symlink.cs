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

using System.Text;
using DiscUtils.Vfs;

namespace DiscUtils.SquashFs;

internal class Symlink : File, IVfsSymlink<DirectoryEntry, File>
{
    private string _targetPath;

    public Symlink(Context context, Inode inode, MetadataRef inodeRef)
        : base(context, inode, inodeRef) { }

    /// <summary>
    /// The link's target as stored: the UTF-8 path that follows the inode in the inode table, relative to the
    /// link's directory unless it starts with a slash.
    /// </summary>
    public string TargetPath
    {
        get
        {
            if (_targetPath is null)
            {
                var inode = (SymlinkInode)Inode;
                Context.InodeReader.SetPosition(InodeRef);
                Context.InodeReader.Skip(inode.Size);
                var target = new byte[inode.SymlinkSize];
                Context.InodeReader.Read(target);
                _targetPath = Encoding.UTF8.GetString(target);
            }

            return _targetPath;
        }
    }
}