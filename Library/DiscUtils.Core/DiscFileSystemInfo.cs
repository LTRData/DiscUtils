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
using DiscUtils.Internal;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils;

/// <summary>
/// Provides the base class for both <see cref="DiscFileInfo"/> and <see cref="DiscDirectoryInfo"/> objects.
/// </summary>
public class DiscFileSystemInfo
{
    internal DiscFileSystemInfo(DiscFileSystem fileSystem, string path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var wrongPathSep = System.IO.Path.DirectorySeparatorChar == '\\' ? '/' : '\\';

        FileSystem = fileSystem;
        Path = path.Replace(wrongPathSep, System.IO.Path.DirectorySeparatorChar).Trim(System.IO.Path.DirectorySeparatorChar);
    }

    /// <summary>
    /// Gets or sets the <see cref="System.IO.FileAttributes"/> of the current <see cref="DiscFileSystemInfo"/> object.
    /// </summary>
    public virtual FileAttributes Attributes
    {
        get => FileSystem.GetAttributes(Path);
        set => FileSystem.SetAttributes(Path, value);
    }

    /// <summary>
    /// Gets or sets the creation time (in local time) of the current <see cref="DiscFileSystemInfo"/> object.
    /// </summary>
    public virtual DateTime CreationTime
    {
        get => CreationTimeUtc.ToLocalTime();
        set => CreationTimeUtc = value.ToUniversalTime();
    }

    /// <summary>
    /// Gets or sets the creation time (in UTC) of the current <see cref="DiscFileSystemInfo"/> object.
    /// </summary>
    public virtual DateTime CreationTimeUtc
    {
        get => FileSystem.GetCreationTimeUtc(Path);
        set => FileSystem.SetCreationTimeUtc(Path, value);
    }

    /// <summary>
    /// Gets a value indicating whether the file system object exists.
    /// </summary>
    public virtual bool Exists => FileSystem.Exists(Path);

    /// <summary>
    /// Gets the extension part of the file or directory name.
    /// </summary>
    public virtual string Extension
    {
        get
        {
            var name = Name;
            var sepIdx = name.LastIndexOf('.');
            if (sepIdx >= 0)
            {
                return name.Substring(sepIdx + 1);
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Gets the file system the referenced file or directory exists on.
    /// </summary>
    public DiscFileSystem FileSystem { get; }

    /// <summary>
    /// Gets the full path of the file or directory.
    /// </summary>
    public virtual string FullName => Path;

    /// <summary>
    /// Gets or sets the last time (in local time) the file or directory was accessed.
    /// </summary>
    /// <remarks>Read-only file systems will never update this value, it will remain at a fixed value.</remarks>
    public virtual DateTime LastAccessTime
    {
        get => LastAccessTimeUtc.ToLocalTime();
        set => LastAccessTimeUtc = value.ToUniversalTime();
    }

    /// <summary>
    /// Gets or sets the last time (in UTC) the file or directory was accessed.
    /// </summary>
    /// <remarks>Read-only file systems will never update this value, it will remain at a fixed value.</remarks>
    public virtual DateTime LastAccessTimeUtc
    {
        get => FileSystem.GetLastAccessTimeUtc(Path);
        set => FileSystem.SetLastAccessTimeUtc(Path, value);
    }

    /// <summary>
    /// Gets or sets the last time (in local time) the file or directory was written to.
    /// </summary>
    public virtual DateTime LastWriteTime
    {
        get => LastWriteTimeUtc.ToLocalTime();
        set => LastWriteTimeUtc = value.ToUniversalTime();
    }

    /// <summary>
    /// Gets or sets the last time (in UTC) the file or directory was written to.
    /// </summary>
    public virtual DateTime LastWriteTimeUtc
    {
        get => FileSystem.GetLastWriteTimeUtc(Path);
        set => FileSystem.SetLastWriteTimeUtc(Path, value);
    }

    /// <summary>
    /// Gets the name of the file or directory.
    /// </summary>
    public virtual string Name => Utilities.GetFileFromPath(Path);

    private DiscDirectoryInfo _parent;

    /// <summary>
    /// Gets the <see cref="DiscDirectoryInfo"/> of the directory containing the current <see cref="DiscFileSystemInfo"/> object.
    /// </summary>
    public virtual DiscDirectoryInfo Parent
    {
        get
        {
            if (string.IsNullOrEmpty(Path))
            {
                return null;
            }

            return _parent ??= new DiscDirectoryInfo(FileSystem, Utilities.GetDirectoryFromPath(Path));
        }
    }

    /// <summary>
    /// Gets the path to the referenced file.
    /// </summary>
    protected string Path { get; }

    /// <summary>
    /// Deletes a file or directory.
    /// </summary>
    public virtual void Delete()
    {
        if ((Attributes & FileAttributes.Directory) != 0)
        {
            FileSystem.DeleteDirectory(Path);
        }
        else
        {
            FileSystem.DeleteFile(Path);
        }
    }

    /// <summary>
    /// Indicates if <paramref name="obj"/> is equivalent to this object.
    /// </summary>
    /// <param name="obj">The object to compare.</param>
    /// <returns><c>true</c> if <paramref name="obj"/> is equivalent, else <c>false</c>.</returns>
    public override bool Equals(object obj)
    {
        var asInfo = obj as DiscFileSystemInfo;
        if (obj == null)
        {
            return false;
        }

        return Path == asInfo.Path &&
            Equals(FileSystem, asInfo.FileSystem);
    }

    /// <summary>
    /// Gets the hash code for this object.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
        => HashCode.Combine(Path, FileSystem);

    public override string ToString()
        => FullName;
}