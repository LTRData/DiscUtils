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
using System.Threading;
using System.Threading.Tasks;

namespace DiscUtils;

/// <summary>
/// Provides information about a file on a disc.
/// </summary>
public class DiscFileInfo : DiscFileSystemInfo
{
    internal DiscFileInfo(DiscFileSystem fileSystem, string path)
        : base(fileSystem, path) { }

    /// <summary>
    /// Gets an instance of the parent directory.
    /// </summary>
    public DiscDirectoryInfo? Directory => Parent;

    /// <summary>
    /// Gets a string representing the directory's full path.
    /// </summary>
    public string? DirectoryName => Directory?.FullName;

    /// <summary>
    /// Gets a value indicating whether the file exists.
    /// </summary>
    public override bool Exists => FileSystem.FileExists(FullPath);

    /// <summary>
    /// Gets or sets a value indicating whether the file is read-only.
    /// </summary>
    public bool IsReadOnly
    {
        get => (Attributes & FileAttributes.ReadOnly) != 0;

        set
        {
            if (value)
            {
                Attributes |= FileAttributes.ReadOnly;
            }
            else
            {
                Attributes &= ~FileAttributes.ReadOnly;
            }
        }
    }

    /// <summary>
    /// Gets the length of the current file in bytes.
    /// </summary>
    public virtual long Length => FileSystem.GetFileLength(FullPath);

    /// <summary>
    /// Gets the contents of the file as a byte array.
    /// </summary>
    public virtual byte[] ReadAllBytes() => FileSystem.ReadAllBytes(FullPath);

    /// <summary>
    /// Asynchronously gets the contents of the file as a byte array.
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public virtual ValueTask<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken) => FileSystem.ReadAllBytesAsync(FullPath, cancellationToken);

    /// <summary>
    /// Deletes a file.
    /// </summary>
    public override void Delete()
    {
        FileSystem.DeleteFile(FullPath);
    }

    /// <summary>
    /// Creates a <see cref="StreamWriter" /> that appends text to the file represented by this <see cref="DiscFileInfo"/>.
    /// </summary>
    /// <returns>The newly created writer.</returns>
    public virtual StreamWriter AppendText()
    {
        return new StreamWriter(Open(FileMode.Append));
    }

    /// <summary>
    /// Copies an existing file to a new file.
    /// </summary>
    /// <param name="destinationFileName">The destination file.</param>
    public void CopyTo(string destinationFileName)
    {
        CopyTo(destinationFileName, false);
    }

    /// <summary>
    /// Copies an existing file to a new file, allowing overwriting of an existing file.
    /// </summary>
    /// <param name="destinationFileName">The destination file.</param>
    /// <param name="overwrite">Whether to permit over-writing of an existing file.</param>
    public void CopyTo(string destinationFileName, bool overwrite)
    {
        FileSystem.CopyFile(FullPath, destinationFileName, overwrite);
    }

    /// <summary>
    /// Creates a new file for reading and writing.
    /// </summary>
    /// <returns>The newly created stream.</returns>
    public virtual Stream Create()
    {
        return Open(FileMode.Create);
    }

    /// <summary>
    /// Creates a new <see cref="StreamWriter"/> that writes a new text file.
    /// </summary>
    /// <returns>A new stream writer that can write to the file contents.</returns>
    public virtual StreamWriter CreateText()
    {
        return new StreamWriter(Open(FileMode.Create));
    }

    /// <summary>
    /// Moves a file to a new location.
    /// </summary>
    /// <param name="destinationFileName">The new name of the file.</param>
    public virtual void MoveTo(string destinationFileName)
    {
        FileSystem.MoveFile(FullPath, destinationFileName);
    }

    /// <summary>
    /// Opens the current file.
    /// </summary>
    /// <param name="mode">The file mode for the created stream.</param>
    /// <returns>The newly created stream.</returns>
    /// <remarks>Read-only file systems only support <c>FileMode.Open</c>.</remarks>
    public virtual Stream Open(FileMode mode)
    {
        return FileSystem.OpenFile(FullPath, mode);
    }

    /// <summary>
    /// Opens the current file.
    /// </summary>
    /// <param name="mode">The file mode for the created stream.</param>
    /// <param name="access">The access permissions for the created stream.</param>
    /// <returns>The newly created stream.</returns>
    /// <remarks>Read-only file systems only support <c>FileMode.Open</c> and <c>FileAccess.Read</c>.</remarks>
    public virtual Stream Open(FileMode mode, FileAccess access)
    {
        return FileSystem.OpenFile(FullPath, mode, access);
    }

    /// <summary>
    /// Opens an existing file for read-only access.
    /// </summary>
    /// <returns>The newly created stream.</returns>
    public Stream OpenRead()
    {
        return Open(FileMode.Open, FileAccess.Read);
    }

    /// <summary>
    /// Opens an existing file for reading as UTF-8 text.
    /// </summary>
    /// <returns>The newly created reader.</returns>
    public virtual StreamReader OpenText()
    {
        return new StreamReader(OpenRead());
    }

    /// <summary>
    /// Opens a file for writing.
    /// </summary>
    /// <returns>The newly created stream.</returns>
    public virtual Stream OpenWrite()
    {
        return Open(FileMode.Open, FileAccess.Write);
    }
}
