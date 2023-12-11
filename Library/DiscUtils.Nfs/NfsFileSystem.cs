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
using System.IO;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Buffers;

namespace DiscUtils.Nfs;

/// <summary>
/// A file system backed by an NFS server.
/// </summary>
/// <remarks>NFS is a common storage protocol for Virtual Machines.  Currently, only NFS v3 is supported.</remarks>
public class NfsFileSystem : DiscFileSystem
{
    private Nfs3Client _client;

    /// <summary>
    /// Initializes a new instance of the NfsFileSystem class.
    /// </summary>
    /// <param name="address">The address of the NFS server (IP or DNS address).</param>
    /// <param name="mountPoint">The mount point on the server to root the file system.</param>
    /// <remarks>
    /// The created instance uses default credentials.
    /// </remarks>
    public NfsFileSystem(string address, string mountPoint)
        : base(new NfsFileSystemOptions())
    {
        _client = new Nfs3Client(address, RpcUnixCredential.Default, mountPoint);
    }

    /// <summary>
    /// Initializes a new instance of the NfsFileSystem class.
    /// </summary>
    /// <param name="address">The address of the NFS server (IP or DNS address).</param>
    /// <param name="credentials">The credentials to use when accessing the NFS server.</param>
    /// <param name="mountPoint">The mount point on the server to root the file system.</param>
    public NfsFileSystem(string address, RpcCredentials credentials, string mountPoint)
        : base(new NfsFileSystemOptions())
    {
        _client = new Nfs3Client(address, credentials, mountPoint);
    }

    /// <summary>
    /// Gets whether this file system supports modification (true for NFS).
    /// </summary>
    public override bool CanWrite
    {
        get { return true; }
    }

    /// <summary>
    /// Gets the friendly name for this file system (NFS).
    /// </summary>
    public override string FriendlyName
    {
        get { return "NFS"; }
    }

    /// <summary>
    /// Gets the options controlling this instance.
    /// </summary>
    public NfsFileSystemOptions NfsOptions
    {
        get { return (NfsFileSystemOptions)Options; }
    }

    /// <summary>
    /// Gets the preferred NFS read size.
    /// </summary>
    public int PreferredReadSize
    {
        get { return _client == null ? 0 : (int)_client.FileSystemInfo.ReadPreferredBytes; }
    }

    /// <summary>
    /// Gets the preferred NFS write size.
    /// </summary>
    public int PreferredWriteSize
    {
        get { return _client == null ? 0 : (int)_client.FileSystemInfo.WritePreferredBytes; }
    }

    /// <summary>
    /// Gets the folders exported by a server.
    /// </summary>
    /// <param name="address">The address of the server.</param>
    /// <returns>An enumeration of exported folders.</returns>
    public static IEnumerable<string> GetExports(string address)
    {
        using var rpcClient = new RpcClient(address, null);
        var mountClient = new Nfs3Mount(rpcClient);
        foreach (var export in mountClient.Exports())
        {
            yield return export.DirPath;
        }
    }

    /// <summary>
    /// Copies a file from one location to another.
    /// </summary>
    /// <param name="sourceFile">The source file to copy.</param>
    /// <param name="destinationFile">The destination path.</param>
    /// <param name="overwrite">Whether to overwrite any existing file (true), or fail if such a file exists.</param>
    public override void CopyFile(string sourceFile, string destinationFile, bool overwrite)
    {
        try
        {
            var sourceParent = GetParentDirectory(sourceFile);
            var destParent = GetParentDirectory(destinationFile);

            var sourceFileName = Utilities.GetFileFromPath(sourceFile);
            var destFileName = Utilities.GetFileFromPath(destinationFile);

            var sourceFileHandle = _client.Lookup(sourceParent, sourceFileName);
            if (sourceFileHandle == null)
            {
                throw new FileNotFoundException(
                    $"The file '{sourceFile}' does not exist",
                    sourceFile);
            }

            var sourceAttrs = _client.GetAttributes(sourceFileHandle);
            if ((sourceAttrs.Type & Nfs3FileType.Directory) != 0)
            {
                throw new FileNotFoundException(
                    $"The path '{sourceFile}' is not a file",
                    sourceFile);
            }

            var destFileHandle = _client.Lookup(destParent, destFileName);
            if (destFileHandle != null)
            {
                if (overwrite == false)
                {
                    throw new IOException($"The destination file '{destinationFile}' already exists");
                }
            }

            // Create the file, with temporary permissions
            var setAttrs = new Nfs3SetAttributes
            {
                Mode = UnixFilePermissions.OwnerRead | UnixFilePermissions.OwnerWrite,
                SetMode = true,
                Size = sourceAttrs.Size,
                SetSize = true
            };
            destFileHandle = _client.Create(destParent, destFileName, !overwrite, setAttrs);

            // Copy the file contents
            using (var sourceFs = new Nfs3FileStream(_client, sourceFileHandle, FileAccess.Read))
            using (var destFs = new Nfs3FileStream(_client, destFileHandle, FileAccess.Write))
            {
                var bufferSize =
                    (int)
                    Math.Max(1 * Sizes.OneMiB,
                        Math.Min(_client.FileSystemInfo.WritePreferredBytes,
                            _client.FileSystemInfo.ReadPreferredBytes));
                var buffer = new byte[bufferSize];

                var numRead = sourceFs.Read(buffer, 0, bufferSize);
                while (numRead > 0)
                {
                    destFs.Write(buffer, 0, numRead);
                    numRead = sourceFs.Read(buffer, 0, bufferSize);
                }
            }

            // Set the new file's attributes based on the source file
            setAttrs = new Nfs3SetAttributes
            {
                Mode = sourceAttrs.Mode,
                SetMode = true,
                AccessTime = sourceAttrs.AccessTime,
                SetAccessTime = Nfs3SetTimeMethod.ClientTime,
                ModifyTime = sourceAttrs.ModifyTime,
                SetModifyTime = Nfs3SetTimeMethod.ClientTime,
                Gid = sourceAttrs.Gid,
                SetGid = true
            };
            _client.SetAttributes(destFileHandle, setAttrs);
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Creates a directory at the specified path.
    /// </summary>
    /// <param name="path">The path of the directory to create.</param>
    public override void CreateDirectory(string path)
    {
        try
        {
            var parent = GetParentDirectory(path);

            var setAttrs = new Nfs3SetAttributes
            {
                Mode = NfsOptions.NewDirectoryPermissions,
                SetMode = true
            };

            _client.MakeDirectory(parent, Utilities.GetFileFromPath(path), setAttrs);
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Deletes a directory at the specified path.
    /// </summary>
    /// <param name="path">The directory to delete.</param>
    public override void DeleteDirectory(string path)
    {
        try
        {
            var handle = GetFile(path);
            if (handle != null && _client.GetAttributes(handle).Type != Nfs3FileType.Directory)
            {
                throw new DirectoryNotFoundException($"No such directory: {path}");
            }

            var parent = GetParentDirectory(path);
            if (handle != null)
            {
                _client.RemoveDirectory(parent, Utilities.GetFileFromPath(path));
            }
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Deletes a file at the specified path.
    /// </summary>
    /// <param name="path">The path of the file to delete.</param>
    public override void DeleteFile(string path)
    {
        try
        {
            var handle = GetFile(path);
            if (handle != null && _client.GetAttributes(handle).Type == Nfs3FileType.Directory)
            {
                throw new FileNotFoundException("No such file", path);
            }

            var parent = GetParentDirectory(path);
            if (handle != null)
            {
                _client.Remove(parent, Utilities.GetFileFromPath(path));
            }
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Indicates whether a specified path exists, and refers to a directory.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    /// <returns><c>true</c> if the path is a directory, else <c>false</c>.</returns>
    public override bool DirectoryExists(string path)
    {
        return (GetAttributes(path) & FileAttributes.Directory) != 0;
    }

    /// <summary>
    /// Indicates whether a specified path exists, and refers to a directory.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    /// <returns><c>true</c> if the path is a file, else <c>false</c>.</returns>
    public override bool FileExists(string path)
    {
        return (GetAttributes(path) & FileAttributes.Normal) != 0;
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
        try
        {
            var re = Utilities.ConvertWildcardsToRegEx(searchPattern, ignoreCase: false);

            var dirs = DoSearch(path, re, searchOption == SearchOption.AllDirectories, true, false);
            return dirs;
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
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
        try
        {
            var filter = Utilities.ConvertWildcardsToRegEx(searchPattern, ignoreCase: false);

            var results = DoSearch(path, filter, searchOption == SearchOption.AllDirectories, false, true);
            return results;
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Gets the names of all files and subdirectories in a specified directory.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <returns>Array of files and subdirectories matching the search pattern.</returns>
    public override IEnumerable<string> GetFileSystemEntries(string path)
    {
        try
        {
            var results = DoSearch(path, null, false, true, true);
            return results;
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
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
        try
        {
            var filter = Utilities.ConvertWildcardsToRegEx(searchPattern, ignoreCase: false);

            var results = DoSearch(path, filter, false, true, true);
            return results;
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Moves a directory.
    /// </summary>
    /// <param name="sourceDirectoryName">The directory to move.</param>
    /// <param name="destinationDirectoryName">The target directory name.</param>
    public override void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName)
    {
        try
        {
            var sourceParent = GetParentDirectory(sourceDirectoryName);
            var destParent = GetParentDirectory(destinationDirectoryName);

            var sourceName = Utilities.GetFileFromPath(sourceDirectoryName);
            var destName = Utilities.GetFileFromPath(destinationDirectoryName);

            var fileHandle = _client.Lookup(sourceParent, sourceName);
            if (fileHandle == null)
            {
                throw new DirectoryNotFoundException($"The directory '{sourceDirectoryName}' does not exist");
            }

            var sourceAttrs = _client.GetAttributes(fileHandle);
            if ((sourceAttrs.Type & Nfs3FileType.Directory) == 0)
            {
                throw new DirectoryNotFoundException($"The path '{sourceDirectoryName}' is not a directory");
            }

            _client.Rename(sourceParent, sourceName, destParent, destName);
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Moves a file, allowing an existing file to be overwritten.
    /// </summary>
    /// <param name="sourceName">The file to move.</param>
    /// <param name="destinationName">The target file name.</param>
    /// <param name="overwrite">Whether to permit a destination file to be overwritten.</param>
    public override void MoveFile(string sourceName, string destinationName, bool overwrite)
    {
        try
        {
            var sourceParent = GetParentDirectory(sourceName);
            var destParent = GetParentDirectory(destinationName);

            var sourceFileName = Utilities.GetFileFromPath(sourceName);
            var destFileName = Utilities.GetFileFromPath(destinationName);

            var sourceFileHandle = _client.Lookup(sourceParent, sourceFileName);
            if (sourceFileHandle == null)
            {
                throw new FileNotFoundException(
                    $"The file '{sourceName}' does not exist",
                    sourceName);
            }

            var sourceAttrs = _client.GetAttributes(sourceFileHandle);
            if ((sourceAttrs.Type & Nfs3FileType.Directory) != 0)
            {
                throw new FileNotFoundException(
                    $"The path '{sourceName}' is not a file",
                    sourceName);
            }

            var destFileHandle = _client.Lookup(destParent, destFileName);
            if (destFileHandle != null && overwrite == false)
            {
                throw new IOException($"The destination file '{destinationName}' already exists");
            }

            _client.Rename(sourceParent, sourceFileName, destParent, destFileName);
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
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
        try
        {
            Nfs3AccessPermissions requested;
            if (access == FileAccess.Read)
            {
                requested = Nfs3AccessPermissions.Read;
            }
            else if (access == FileAccess.ReadWrite)
            {
                requested = Nfs3AccessPermissions.Read | Nfs3AccessPermissions.Modify;
            }
            else
            {
                requested = Nfs3AccessPermissions.Modify;
            }

            if (mode == FileMode.Create || mode == FileMode.CreateNew ||
                (mode == FileMode.OpenOrCreate && !FileExists(path)))
            {
                var parent = GetParentDirectory(path);

                var setAttrs = new Nfs3SetAttributes
                {
                    Mode = NfsOptions.NewFilePermissions,
                    SetMode = true,
                    Size = 0,
                    SetSize = true
                };
                var handle = _client.Create(parent, Utilities.GetFileFromPath(path),
                    mode != FileMode.Create, setAttrs);

                return new Nfs3FileStream(_client, handle, access);
            }
            else
            {
                var handle = GetFile(path);
                var actualPerms = _client.Access(handle, requested);

                if (actualPerms != requested)
                {
                    throw new UnauthorizedAccessException($"Access denied opening '{path}'. Requested permission '{requested}', got '{actualPerms}'");
                }

                var result = new Nfs3FileStream(_client, handle, access);
                if (mode == FileMode.Append)
                {
                    result.Seek(0, SeekOrigin.End);
                }
                else if (mode == FileMode.Truncate)
                {
                    result.SetLength(0);
                }

                return result;
            }
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Gets the attributes of a file or directory.
    /// </summary>
    /// <param name="path">The file or directory to inspect.</param>
    /// <returns>The attributes of the file or directory.</returns>
    public override FileAttributes GetAttributes(string path)
    {
        try
        {
            var handle = GetFile(path);
            var nfsAttrs = _client.GetAttributes(handle);

            FileAttributes result = 0;
            if (nfsAttrs.Type == Nfs3FileType.Directory)
            {
                result |= FileAttributes.Directory;
            }
            else if (nfsAttrs.Type == Nfs3FileType.BlockDevice || nfsAttrs.Type == Nfs3FileType.CharacterDevice)
            {
                result |= FileAttributes.Device;
            }
            else
            {
                result |= FileAttributes.Normal;
            }

            if (Utilities.GetFileFromPath(path).StartsWith('.'))
            {
                result |= FileAttributes.Hidden;
            }

            return result;
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Sets the attributes of a file or directory.
    /// </summary>
    /// <param name="path">The file or directory to change.</param>
    /// <param name="newValue">The new attributes of the file or directory.</param>
    public override void SetAttributes(string path, FileAttributes newValue)
    {
        if (newValue != GetAttributes(path))
        {
            throw new NotSupportedException("Unable to change file attributes over NFS");
        }
    }

    /// <summary>
    /// Gets the creation time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The creation time.</returns>
    public override DateTime GetCreationTimeUtc(string path)
    {
        try
        {
            // Note creation time is not available, so simulating from last modification time
            var handle = GetFile(path);
            var attrs = _client.GetAttributes(handle);
            return attrs.ModifyTime.ToDateTime();
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Sets the creation time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetCreationTimeUtc(string path, DateTime newTime)
    {
        // No action - creation time is not accessible over NFS
    }

    /// <summary>
    /// Gets the last access time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The last access time.</returns>
    public override DateTime GetLastAccessTimeUtc(string path)
    {
        try
        {
            var handle = GetFile(path);
            var attrs = _client.GetAttributes(handle);
            return attrs.AccessTime.ToDateTime();
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Sets the last access time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetLastAccessTimeUtc(string path, DateTime newTime)
    {
        try
        {
            var handle = GetFile(path);
            _client.SetAttributes(handle,
                new Nfs3SetAttributes
                {
                    SetAccessTime = Nfs3SetTimeMethod.ClientTime,
                    AccessTime = new Nfs3FileTime(newTime)
                });
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Gets the last modification time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The last write time.</returns>
    public override DateTime GetLastWriteTimeUtc(string path)
    {
        try
        {
            var handle = GetFile(path);
            var attrs = _client.GetAttributes(handle);
            return attrs.ModifyTime.ToDateTime();
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Sets the last modification time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetLastWriteTimeUtc(string path, DateTime newTime)
    {
        try
        {
            var handle = GetFile(path);
            _client.SetAttributes(handle,
                new Nfs3SetAttributes
                {
                    SetModifyTime = Nfs3SetTimeMethod.ClientTime,
                    ModifyTime = new Nfs3FileTime(newTime)
                });
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Gets the length of a file.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <returns>The length in bytes.</returns>
    public override long GetFileLength(string path)
    {
        try
        {
            var handle = GetFile(path);
            var attrs = _client.GetAttributes(handle);
            return attrs.Size;
        }
        catch (Nfs3Exception ne)
        {
            throw ConvertNfsException(ne);
        }
    }

    /// <summary>
    /// Size of the Filesystem in bytes
    /// </summary>
    public override long Size
    {
        get { return (long) _client.FsStat(_client.RootHandle).TotalSizeBytes; }
    }

    /// <summary>
    /// Used space of the Filesystem in bytes
    /// </summary>
    public override long UsedSpace
    {
        get { return Size - AvailableSpace; }
    }

     /// <summary>
    /// Available space of the Filesystem in bytes
    /// </summary>
    public override long AvailableSpace
    {
        get { return (long) _client.FsStat(_client.RootHandle).FreeSpaceBytes; }
    }

    /// <summary>
    /// Disposes of this instance, freeing up any resources used.
    /// </summary>
    /// <param name="disposing"><c>true</c> if called from Dispose, else <c>false</c>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_client != null)
            {
                _client.Dispose();
                _client = null;
            }
        }

        base.Dispose(disposing);
    }

    private static Exception ConvertNfsException(Nfs3Exception ne)
    {
        throw new IOException($"NFS Status: {ne.Message}", ne);
    }

    private IEnumerable<string> DoSearch(string path, Func<string, bool> filter, bool subFolders, bool dirs, bool files)
    {
        var dir = GetDirectory(path);

        foreach (var de in _client.ReadDirectory(dir, true))
        {
            if (de.Name == "." || de.Name == "..")
            {
                continue;
            }

            var isDir = de.FileAttributes.Type == Nfs3FileType.Directory;

            if ((isDir && dirs) || (!isDir && files))
            {
                var searchName = !de.Name.Contains('.') ? $"{de.Name}." : de.Name;

                if (filter is null || filter(searchName))
                {
                    yield return Utilities.CombinePaths(path, de.Name);
                }
            }

            if (subFolders && isDir)
            {
                foreach (var subdirentry in DoSearch(Utilities.CombinePaths(path, de.Name), filter, subFolders, dirs, files))
                {
                    yield return subdirentry;
                }
            }
        }
    }

    private Nfs3FileHandle GetFile(string path)
    {
        var file = Utilities.GetFileFromPath(path);
        var parent = GetParentDirectory(path);

        var handle = _client.Lookup(parent, file);
        if (handle == null)
        {
            throw new FileNotFoundException("No such file or directory", path);
        }

        return handle;
    }

    private Nfs3FileHandle GetParentDirectory(string path)
    {
        var dirs = Utilities.GetDirectoryFromPath(path)
                                 .Split(Utilities.PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        var parent = GetDirectory(_client.RootHandle, dirs);
        return parent;
    }

    private Nfs3FileHandle GetDirectory(string path)
    {
        var dirs = path.Split(Utilities.PathSeparators, StringSplitOptions.RemoveEmptyEntries);
        return GetDirectory(_client.RootHandle, dirs);
    }

    private Nfs3FileHandle GetDirectory(Nfs3FileHandle parent, string[] dirs)
    {
        if (dirs == null)
        {
            return parent;
        }

        var handle = parent;
        for (var i = 0; i < dirs.Length; ++i)
        {
            handle = _client.Lookup(handle, dirs[i]);

            if (handle == null || _client.GetAttributes(handle).Type != Nfs3FileType.Directory)
            {
                throw new DirectoryNotFoundException();
            }
        }

        return handle;
    }
}
