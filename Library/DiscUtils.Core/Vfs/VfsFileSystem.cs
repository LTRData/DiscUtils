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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using DiscUtils.Internal;
using DiscUtils.Streams;

namespace DiscUtils.Vfs;

/// <summary>
/// Base class for VFS file systems.
/// </summary>
/// <typeparam name="TDirEntry">The concrete type representing directory entries.</typeparam>
/// <typeparam name="TFile">The concrete type representing files.</typeparam>
/// <typeparam name="TDirectory">The concrete type representing directories.</typeparam>
/// <typeparam name="TContext">The concrete type holding global state.</typeparam>
public abstract class VfsFileSystem<TDirEntry, TFile, TDirectory, TContext> : DiscFileSystem
    where TDirEntry : VfsDirEntry
    where TFile : class, IVfsFile
    where TDirectory : class, IVfsDirectory<TDirEntry, TFile>, TFile
    where TContext : VfsContext
{
    public abstract bool IsCaseSensitive { get; }

    private readonly ObjectCache<long, TFile> _fileCache;

    /// <summary>
    /// Initializes a new instance of the VfsFileSystem class.
    /// </summary>
    /// <param name="defaultOptions">The default file system options.</param>
    protected VfsFileSystem(DiscFileSystemOptions defaultOptions)
        : base(defaultOptions)
    {
        _fileCache = new ObjectCache<long, TFile>();
    }

    /// <summary>
    /// Gets or sets the global shared state.
    /// </summary>
    protected TContext Context { get; set; }

    /// <summary>
    /// Gets or sets the object representing the root directory.
    /// </summary>
    protected TDirectory RootDirectory { get; set; }

    /// <summary>
    /// Gets the volume label.
    /// </summary>
    public abstract override string VolumeLabel { get; }

    /// <summary>
    /// Copies a file - not supported on read-only file systems.
    /// </summary>
    /// <param name="sourceFile">The source file.</param>
    /// <param name="destinationFile">The destination file.</param>
    /// <param name="overwrite">Whether to permit over-writing of an existing file.</param>
    public override void CopyFile(string sourceFile, string destinationFile, bool overwrite)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Creates a directory - not supported on read-only file systems.
    /// </summary>
    /// <param name="path">The path of the new directory.</param>
    public override void CreateDirectory(string path)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Deletes a directory - not supported on read-only file systems.
    /// </summary>
    /// <param name="path">The path of the directory to delete.</param>
    public override void DeleteDirectory(string path)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Deletes a file - not supported on read-only file systems.
    /// </summary>
    /// <param name="path">The path of the file to delete.</param>
    public override void DeleteFile(string path)
    {
        throw new NotImplementedException();
    }

    public override DiscFileSystemInfo GetFileSystemInfo(string path)
    {
        if (IsRoot(path) && RootDirectory != null)
        {
            return Root;
        }

        var dirEntry = GetDirectoryEntry(path);

        if (dirEntry != null && dirEntry.IsSymlink)
        {
            dirEntry = ResolveSymlink(dirEntry, path);
        }

        if (dirEntry == null)
        {
            return new(this, path);
        }

        var file = GetFile(dirEntry);

        if (file == null)
        {
            return new(this, path);
        }

        var attributes = file.FileAttributes;
        var creationTimeUtc = file.CreationTimeUtc;
        var lastAccessTimeUtc = file.LastAccessTimeUtc;
        var lastWriteTimeUtc = file.LastWriteTimeUtc;

        if (attributes.HasFlag(FileAttributes.Directory))
        {
            return new CachedDiscDirectoryInfo(this, path, attributes, creationTimeUtc, lastAccessTimeUtc, lastWriteTimeUtc);
        }
        else
        {
            return new CachedDiscFileInfo(this, path, attributes, creationTimeUtc, lastAccessTimeUtc, lastWriteTimeUtc, file.FileLength);
        }
    }

    public override DiscDirectoryInfo GetDirectoryInfo(string path)
    {
        if (IsRoot(path) && RootDirectory != null)
        {
            return Root;
        }

        var dirEntry = GetDirectoryEntry(path);

        if (dirEntry != null && dirEntry.IsSymlink)
        {
            dirEntry = ResolveSymlink(dirEntry, path);
        }

        if (dirEntry == null)
        {
            return new(this, path);
        }

        var file = GetFile(dirEntry);

        if (file == null)
        {
            return new(this, path);
        }

        var attributes = file.FileAttributes;
        var creationTimeUtc = file.CreationTimeUtc;
        var lastAccessTimeUtc = file.LastAccessTimeUtc;
        var lastWriteTimeUtc = file.LastWriteTimeUtc;

        if (attributes.HasFlag(FileAttributes.Directory))
        {
            return new CachedDiscDirectoryInfo(this, path, attributes, creationTimeUtc, lastAccessTimeUtc, lastWriteTimeUtc);
        }
        else
        {
            return new(this, path);
        }
    }

    public override DiscFileInfo GetFileInfo(string path)
    {
        if (IsRoot(path) && RootDirectory != null)
        {
            return new CachedDiscFileInfo(this, path);
        }

        var dirEntry = GetDirectoryEntry(path);

        if (dirEntry != null && dirEntry.IsSymlink)
        {
            dirEntry = ResolveSymlink(dirEntry, path);
        }

        if (dirEntry == null)
        {
            return new(this, path);
        }

        var file = GetFile(dirEntry);

        if (file == null)
        {
            return new(this, path);
        }

        var attributes = file.FileAttributes;
        var creationTimeUtc = file.CreationTimeUtc;
        var lastAccessTimeUtc = file.LastAccessTimeUtc;
        var lastWriteTimeUtc = file.LastWriteTimeUtc;

        if (attributes.HasFlag(FileAttributes.Directory))
        {
            return new(this, path);
        }
        else
        {
            return new CachedDiscFileInfo(this, path, attributes, creationTimeUtc, lastAccessTimeUtc, lastWriteTimeUtc, file.FileLength);
        }
    }

    /// <summary>
    /// Indicates if a directory exists.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>true if the directory exists.</returns>
    public override bool DirectoryExists(string path)
    {
        if (IsRoot(path) && RootDirectory != null)
        {
            return true;
        }

        var dirEntry = GetDirectoryEntry(path);

        if (dirEntry != null && dirEntry.IsSymlink)
        {
            dirEntry = ResolveSymlink(dirEntry, path);
        }

        if (dirEntry != null)
        {
            return dirEntry.IsDirectory;
        }

        return false;
    }

    /// <summary>
    /// Indicates if a file exists.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>true if the file exists.</returns>
    public override bool FileExists(string path)
    {
        if (IsRoot(path) && RootDirectory != null)
        {
            return false;
        }

        var dirEntry = GetDirectoryEntry(path);

        if (dirEntry != null && dirEntry.IsSymlink)
        {
            dirEntry = ResolveSymlink(dirEntry, path);
        }

        if (dirEntry != null)
        {
            return !dirEntry.IsDirectory;
        }

        return false;
    }

    /// <summary>
    /// Indicates if a file or directory exists.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>true if the file exists.</returns>
    public override bool Exists(string path)
    {
        if (IsRoot(path) && RootDirectory != null)
        {
            return true;
        }

        var dirEntry = GetDirectoryEntry(path);

        if (dirEntry != null && dirEntry.IsSymlink)
        {
            dirEntry = ResolveSymlink(dirEntry, path);
        }

        if (dirEntry != null)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets the names of subdirectories in a specified directory matching a specified
    /// search pattern, using a value to determine whether to search subdirectories.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <param name="searchPattern">The search string to match against.</param>
    /// <param name="searchOption">Indicates whether to search subdirectories.</param>
    /// <returns>Array of directories matching the search pattern.</returns>
    public override IEnumerable<string> GetDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        var dirs = DoSearch(path, searchPattern, searchOption == SearchOption.AllDirectories, true, false);
        return dirs;
    }

    /// <summary>
    /// Gets the names of files in a specified directory matching a specified
    /// search pattern, using a value to determine whether to search subdirectories.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <param name="searchPattern">The search string to match against.</param>
    /// <param name="searchOption">Indicates whether to search subdirectories.</param>
    /// <returns>Array of files matching the search pattern.</returns>
    public override IEnumerable<string> GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var results = DoSearch(path, searchPattern, searchOption == SearchOption.AllDirectories, false, true);
        return results;
    }

    /// <summary>
    /// Gets the names of all files and subdirectories in a specified directory.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <returns>Array of files and subdirectories matching the search pattern.</returns>
    public override IEnumerable<string> GetFileSystemEntries(string path)
    {
        var fullPath = path;
        if (fullPath.Length == 0 || (fullPath[0] != '\\' && fullPath[0] != '/'))
        {
            fullPath = Path.DirectorySeparatorChar + fullPath;
        }

        var parentDir = GetDirectory(fullPath);
        return parentDir.AllEntries.Values
            .Select(m => Utilities.CombinePaths(fullPath, FormatFileName(m.FileName)));
    }

    /// <summary>
    /// Gets the names of files and subdirectories in a specified directory matching a specified
    /// search pattern.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <param name="searchPattern">The search string to match against.</param>
    /// <returns>Array of files and subdirectories matching the search pattern.</returns>
    public override IEnumerable<string> GetFileSystemEntries(string path, string searchPattern)
    {
        var filter = Utilities.ConvertWildcardsToRegEx(searchPattern, !IsCaseSensitive);

        var parentDir = GetDirectory(path);

        var result = parentDir.AllEntries.Values
            .Where(dirEntry => filter is null || filter(dirEntry.SearchName))
            .Select(dirEntry => Utilities.CombinePaths(path, dirEntry.FileName));

        return result;
    }

    /// <summary>
    /// Moves a directory.
    /// </summary>
    /// <param name="sourceDirectoryName">The directory to move.</param>
    /// <param name="destinationDirectoryName">The target directory name.</param>
    public override void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Moves a file.
    /// </summary>
    /// <param name="sourceName">The file to move.</param>
    /// <param name="destinationName">The target file name.</param>
    /// <param name="overwrite">Overwrite any existing file.</param>
    public override void MoveFile(string sourceName, string destinationName, bool overwrite)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Opens the specified file.
    /// </summary>
    /// <param name="path">The full path of the file to open.</param>
    /// <param name="mode">The file mode for the created stream.</param>
    /// <param name="access">The access permissions for the created stream.</param>
    /// <returns>The new stream.</returns>
    public override SparseStream OpenFile(string path, FileMode mode, FileAccess access)
    {
        if (!CanWrite)
        {
            if (mode != FileMode.Open)
            {
                throw new NotSupportedException("Only existing files can be opened");
            }

            if (access != FileAccess.Read)
            {
                throw new NotSupportedException("Files cannot be opened for write");
            }
        }

        var fileName = Utilities.GetFileFromPath(path);

        string dirName;
        try
        {
            dirName = Utilities.GetDirectoryFromPath(path);
        }
        catch (ArgumentException)
        {
            throw new IOException($"Invalid path: {path}");
        }

        var entryPath = Utilities.CombinePaths(dirName, fileName);
        var entry = GetDirectoryEntry(entryPath);
        if (entry == null)
        {
            if (mode == FileMode.Open)
            {
                throw new FileNotFoundException("No such file", path);
            }

            var parentDir = GetDirectory(Utilities.GetDirectoryFromPath(path));
            entry = parentDir.CreateNewFile(Utilities.GetFileFromPath(path));
        }
        else if (mode == FileMode.CreateNew)
        {
            throw new IOException("File already exists");
        }

        if (entry.IsSymlink)
        {
            entry = ResolveSymlink(entry, entryPath) ??
                throw new FileNotFoundException("Unable to resolve symlink", entryPath);
        }

        if (entry.IsDirectory)
        {
            throw new IOException("Attempt to open directory as a file");
        }

        var file = GetFile(entry);

        string attributeName = null;

        var streamSepPos = fileName.IndexOf(':');
        if (streamSepPos >= 0)
        {
            attributeName = fileName.Substring(streamSepPos + 1);
        }

        SparseStream stream;

        if (file is IVfsFileWithStreams fileStreams && attributeName != null)
        {
            stream = fileStreams.OpenExistingStream(attributeName);
            if (stream == null)
            {
                if (mode is FileMode.Create or FileMode.OpenOrCreate)
                {
                    stream = fileStreams.CreateStream(attributeName);
                }
                else
                {
                    throw new FileNotFoundException("No such attribute on file", path);
                }
            }
        }
        else
        {
            stream = new BufferStream(file.FileContent, access);
        }

        if (mode is FileMode.Create or FileMode.Truncate)
        {
            stream.SetLength(0);
        }

        return stream;
    }

    /// <summary>
    /// Gets the attributes of a file or directory.
    /// </summary>
    /// <param name="path">The file or directory to inspect.</param>
    /// <returns>The attributes of the file or directory.</returns>
    public override FileAttributes GetAttributes(string path)
    {
        if (IsRoot(path))
        {
            return RootDirectory.FileAttributes;
        }

        var dirEntry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("File not found", path);

        if (dirEntry.HasVfsFileAttributes)
        {
            return dirEntry.FileAttributes;
        }

        return GetFile(dirEntry).FileAttributes;
    }

    /// <summary>
    /// Sets the attributes of a file or directory.
    /// </summary>
    /// <param name="path">The file or directory to change.</param>
    /// <param name="newValue">The new attributes of the file or directory.</param>
    public override void SetAttributes(string path, FileAttributes newValue)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the creation time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The creation time.</returns>
    public override DateTime GetCreationTimeUtc(string path)
    {
        if (IsRoot(path))
        {
            return RootDirectory.CreationTimeUtc;
        }

        var dirEntry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("No such file or directory", path);

        if (dirEntry.HasVfsTimeInfo)
        {
            return dirEntry.CreationTimeUtc;
        }

        return GetFile(dirEntry).CreationTimeUtc;
    }

    /// <summary>
    /// Sets the creation time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetCreationTimeUtc(string path, DateTime newTime)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the last access time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The last access time.</returns>
    public override DateTime GetLastAccessTimeUtc(string path)
    {
        if (IsRoot(path))
        {
            return RootDirectory.LastAccessTimeUtc;
        }

        var dirEntry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("No such file or directory", path);

        if (dirEntry.HasVfsTimeInfo)
        {
            return dirEntry.LastAccessTimeUtc;
        }

        return GetFile(dirEntry).LastAccessTimeUtc;
    }

    /// <summary>
    /// Sets the last access time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetLastAccessTimeUtc(string path, DateTime newTime)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the last modification time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The last write time.</returns>
    public override DateTime GetLastWriteTimeUtc(string path)
    {
        if (IsRoot(path))
        {
            return RootDirectory.LastWriteTimeUtc;
        }

        var dirEntry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("No such file or directory", path);

        if (dirEntry.HasVfsTimeInfo)
        {
            return dirEntry.LastWriteTimeUtc;
        }

        return GetFile(dirEntry).LastWriteTimeUtc;
    }

    /// <summary>
    /// Sets the last modification time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetLastWriteTimeUtc(string path, DateTime newTime)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Gets the length of a file.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <returns>The length in bytes.</returns>
    public override long GetFileLength(string path)
    {
        var file = GetFile(path);
        if (file == null || (file.FileAttributes & FileAttributes.Directory) != 0)
        {
            throw new FileNotFoundException("No such file", path);
        }

        return file.FileLength;
    }

    protected TFile GetFile(TDirEntry dirEntry)
    {
        var cacheKey = dirEntry.UniqueCacheId;

        var file = _fileCache[cacheKey];
        if (file == null)
        {
            file = ConvertDirEntryToFile(dirEntry);
            _fileCache[cacheKey] = file;
        }

        return file;
    }

    protected TDirectory GetDirectory(string path)
    {
        if (IsRoot(path))
        {
            return RootDirectory;
        }

        var dirEntry = GetDirectoryEntry(path);

        if (dirEntry != null && dirEntry.IsSymlink)
        {
            dirEntry = ResolveSymlink(dirEntry, path);
        }

        if (dirEntry == null || !dirEntry.IsDirectory)
        {
            throw new DirectoryNotFoundException($"No such directory: {path}");
        }

        return (TDirectory)GetFile(dirEntry);
    }

    protected virtual TDirEntry GetDirectoryEntry(string path)
    {
        return GetDirectoryEntry(RootDirectory, path);
    }

    /// <summary>
    /// Gets all directory entries in the specified directory and sub-directories.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    /// <param name="handler">Delegate invoked for each directory entry.</param>
    protected void ForAllDirEntries(string path, DirEntryHandler handler)
    {
        TDirectory dir = null;
        var self = GetDirectoryEntry(path);

        if (self != null)
        {
            handler(path, self);
            if (self.IsDirectory)
            {
                dir = GetFile(self) as TDirectory;
            }
        }
        else
        {
            dir = GetFile(path) as TDirectory;
        }

        if (dir != null)
        {
            foreach (var subentry in dir.AllEntries.Values)
            {
                ForAllDirEntries(Utilities.CombinePaths(path, subentry.FileName), handler);
            }
        }
    }

    /// <summary>
    /// Gets the file object for a given path.
    /// </summary>
    /// <param name="path">The path to query.</param>
    /// <returns>The file object corresponding to the path.</returns>
    protected TFile GetFile(string path)
    {
        if (IsRoot(path))
        {
            return RootDirectory;
        }

        if (path == null)
        {
            return default(TFile);
        }

        var dirEntry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("No such file or directory", path);

        if (dirEntry != null && dirEntry.IsSymlink)
        {
            dirEntry = ResolveSymlink(dirEntry, path);
        }

        return GetFile(dirEntry);
    }

    /// <summary>
    /// Converts a directory entry to an object representing a file.
    /// </summary>
    /// <param name="dirEntry">The directory entry to convert.</param>
    /// <returns>The corresponding file object.</returns>
    protected abstract TFile ConvertDirEntryToFile(TDirEntry dirEntry);

    /// <summary>
    /// Converts an internal directory entry name into an external one.
    /// </summary>
    /// <param name="name">The name to convert.</param>
    /// <returns>The external name.</returns>
    /// <remarks>
    /// This method is called on a single path element (i.e. name contains no path
    /// separators).
    /// </remarks>
    protected virtual string FormatFileName(string name)
    {
        return name;
    }

    protected static bool IsRoot(string path)
    {
        return string.IsNullOrEmpty(path) || path == @"\" || path == "/";
    }

    protected TDirEntry GetDirectoryEntry(TDirectory dir, string path)
    {
        var pathElements = path.Split(Utilities.PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        return GetDirectoryEntry(dir, pathElements, 0);
    }

    private TDirEntry GetDirectoryEntry(TDirectory dir, string[] pathEntries, int pathOffset)
    {
        TDirEntry entry;

        if (pathEntries.Length == 0)
        {
            return dir?.Self;
        }

        entry = dir?.GetEntryByName(pathEntries[pathOffset]);
        if (entry != null)
        {
            if (pathOffset == pathEntries.Length - 1)
            {
                return entry;
            }

            if (entry.IsSymlink)
            {
                entry = ResolveSymlink(entry, pathEntries[pathOffset]);

                if (entry == null)
                {
                    return null;
                }
            }

            if (entry.IsDirectory)
            {
                return GetDirectoryEntry((TDirectory)GetFile(entry), pathEntries, pathOffset + 1);
            }

            throw new IOException($"{pathEntries[pathOffset]} is a file, not a directory");
        }

        return null;
    }

    private IEnumerable<string> DoSearch(string path, string searchPattern, bool subFolders, bool dirs, bool files)
    {
        var parentDir = GetDirectory(path)
            ?? throw new DirectoryNotFoundException($"The directory '{path}' was not found");

        var resultPrefixPath = path;
        if (IsRoot(path))
        {
            resultPrefixPath = Utilities.DirectorySeparatorString;
        }

        if (parentDir.AllEntries.TryGetValue(searchPattern, out var entry))
        {
            var isDir = entry.IsDirectory;

            if ((isDir && dirs) || (!isDir && files))
            {
                yield return Utilities.CombinePaths(resultPrefixPath, FormatFileName(entry.FileName));
            }

            yield break;
        }

        var filter = Utilities.ConvertWildcardsToRegEx(searchPattern, !IsCaseSensitive);

        foreach (var e in DoSearch(path, filter, subFolders, dirs, files))
        {
            yield return e;
        }
    }

    private IEnumerable<string> DoSearch(string path, Func<string, bool> filter, bool subFolders, bool dirs, bool files)
    {
        var parentDir = GetDirectory(path)
            ?? throw new DirectoryNotFoundException($"The directory '{path}' was not found");

        var resultPrefixPath = path;
        if (IsRoot(path))
        {
            resultPrefixPath = Utilities.DirectorySeparatorString;
        }

        foreach (var de in parentDir.AllEntries.Values)
        {
            var entry = de;

            if (entry.IsSymlink)
            {
                entry = ResolveSymlink(entry, $@"{path}\{entry.FileName}");

                if (entry == null)
                {
                    continue;
                }
            }

            if(entry == null)
            {
                continue;
            }

            var isDir = entry.IsDirectory;

            if ((isDir && dirs) || (!isDir && files))
            {
                if (filter is null || filter(de.SearchName))
                {
                    yield return Utilities.CombinePaths(resultPrefixPath, FormatFileName(entry.FileName));
                }
            }

            if (subFolders && isDir)
            {
                foreach (var subdirentry in DoSearch(Utilities.CombinePaths(resultPrefixPath, FormatFileName(entry.FileName)), filter,
                    subFolders, dirs, files))
                {
                    yield return subdirentry;
                }
            }
        }
    }

    protected virtual TDirEntry ResolveSymlink(TDirEntry entry, string path)
    {
        var currentEntry = entry;
        if (path.Length > 0 && path[0] != '\\' && path[0] != '/')
        {
            path = Path.DirectorySeparatorChar + path;
        }

        var currentPath = path;
        var resolvesLeft = 20;
        while (currentEntry.IsSymlink && resolvesLeft > 0)
        {
            if (GetFile(currentEntry) is not IVfsSymlink<TDirEntry, TFile> symlink)
            {
                Trace.WriteLine($"Unable to resolve symlink '{path}'");
                return null;
            }

            currentPath = Utilities.ResolvePath(currentPath.TrimEnd(Utilities.PathSeparators), symlink.TargetPath);
            currentEntry = GetDirectoryEntry(currentPath);

            if (currentEntry == null)
            {
                Trace.WriteLine($"Unable to resolve symlink '{path}' to '{symlink.TargetPath}'");
                return null;
            }

            --resolvesLeft;
        }

        if (currentEntry != null && currentEntry.IsSymlink)
        {
            Trace.WriteLine($"Unable to resolve symlink - too many links '{path}'");
            return null;
        }

        return currentEntry;
    }

    /// <summary>
    /// Delegate for processing directory entries.
    /// </summary>
    /// <param name="path">Full path to the directory entry.</param>
    /// <param name="dirEntry">The directory entry itself.</param>
    protected delegate void DirEntryHandler(string path, TDirEntry dirEntry);
}
