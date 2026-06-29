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

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiscUtils;

/// <summary>
/// Provides information about a directory on a disc.
/// </summary>
/// <remarks>
/// This class allows navigation of the disc directory/file hierarchy.
/// </remarks>
public class DiscDirectoryInfo : DiscFileSystemInfo
{
    /// <summary>
    /// Initializes a new instance of the DiscDirectoryInfo class.
    /// </summary>
    /// <param name="fileSystem">The file system the directory info relates to.</param>
    /// <param name="path">The path within the file system of the directory.</param>
    internal DiscDirectoryInfo(DiscFileSystem fileSystem, string path)
        : base(fileSystem, path) { }

    /// <summary>
    /// Gets a value indicating whether the directory exists.
    /// </summary>
    public override bool Exists => FileSystem.DirectoryExists(FullPath);

    /// <summary>
    /// Gets the full path of the directory.
    /// </summary>
    public override string FullName => base.FullName + System.IO.Path.DirectorySeparatorChar;

    /// <summary>
    /// Creates a directory.
    /// </summary>
    public virtual void Create()
    {
        FileSystem.CreateDirectory(FullPath);
    }

    /// <summary>
    /// Deletes a directory, even if it's not empty.
    /// </summary>
    public override void Delete()
    {
        FileSystem.DeleteDirectory(FullPath, false);
    }

    /// <summary>
    /// Deletes a directory, with the caller choosing whether to recurse.
    /// </summary>
    /// <param name="recursive"><c>true</c> to delete all child node, <c>false</c> to fail if the directory is not empty.</param>
    public void Delete(bool recursive)
    {
        FileSystem.DeleteDirectory(FullPath, recursive);
    }

    /// <summary>
    /// Moves a directory and it's contents to a new path.
    /// </summary>
    /// <param name="destinationDirName">The destination directory name.</param>
    public void MoveTo(string destinationDirName)
    {
        FileSystem.MoveDirectory(FullPath, destinationDirName);
    }

    /// <summary>
    /// Gets all child directories.
    /// </summary>
    /// <returns>An array of child directories.</returns>
    public IEnumerable<DiscDirectoryInfo> GetDirectories()
    {
        return FileSystem.GetDirectories(FullPath)
            .Select(p => new DiscDirectoryInfo(FileSystem, p));
    }

    /// <summary>
    /// Gets all child directories matching a search pattern.
    /// </summary>
    /// <param name="pattern">The search pattern.</param>
    /// <returns>An array of child directories, or empty if none match.</returns>
    /// <remarks>The search pattern can include the wildcards * (matching 0 or more characters)
    /// and ? (matching 1 character).</remarks>
    public IEnumerable<DiscDirectoryInfo> GetDirectories(string pattern)
    {
        return GetDirectories(pattern, SearchOption.TopDirectoryOnly);
    }

    /// <summary>
    /// Gets all descendant directories matching a search pattern.
    /// </summary>
    /// <param name="pattern">The search pattern.</param>
    /// <param name="searchOption">Whether to search just this directory, or all children.</param>
    /// <returns>An array of descendant directories, or empty if none match.</returns>
    /// <remarks>The search pattern can include the wildcards * (matching 0 or more characters)
    /// and ? (matching 1 character).  The option parameter determines whether only immediate
    /// children, or all children are returned.</remarks>
    public IEnumerable<DiscDirectoryInfo> GetDirectories(string pattern, SearchOption searchOption)
    {
        return FileSystem.GetDirectories(FullPath, pattern, searchOption)
            .Select(p => new DiscDirectoryInfo(FileSystem, p));
    }

    /// <summary>
    /// Gets an object representing a possible subdirectory.
    /// </summary>
    /// <param name="name">The subdirectory name.</param>
    /// <returns>The representing object.</returns>
    /// <remarks>The subdirectory does not need to exist.</remarks>
    public DiscDirectoryInfo GetDirectory(string name)
    {
        return FileSystem.GetDirectoryInfo(Path.Combine(FullPath, name));
    }

    /// <summary>
    /// Gets all files.
    /// </summary>
    /// <returns>An array of files.</returns>
    public IEnumerable<DiscFileInfo> GetFiles()
    {
        return FileSystem.GetFiles(FullPath).Select(p => new DiscFileInfo(FileSystem, p));
    }

    /// <summary>
    /// Gets all files matching a search pattern.
    /// </summary>
    /// <param name="pattern">The search pattern.</param>
    /// <returns>An array of files, or empty if none match.</returns>
    /// <remarks>The search pattern can include the wildcards * (matching 0 or more characters)
    /// and ? (matching 1 character).</remarks>
    public IEnumerable<DiscFileInfo> GetFiles(string pattern)
    {
        return GetFiles(pattern, SearchOption.TopDirectoryOnly);
    }

    /// <summary>
    /// Gets all descendant files matching a search pattern.
    /// </summary>
    /// <param name="pattern">The search pattern.</param>
    /// <param name="searchOption">Whether to search just this directory, or all children.</param>
    /// <returns>An array of descendant files, or empty if none match.</returns>
    /// <remarks>The search pattern can include the wildcards * (matching 0 or more characters)
    /// and ? (matching 1 character).  The option parameter determines whether only immediate
    /// children, or all children are returned.</remarks>
    public IEnumerable<DiscFileInfo> GetFiles(string pattern, SearchOption searchOption)
    {
        return FileSystem.GetFiles(FullPath, pattern, searchOption)
            .Select(p => new DiscFileInfo(FileSystem, p));
    }

    /// <summary>
    /// Gets an object representing a possible file.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns>The representing object.</returns>
    /// <remarks>The file does not need to exist.</remarks>
    public DiscFileInfo GetFile(string name)
    {
        return FileSystem.GetFileInfo(Path.Combine(FullPath, name));
    }

    /// <summary>
    /// Gets all files and directories in this directory.
    /// </summary>
    /// <returns>An array of files and directories.</returns>
    public IEnumerable<DiscFileSystemInfo> GetFileSystemInfos()
    {
        return FileSystem.GetFileSystemEntries(FullPath)
            .Select(p => new DiscFileSystemInfo(FileSystem, p));
    }

    /// <summary>
    /// Gets all files and directories in this directory.
    /// </summary>
    /// <param name="pattern">The search pattern.</param>
    /// <returns>An array of files and directories.</returns>
    /// <remarks>The search pattern can include the wildcards * (matching 0 or more characters)
    /// and ? (matching 1 character).</remarks>
    public IEnumerable<DiscFileSystemInfo> GetFileSystemInfos(string pattern)
    {
        return FileSystem.GetFileSystemEntries(FullPath, pattern)
            .Select(p => new DiscFileSystemInfo(FileSystem, p));
    }

    /// <summary>
    /// Gets an object representing a possible file system object (file or directory).
    /// </summary>
    /// <param name="name">The file or subdirectory name.</param>
    /// <returns>The representing object.</returns>
    /// <remarks>The file system object does not need to exist.</remarks>
    public DiscFileSystemInfo GetFileSystemInfo(string name)
    {
        return FileSystem.GetFileSystemInfo(Path.Combine(FullPath, name));
    }
}