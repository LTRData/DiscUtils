using DiscUtils.Internal;
using DiscUtils.Streams.Compatibility;
using DokanNet;
using DokanNet.Logging;
using LTRData.Extensions.Buffers;
using LTRData.Extensions.Formatting;
using LTRData.Extensions.Native.Memory;
using LTRData.Extensions.Split;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using FileAccess = System.IO.FileAccess;
using NativeFileAccess = DokanNet.FileAccess;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA1021 // Avoid out parameters
#pragma warning disable CA1822 // Mark members as static
#pragma warning disable IDE0057 // Use range operator
#pragma warning disable IDE0022 // Use expression body for methods

namespace DiscUtils.MountDokan;

#if NET5_0_OR_GREATER
[SupportedOSPlatform("windows")]
#endif
public class DokanDiscUtils : IDokanOperations2, IDisposable
{
    public IFileSystem FileSystem { get; }

    private const NativeFileAccess DataAccess = NativeFileAccess.ReadData | NativeFileAccess.WriteData | NativeFileAccess.AppendData |
                                          NativeFileAccess.Execute |
                                          NativeFileAccess.GenericExecute | NativeFileAccess.GenericWrite |
                                          NativeFileAccess.GenericRead;

    private const NativeFileAccess DataWriteAccess = NativeFileAccess.WriteData | NativeFileAccess.AppendData |
                                               NativeFileAccess.Delete |
                                               NativeFileAccess.GenericWrite;

#if CONSOLE_LOGGING
    private readonly ConsoleLogger logger = new("[DokanDiscUtils] ");
#else
    private readonly NullLogger logger = new();
#endif

    private readonly StringComparison _comparison = StringComparison.OrdinalIgnoreCase;

    private readonly List<KeyValuePair<string, string>> _transl = [];

    public event EventHandler<AccessCheckEventArgs>? AccessCheck;

    public FileSecurity? ForcedFileSecurity { get; set; }

    public DirectorySecurity? DirectorySecurity { get; set; }

    public bool CaseSensitive { get; }

    public bool NamedStreams { get; }

    public bool ReadOnly { get; }

    public bool BlockExecute { get; }

    public bool HiddenAsNormal { get; set; }

    public bool LeaveFSOpen { get; set; }

    public ReadOnlyCollection<KeyValuePair<string, string>> Translations => _transl.AsReadOnly();

    private NtStatus Trace(string method, string? fileName, in DokanFileInfo info, NtStatus result)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug($"{method}('{fileName}', {info}) -> {result}");
        }

        return result;
    }

    private NtStatus Trace(string method, ReadOnlyNativeMemory<char> fileName, in DokanFileInfo info, NtStatus result)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug($"{method}('{fileName}', {info}) -> {result}");
        }

        return result;
    }

    private NtStatus Trace<TParam>(string method, string fileName, in DokanFileInfo info, NtStatus result,
        TParam parameter)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug($"{method}('{fileName}', {info}, {parameter}) -> {result}");
        }

        return result;
    }

    private NtStatus Trace<TParam>(string method, ReadOnlyNativeMemory<char> fileName, in DokanFileInfo info, NtStatus result,
        TParam parameter)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug($"{method}('{fileName}', {info}, {parameter}) -> {result}");
        }

        return result;
    }

    private NtStatus Trace<TParam1, TParam2>(string method, string fileName, in DokanFileInfo info, NtStatus result,
        TParam1 parameter1, TParam2 parameter2)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug($"{method}('{fileName}', {info}, {parameter1}, {parameter2}) -> {result}");
        }

        return result;
    }

    private NtStatus Trace<TParam1, TParam2>(string method, ReadOnlyNativeMemory<char> fileName, in DokanFileInfo info, NtStatus result,
        TParam1 parameter1, TParam2 parameter2)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug($"{method}('{fileName}', {info}, {parameter1}, {parameter2}) -> {result}");
        }

        return result;
    }

    private NtStatus Trace<TParam1, TParam2, TParam3>(string method, string? fileName, in DokanFileInfo info, NtStatus result,
        TParam1 parameter1, TParam2 parameter2, TParam3 parameter3)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug($"{method}('{fileName}', {info}, {parameter1}, {parameter2}, {parameter3}) -> {result}");
        }

        return result;
    }

    private NtStatus Trace<TParam1, TParam2, TParam3>(string method, ReadOnlyNativeMemory<char> fileName, in DokanFileInfo info, NtStatus result,
        TParam1 parameter1, TParam2 parameter2, TParam3 parameter3)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug($"{method}('{fileName}', {info}, {parameter1}, {parameter2}, {parameter3}) -> {result}");
        }

        return result;
    }

    private NtStatus Trace(string method, ReadOnlyNativeMemory<char> fileName, in DokanFileInfo info,
        NativeFileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes,
        NtStatus result)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug(
                $"{method}('{fileName}', {info}, [{access}], [{share}], [{mode}], [{options}], [{attributes}]) -> {result}");
        }

        return result;
    }

    private NtStatus Trace(string method, string fileName, in DokanFileInfo info,
        NativeFileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes,
        NtStatus result)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug(
                $"{method}('{fileName}', {info}, [{access}], [{share}], [{mode}], [{options}], [{attributes}]) -> {result}");
        }

        return result;
    }

    public DokanDiscUtils(IFileSystem fileSystem, DokanDiscUtilsOptions options)
    {
#if NET7_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(fileSystem);
#else
        if (fileSystem is null)
        {
            throw new ArgumentNullException(nameof(fileSystem));
        }
#endif

        FileSystem = fileSystem;

        if (!fileSystem.CanWrite)
        {
            options |= DokanDiscUtilsOptions.ForceReadOnly;
        }

        if (options.HasFlag(DokanDiscUtilsOptions.ForceReadOnly))
        {
            ReadOnly = true;
        }

        if (fileSystem is IWindowsFileSystem or IUnixFileSystem)
        {
            NamedStreams = true;
        }

        if (fileSystem is IUnixFileSystem ||
            (fileSystem is VirtualFileSystem.VirtualFileSystem vfs && vfs.Options.CaseSensitive))
        {
            _comparison = StringComparison.Ordinal;
            CaseSensitive = true;
        }

        if (options.HasFlag(DokanDiscUtilsOptions.BlockExecute))
        {
            BlockExecute = true;
        }

        if (options.HasFlag(DokanDiscUtilsOptions.AccessCheck))
        {
            throw new NotImplementedException("Access check not implemented");
            //AccessCheck += ValidateFileAccess;
        }

        if (options.HasFlag(DokanDiscUtilsOptions.HiddenAsNormal))
        {
            HiddenAsNormal = true;
        }

        if (options.HasFlag(DokanDiscUtilsOptions.LeaveFSOpen))
        {
            LeaveFSOpen = true;
        }
    }

    #region Implementation of IDokanOperations

    /// <summary>
    /// Translates a path from OS representation to file system representation
    /// </summary>
    /// <param name="pathstr">OS translated path</param>
    /// <returns>File system original path</returns>
    private string TranslatePath(string pathstr)
    {
        var path = pathstr.AsSpan().Trim('\\');

        if (path.IsEmpty)
        {
            return string.Empty;
        }

        foreach (var transl in _transl)
        {
            if (path.Equals(transl.Key.AsSpan(), _comparison))
            {
                var newpath = transl.Value;
#if DEBUG
                Debug.WriteLine($"Using translation of '{transl.Key}' to '{newpath}'");
#endif

                return newpath;
            }

            if (path.Length <= transl.Key.Length)
            {
                continue;
            }

            if (path.StartsWith(transl.Key.AsSpan(), _comparison) &&
                path[transl.Key.Length] == '\\')
            {
#if NETCOREAPP
                var newpath = string.Concat(transl.Value, path.Slice(transl.Key.Length));
#elif NETSTANDARD2_1_OR_GREATER
                var newpath = Path.Join(transl.Value, path.Slice(transl.Key.Length));
#else
                var newpath = string.Concat(transl.Value, path.Slice(transl.Key.Length).ToString());
#endif

                var originalpath = path.ToString();

                _transl.Add(new(originalpath, newpath));

#if DEBUG
                Debug.WriteLine($"DokanDiscUtils: Added parent directory based translation of '{originalpath}' to '{newpath}'");
#endif

                return newpath;
            }
        }

        return pathstr;
    }

    /// <summary>
    /// Translates a path from OS representation to file system representation
    /// </summary>
    /// <param name="pathPtr">OS translated path</param>
    /// <returns>File system original path</returns>
    private string TranslatePath(ReadOnlyNativeMemory<char> pathPtr)
    {
        var path = pathPtr.Span.Trim('\\');

        if (path.IsEmpty)
        {
            return string.Empty;
        }

        foreach (var transl in _transl)
        {
            if (path.Equals(transl.Key.AsSpan(), _comparison))
            {
                var newpath = transl.Value;
#if DEBUG
                Debug.WriteLine($"Using translation of '{transl.Key}' to '{newpath}'");
#endif

                return newpath;
            }

            if (path.Length <= transl.Key.Length)
            {
                continue;
            }

            if (path.StartsWith(transl.Key.AsSpan(), _comparison) &&
                path[transl.Key.Length] == '\\')
            {
#if NETCOREAPP
                var newpath = string.Concat(transl.Value, path.Slice(transl.Key.Length));
#elif NETSTANDARD2_1_OR_GREATER
                var newpath = Path.Join(transl.Value, path.Slice(transl.Key.Length));
#else
                var newpath = string.Concat(transl.Value, path.Slice(transl.Key.Length).ToString());
#endif

                var originalpath = path.ToString();

                _transl.Add(new(originalpath, newpath));

#if DEBUG
                Debug.WriteLine($"DokanDiscUtils: Added parent directory based translation of '{originalpath}' to '{newpath}'");
#endif

                return newpath;
            }
        }

        return path.ToString();
    }

    /// <summary>
    /// Translates a path from file system representation to OS representation
    /// </summary>
    /// <param name="pathstr">File system original path</param>
    /// <returns>OS translated path</returns>
    private string UntranslatePath(string pathstr)
    {
        var path = pathstr.AsSpan().Trim('\\');

        if (path.IsEmpty)
        {
            return string.Empty;
        }

        foreach (var transl in _transl)
        {
            if (path.Equals(transl.Value.AsSpan(), _comparison))
            {
                var newpath = transl.Key;
#if DEBUG
                Debug.WriteLine($"Using translation of '{newpath}' to '{transl.Value}'");
#endif

                return newpath;
            }

            if (path.Length <= transl.Value.Length)
            {
                continue;
            }

            if (path.StartsWith(transl.Value.AsSpan(), _comparison) &&
                path[transl.Value.Length] == '\\')
            {
#if NETCOREAPP
                var newpath = string.Concat(transl.Key, path.Slice(transl.Value.Length));
#elif NETSTANDARD2_1_OR_GREATER
                var newpath = Path.Join(transl.Key, path.Slice(transl.Value.Length));
#else
                var newpath = string.Concat(transl.Key, path.Slice(transl.Value.Length).ToString());
#endif

                var originalpath = path.ToString();

                _transl.Add(new(newpath, originalpath));

#if DEBUG
                Debug.WriteLine($"DokanDiscUtils: Added parent directory based translation of '{newpath}' to '{originalpath}'");
#endif

                return newpath;
            }
        }

        return pathstr;
    }

    private readonly static char[] _invalidFileNameChars = Path.GetInvalidFileNameChars();

    /// <summary>
    /// Removes unsupported characters from a file name and adds a record to the translation
    /// dictionary for later file open/delete/etc operations.
    /// </summary>
    /// <param name="OSPath">Directory path, OS translated</param>
    /// <param name="fileSystempath">Directory path in the file system</param>
    /// <param name="fileSystemname">Directory entry name in the file system</param>
    /// <returns>OS translated name of the directory entry</returns>
    private string SanitizePath(string OSPath, string fileSystempath, string fileSystemname)
    {
        var newpath = fileSystemname;

        foreach (var c in _invalidFileNameChars)
        {
            newpath = newpath.Replace(c, '_');
        }

        if (ReferenceEquals(newpath, fileSystemname))
        {
            return fileSystemname;
        }

        var newFullPath = Path.Combine(OSPath, newpath);
        var fsFullPath = Path.Combine(fileSystempath, fileSystemname);

        _transl.Add(new(newFullPath, fsFullPath));

        Debug.WriteLine($"DokanDiscUtils: Added translation of '{fsFullPath}' to '{newFullPath}'");

        return newpath;
    }

    public NtStatus CreateFile(ReadOnlyNativeMemory<char> fileNamePtr, NativeFileAccess access, FileShare share, FileMode mode,
        FileOptions options, FileAttributes attributes, ref DokanFileInfo info)
    {
        if (mode == FileMode.Open
            && Utilities.GetFileName(fileNamePtr.Span).IndexOfAny('*', '?') >= 0)
        {
            return Trace(nameof(CreateFile), fileNamePtr, info, access, share, mode, options, attributes,
                DokanResult.InvalidName);
        }

        var fileName = TranslatePath(fileNamePtr);

        if (AccessCheck is not null)
        {
            using var accessToken = info.GetRequestorToken();

            var e = new AccessCheckEventArgs
            {
                Path = fileName,
                IsDirectory = info.IsDirectory,
                RequestorToken = accessToken,
                SynchronousIo = info.SynchronousIo,
                DeleteOnClose = info.DeletePending,
                NoCache = info.NoCache,
                PagingIo = info.PagingIo,
                ProcessId = info.ProcessId,
                WriteToEndOfFile = info.WriteToEndOfFile
            };

            AccessCheck(this, e);

            if (e.Status != NtStatus.Success)
            {
                return e.Status;
            }
        }

        if (info.IsDirectory)
        {
            return CreateDirectory(fileName, access, share, mode, options, attributes, ref info);
        }

        var result = DokanResult.Success;

        if (BlockExecute && access.HasFlag(NativeFileAccess.Execute))
        {
            result = NtStatus.AccessDenied;

            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                result);
        }

        DiscFileSystemInfo? fileInfo = null;
        var pathExists = true;
        var pathIsDirectory = false;

        var readWriteAttributes = (access & DataAccess) == 0;
        var readAccess = (access & DataWriteAccess) == 0;

        try
        {
            fileInfo = FileSystem.GetFileSystemInfo(fileName);
            
            pathExists = fileInfo.Exists;

            if (pathExists)
            {
                pathIsDirectory = fileInfo.Attributes.HasFlag(FileAttributes.Directory);
            }
        }
        catch (IOException)
        {
        }

        switch (mode)
        {
            case FileMode.Open:

                if (pathExists)
                {
                    // check if driver only wants to read attributes, security info, or open directory
                    if (readWriteAttributes || pathIsDirectory)
                    {
                        if (pathIsDirectory && (access & NativeFileAccess.Delete) == NativeFileAccess.Delete
                            && (access & NativeFileAccess.Synchronize) != NativeFileAccess.Synchronize)
                        {
                            //It is a DeleteFile request on a directory
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                attributes, DokanResult.AccessDenied);
                        }

                        info.IsDirectory = pathIsDirectory;

                        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                            attributes, DokanResult.Success);
                    }
                }
                else
                {
                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                        DokanResult.FileNotFound);
                }

                break;

            case FileMode.CreateNew:
                if (pathExists)
                {
                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                        DokanResult.FileExists);
                }

                break;

            case FileMode.Truncate:
                if (!pathExists)
                {
                    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                        DokanResult.FileNotFound);
                }

                break;
        }

        try
        {
            info.Context = FileSystem.OpenFile(fileName, mode,
                readAccess ? FileAccess.Read : FileAccess.ReadWrite);

            if (pathExists && (mode == FileMode.OpenOrCreate
                               || mode == FileMode.Create))
            {
                result = DokanResult.AlreadyExists; // Returned to caller for information, not error
            }

            if (mode is FileMode.CreateNew or FileMode.Create) //Files are always created as Archive
            {
                attributes |= FileAttributes.Archive;
                FileSystem.SetAttributes(fileName, attributes);
            }
        }
        catch (UnauthorizedAccessException) // don't have access rights
        {
            if (info.Context is IDisposable fileStream)
            {
                // returning AccessDenied cleanup and close won't be called,
                // so we have to take care of the stream now
                fileStream.Dispose();
                info.Context = null;
            }

            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                DokanResult.AccessDenied);
        }
        catch (DirectoryNotFoundException)
        {
            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                DokanResult.PathNotFound);
        }
        catch (Exception ex)
        when ((uint)ex.HResult == 0x80070020) //Sharing violation
        {
            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                DokanResult.SharingViolation);
        }

        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
            result);
    }

    private NtStatus CreateDirectory(string fileName, NativeFileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, ref DokanFileInfo info)
    {
        var result = DokanResult.Success;

        try
        {
            switch (mode)
            {
                case FileMode.Open:

                    if (!FileSystem.DirectoryExists(fileName))
                    {
                        if (FileSystem.Exists(fileName))
                        {
                            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                attributes, DokanResult.NotADirectory);
                        }

                        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                            attributes, DokanResult.PathNotFound);
                    }

                    break;

                case FileMode.OpenOrCreate:
                case FileMode.Create:
                    if (FileSystem.FileExists(fileName))
                    {
                        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                            attributes, DokanResult.NotADirectory);
                    }

                    if (!FileSystem.DirectoryExists(fileName))
                    {
                        FileSystem.CreateDirectory(fileName);
                    }

                    break;

                case FileMode.CreateNew:
                    if (FileSystem.FileExists(fileName))
                    {
                        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                            attributes, DokanResult.FileExists);
                    }

                    if (FileSystem.DirectoryExists(fileName))
                    {
                        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                            attributes, DokanResult.AlreadyExists);
                    }

                    FileSystem.CreateDirectory(fileName);
                    break;
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                DokanResult.AccessDenied);
        }

        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
            result);
    }

    public void Cleanup(ReadOnlyNativeMemory<char> fileNamePtr, ref DokanFileInfo info)
    {
        (info.Context as IDisposable)?.Dispose();
        info.Context = null;

        if (info.DeletePending)
        {
            var fileName = TranslatePath(fileNamePtr);

            if (info.IsDirectory)
            {
                FileSystem.DeleteDirectory(fileName);
            }
            else
            {
                FileSystem.DeleteFile(fileName);
            }
        }

        Trace(nameof(Cleanup), fileNamePtr, info, DokanResult.Success);
    }

    public void CloseFile(ReadOnlyNativeMemory<char> fileNamePtr, ref DokanFileInfo info)
    {
        (info.Context as IDisposable)?.Dispose();
        info.Context = null;
        Trace(nameof(CloseFile), fileNamePtr, info, DokanResult.Success);
        // could recreate cleanup code here but this is not called sometimes
    }

    public NtStatus ReadFile(ReadOnlyNativeMemory<char> fileNamePtr, NativeMemory<byte> buffer, out int bytesRead, long offset, ref DokanFileInfo info)
    {
        if (info.Context is CompatibilityStream stream) // normal read
        {
            lock (stream) //Protect from overlapped read
            {
                stream.Position = offset;
                bytesRead = stream.Read(buffer.Span);
            }
        }
        else // memory mapped read
        {
            var fileName = TranslatePath(fileNamePtr);

            using var fstream = FileSystem.OpenFile(fileName, FileMode.Open, FileAccess.Read);
            fstream.Position = offset;
            bytesRead = fstream.Read(buffer.Span);
        }

        return Trace(nameof(ReadFile), fileNamePtr, info, DokanResult.Success, bytesRead,
            offset);
    }

    public NtStatus WriteFile(ReadOnlyNativeMemory<char> fileNamePtr, ReadOnlyNativeMemory<byte> buffer, out int bytesWritten, long offset, ref DokanFileInfo info)
    {
        bytesWritten = 0;

        if (ReadOnly)
        {
            return Trace(nameof(WriteFile), fileNamePtr, info, DokanResult.AccessDenied);
        }

        if (info.Context is CompatibilityStream stream)
        {
            lock (stream) //Protect from overlapped write
            {
                stream.Position = offset;
                stream.Write(buffer.Span);
            }

            bytesWritten = buffer.Length;
        }
        else
        {
            var fileName = TranslatePath(fileNamePtr);

            using var fstream = FileSystem.OpenFile(fileName, FileMode.Open, FileAccess.Write);
            fstream.Position = offset;
            fstream.Write(buffer.Span);
            bytesWritten = buffer.Length;
        }

        return Trace(nameof(WriteFile), fileNamePtr, info, DokanResult.Success, bytesWritten,
            offset);
    }

    public NtStatus FlushFileBuffers(ReadOnlyNativeMemory<char> fileNamePtr, ref DokanFileInfo info)
    {
        if (ReadOnly)
        {
            return Trace(nameof(FlushFileBuffers), fileNamePtr, info, DokanResult.AccessDenied);
        }

        try
        {
            (info.Context as Stream)?.Flush();
            return Trace(nameof(FlushFileBuffers), fileNamePtr, info, DokanResult.Success);
        }
        catch (IOException)
        {
            return Trace(nameof(FlushFileBuffers), fileNamePtr, info, DokanResult.DiskFull);
        }
    }

    public NtStatus GetFileInformation(ReadOnlyNativeMemory<char> fileNamePtr, out ByHandleFileInformation fileInfo, ref DokanFileInfo info)
    {
        var fileName = TranslatePath(fileNamePtr);

        // may be called with info.Context == null, but usually it isn't
        var finfo = FileSystem.GetFileSystemInfo(fileName);

        if (!finfo.Exists)
        {
            fileInfo = default;
            return Trace(nameof(GetFileInformation), fileName, info, DokanResult.FileNotFound);
        }

        fileInfo = new ByHandleFileInformation
        {
            Length = FileSystem.FileExists(fileName) ?
                FileSystem.GetFileLength(fileName) : 0,
        };

        if (FileSystem is IWindowsFileSystem wfs)
        {
            fileInfo.FileIndex = wfs.GetFileId(fileName);
            fileInfo.NumberOfLinks = wfs.GetHardLinkCount(fileName);

            var wfsinfo = wfs.GetFileStandardInformation(fileName);
            fileInfo.Attributes = FilterAttributes(wfsinfo.FileAttributes);
            fileInfo.CreationTime = wfsinfo.CreationTime;
            fileInfo.LastAccessTime = wfsinfo.LastAccessTime;
            fileInfo.LastWriteTime = wfsinfo.LastWriteTime;
        }
        else
        {
            fileInfo.Attributes = FilterAttributes(finfo.Attributes);
            fileInfo.CreationTime = finfo.CreationTime;
            fileInfo.LastAccessTime = finfo.LastAccessTime;
            fileInfo.LastWriteTime = finfo.LastWriteTime;
        }

        if (FileSystem is IUnixFileSystem ufs)
        {
            var ufi = ufs.GetUnixFileInfo(fileName);
            fileInfo.FileIndex = ufi.Inode;
            fileInfo.NumberOfLinks = ufi.LinkCount;
        }

        return Trace(nameof(GetFileInformation), fileName, info, DokanResult.Success);
    }

    public NtStatus FindFiles(ReadOnlyNativeMemory<char> fileNamePtr, out IEnumerable<FindFileInformation> files, ref DokanFileInfo info)
    {
        files = FindFilesHelper(fileNamePtr, "*");

        return Trace(nameof(FindFiles), fileNamePtr, info, DokanResult.Success);
    }

    public NtStatus SetFileAttributes(ReadOnlyNativeMemory<char> fileNamePtr, FileAttributes attributes, ref DokanFileInfo info)
    {
        if (ReadOnly)
        {
            return Trace(nameof(SetFileAttributes), fileNamePtr, info, DokanResult.AccessDenied);
        }

        try
        {
            // MS-FSCC 2.6 File Attributes : There is no file attribute with the value 0x00000000
            // because a value of 0x00000000 in the FileAttributes field means that the file attributes for this file MUST NOT be changed when setting basic information for the file
            if (attributes != 0)
            {
                var fileName = TranslatePath(fileNamePtr);

                FileSystem.SetAttributes(fileName, attributes);
            }

            return Trace(nameof(SetFileAttributes), fileNamePtr, info, DokanResult.Success, attributes);
        }
        catch (UnauthorizedAccessException)
        {
            return Trace(nameof(SetFileAttributes), fileNamePtr, info, DokanResult.AccessDenied, attributes);
        }
        catch (FileNotFoundException)
        {
            return Trace(nameof(SetFileAttributes), fileNamePtr, info, DokanResult.FileNotFound, attributes);
        }
        catch (DirectoryNotFoundException)
        {
            return Trace(nameof(SetFileAttributes), fileNamePtr, info, DokanResult.PathNotFound, attributes);
        }
    }

    public NtStatus SetFileTime(ReadOnlyNativeMemory<char> fileNamePtr, DateTime? creationTime, DateTime? lastAccessTime,
        DateTime? lastWriteTime, ref DokanFileInfo info)
    {
        if (ReadOnly)
        {
            return Trace(nameof(SetFileTime), fileNamePtr, info, DokanResult.AccessDenied);
        }

        var fileName = TranslatePath(fileNamePtr);

        try
        {
            if (creationTime.HasValue)
            {
                FileSystem.SetCreationTime(fileName, creationTime.Value);
            }

            if (lastAccessTime.HasValue)
            {
                FileSystem.SetLastAccessTime(fileName, lastAccessTime.Value);
            }

            if (lastWriteTime.HasValue)
            {
                FileSystem.SetLastWriteTime(fileName, lastWriteTime.Value);
            }

            return Trace(nameof(SetFileTime), fileName, info, DokanResult.Success, creationTime, lastAccessTime,
                lastWriteTime);
        }
        catch (UnauthorizedAccessException)
        {
            return Trace(nameof(SetFileTime), fileName, info, DokanResult.AccessDenied, creationTime, lastAccessTime,
                lastWriteTime);
        }
        catch (FileNotFoundException)
        {
            return Trace(nameof(SetFileTime), fileName, info, DokanResult.FileNotFound, creationTime, lastAccessTime,
                lastWriteTime);
        }
    }

    public NtStatus DeleteFile(ReadOnlyNativeMemory<char> fileNamePtr, ref DokanFileInfo info)
    {
        if (ReadOnly)
        {
            return Trace(nameof(DeleteFile), fileNamePtr, info, DokanResult.AccessDenied);
        }

        var fileName = TranslatePath(fileNamePtr);

        if (!FileSystem.Exists(fileName))
        {
            return Trace(nameof(DeleteFile), fileName, info, DokanResult.FileNotFound);
        }

        if (FileSystem.DirectoryExists(fileName))
        {
            return Trace(nameof(DeleteFile), fileName, info, DokanResult.AccessDenied);
        }

        return Trace(nameof(DeleteFile), fileName, info, DokanResult.Success);
        // we just check here if we could delete the file - the true deletion is in Cleanup
    }

    public NtStatus DeleteDirectory(ReadOnlyNativeMemory<char> fileNamePtr, ref DokanFileInfo info)
    {
        if (ReadOnly)
        {
            return Trace(nameof(DeleteDirectory), fileNamePtr, info, DokanResult.AccessDenied);
        }

        var fileName = TranslatePath(fileNamePtr);

        try
        {
            FileSystem.DeleteDirectory(fileName, recursive: false);

            return Trace(nameof(DeleteDirectory), fileName, info, DokanResult.Success);
        }
        catch
        {
            return Trace(nameof(DeleteDirectory), fileName, info, DokanResult.DirectoryNotEmpty);
        }

        // if dir is not empty it can't be deleted
    }

    public NtStatus MoveFile(ReadOnlyNativeMemory<char> oldNamePtr, ReadOnlyNativeMemory<char> newNamePtr, bool replace, ref DokanFileInfo info)
    {
        if (ReadOnly)
        {
            return Trace(nameof(MoveFile), oldNamePtr, info, DokanResult.AccessDenied);
        }

        var oldName = TranslatePath(oldNamePtr);
        var newName = TranslatePath(newNamePtr);

        (info.Context as IDisposable)?.Dispose();
        info.Context = null;

        var exist = info.IsDirectory ? FileSystem.DirectoryExists(newName) : FileSystem.FileExists(newName);

        try
        {
            if (!exist)
            {
                info.Context = null;

                if (info.IsDirectory)
                {
                    FileSystem.MoveDirectory(oldName, newName);
                }
                else
                {
                    FileSystem.MoveFile(oldName, newName);
                }

                return Trace(nameof(MoveFile), oldName, info, DokanResult.Success, newName,
                    replace);
            }
            else if (replace)
            {
                info.Context = null;

                if (info.IsDirectory) //Cannot replace directory destination - See MOVEFILE_REPLACE_EXISTING
                {
                    return Trace(nameof(MoveFile), oldName, info, DokanResult.AccessDenied, newName,
                        replace);
                }

                FileSystem.MoveFile(oldName, newName, overwrite: true);

                return Trace(nameof(MoveFile), oldName, info, DokanResult.Success, newName,
                    replace);
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Trace(nameof(MoveFile), oldName, info, DokanResult.AccessDenied, newName,
                replace);
        }

        return Trace(nameof(MoveFile), oldName, info, DokanResult.FileExists, newName,
            replace);
    }

    public NtStatus SetEndOfFile(ReadOnlyNativeMemory<char> fileNamePtr, long length, ref DokanFileInfo info)
    {
        if (ReadOnly || info.Context is not Stream stream)
        {
            return Trace(nameof(SetEndOfFile), fileNamePtr, info, DokanResult.AccessDenied);
        }

        try
        {
            stream.SetLength(length);

            return Trace(nameof(SetEndOfFile), fileNamePtr, info, DokanResult.Success,
                length);
        }
        catch (IOException)
        {
            return Trace(nameof(SetEndOfFile), fileNamePtr, info, DokanResult.DiskFull,
                length);
        }
    }

    public NtStatus SetAllocationSize(ReadOnlyNativeMemory<char> fileNamePtr, long length, ref DokanFileInfo info)
    {
        if (ReadOnly || info.Context is not Stream stream)
        {
            return Trace(nameof(SetEndOfFile), fileNamePtr, info, DokanResult.AccessDenied);
        }

        try
        {
            stream.SetLength(length);

            return Trace(nameof(SetAllocationSize), fileNamePtr, info, DokanResult.Success,
                length);
        }
        catch (IOException)
        {
            return Trace(nameof(SetAllocationSize), fileNamePtr, info, DokanResult.DiskFull,
                length);
        }
    }

    public NtStatus LockFile(ReadOnlyNativeMemory<char> fileNamePtr, long offset, long length, ref DokanFileInfo info) => DokanResult.NotImplemented;

    public NtStatus UnlockFile(ReadOnlyNativeMemory<char> fileNamePtr, long offset, long length, ref DokanFileInfo info) => DokanResult.NotImplemented;

    public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, ref DokanFileInfo info)
    {
        if (!FileSystem.SupportsUsedAvailableSpace)
        {
            freeBytesAvailable = 0;
            totalNumberOfBytes = 0;
            totalNumberOfFreeBytes = 0;
        }
        else
        {
            freeBytesAvailable = FileSystem.AvailableSpace;
            totalNumberOfBytes = FileSystem.Size;
            totalNumberOfFreeBytes = FileSystem.AvailableSpace;
        }

        return Trace(nameof(GetDiskFreeSpace), null, info, DokanResult.Success, freeBytesAvailable,
            totalNumberOfBytes, totalNumberOfFreeBytes);
    }

    public NtStatus GetVolumeInformation(NativeMemory<char> volumeLabel, out FileSystemFeatures features,
        NativeMemory<char> fileSystemName, out uint maximumComponentLength, ref uint volumeSerialNumber, ref DokanFileInfo info)
    {
        if ((FileSystem as DiscFileSystem)?.VolumeLabel is { Length: > 0 } label)
        {
            volumeLabel.SetString(label);
        }

        var fsName = ((FileSystem as DiscFileSystem)?.FriendlyName)
            .AsSpan()
            .TokenEnum(' ', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault();

        if (fsName.IsEmpty)
        {
            fsName = FileSystem.GetType().Name.AsSpan();

            if (fsName.EndsWith("FileSystem".AsSpan(), StringComparison.Ordinal))
            {
                fsName = fsName.Slice(0, fsName.Length - "FileSystem".Length);
            }
            else if (fsName.EndsWith("Reader".AsSpan(), StringComparison.Ordinal))
            {
                fsName = fsName.Slice(0, fsName.Length - "Reader".Length);
            }
        }

        if (fsName.Length <= 5)
        {
            fsName.ToUpperInvariant(fileSystemName.Span);
            fileSystemName.Span.Slice(fsName.Length).Clear();
        }
        else
        {
            fileSystemName.SetString(fsName);
        }

        maximumComponentLength = 260;

        features = FileSystemFeatures.CasePreservedNames |
                   FileSystemFeatures.SupportsRemoteStorage |
                   FileSystemFeatures.UnicodeOnDisk;

        if (NamedStreams)
        {
            features |= FileSystemFeatures.NamedStreams;
        }

        if (CaseSensitive)
        {
            features |= FileSystemFeatures.CaseSensitiveSearch;
        }

        if (FileSystem is IWindowsFileSystem)
        {
            features |= FileSystemFeatures.PersistentAcls;
        }

        if (ReadOnly)
        {
            features |= FileSystemFeatures.ReadOnlyVolume;
        }

        if (FileSystem is DiscFileSystem dfs)
        {
            volumeSerialNumber = dfs.VolumeId;
        }

        return Trace(nameof(GetVolumeInformation), null, info, DokanResult.Success, volumeLabel,
            features, fileSystemName);
    }

    public NtStatus GetFileSecurity(ReadOnlyNativeMemory<char> fileNamePtr, out FileSystemSecurity? security, AccessControlSections sections,
        ref DokanFileInfo info)
    {
        try
        {
            if (DirectorySecurity != null && info.IsDirectory)
            {
                security = DirectorySecurity;
                return Trace(nameof(GetFileSecurity), fileNamePtr, info, DokanResult.Success);
            }
            else if (ForcedFileSecurity != null && !info.IsDirectory)
            {
                security = ForcedFileSecurity;
                return Trace(nameof(GetFileSecurity), fileNamePtr, info, DokanResult.Success);
            }

            if (FileSystem is not IWindowsFileSystem wfs)
            {
                security = null;
                return Trace(nameof(GetFileSecurity), fileNamePtr, info, DokanResult.NotImplemented);
            }

            var fileName = TranslatePath(fileNamePtr);

            security = new FileSecurity();

            if (sections != AccessControlSections.None)
            {
                var fs_security = wfs.GetSecurity(fileName);

                if (fs_security == null)
                {
                    security = null;
                    return Trace(nameof(GetFileSecurity), fileName, info, DokanResult.InvalidParameter);
                }

                var buffer = ArrayPool<byte>.Shared.Rent(fs_security.BinaryLength);
                try
                {
                    fs_security.GetBinaryForm(buffer, 0);
                    security.SetSecurityDescriptorBinaryForm(buffer, sections);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            return Trace(nameof(GetFileSecurity), fileName, info, DokanResult.Success, sections);
        }
        catch (UnauthorizedAccessException)
        {
            security = null;
            return Trace(nameof(GetFileSecurity), fileNamePtr, info, DokanResult.AccessDenied, sections);
        }
    }

    public NtStatus SetFileSecurity(ReadOnlyNativeMemory<char> fileNamePtr, FileSystemSecurity security, AccessControlSections sections,
        ref DokanFileInfo info)
    {
        if (ReadOnly || security is null)
        {
            return Trace(nameof(SetFileSecurity), fileNamePtr, info, DokanResult.AccessDenied);
        }

        try
        {
            if (FileSystem is not IWindowsFileSystem wfs)
            {
                return Trace(nameof(SetFileSecurity), fileNamePtr, info, DokanResult.NotImplemented);
            }

            var binaryForm = security.GetSecurityDescriptorBinaryForm();
            var fs_security = new Core.WindowsSecurity.AccessControl.RawSecurityDescriptor(binaryForm, 0);

            var fileName = TranslatePath(fileNamePtr);

            wfs.SetSecurity(fileName, fs_security);

            return Trace(nameof(SetFileSecurity), fileName, info, DokanResult.Success, sections);
        }
        catch (UnauthorizedAccessException)
        {
            return Trace(nameof(SetFileSecurity), fileNamePtr, info, DokanResult.AccessDenied, sections);
        }
    }

    public NtStatus Mounted(ReadOnlyNativeMemory<char> mountPoint, ref DokanFileInfo info) => Trace(nameof(Mounted), null, info, DokanResult.Success);

    public NtStatus Unmounted(ref DokanFileInfo info) => Trace(nameof(Unmounted), null, info, DokanResult.Success);

    public NtStatus FindStreams(ReadOnlyNativeMemory<char> fileNamePtr, out IEnumerable<FindFileInformation> streams, ref DokanFileInfo info)
    {
        var fileName = TranslatePath(fileNamePtr);

        if (FileSystem is IWindowsFileSystem wfs)
        {
            streams = wfs.GetAlternateDataStreams(fileName)
                .Select(name =>
                {
                    var finfo = FileSystem.GetFileInfo(name);

                    return new FindFileInformation
                    {
                        Attributes = FilterAttributes(finfo.Attributes),
                        CreationTime = finfo.CreationTime,
                        LastAccessTime = finfo.LastAccessTime,
                        LastWriteTime = finfo.LastWriteTime,
                        Length = finfo.Length,
                        FileName = finfo.Name.AsMemory()
                    };
                });

            return Trace(nameof(FindStreams), fileName, info, DokanResult.Success, "FindStreams done");
        }
        else
        {
            streams = [];
            return Trace(nameof(FindStreams), fileName, info, DokanResult.NotImplemented);
        }
    }

    private FileAttributes FilterAttributes(FileAttributes attributes)
    {
        if (HiddenAsNormal)
        {
            attributes &= ~(FileAttributes.Hidden | FileAttributes.System);
        }

        return attributes;
    }

    public IEnumerable<FindFileInformation> FindFilesHelper(ReadOnlyNativeMemory<char> pathPtr, string searchPattern)
    {
        var OSPath = pathPtr.Span.Trim('\\').ToString();
        var path = TranslatePath(OSPath);

        searchPattern ??= "*";
        searchPattern = searchPattern.Replace('<', '*');

        if (FileSystem is IWindowsFileSystem wfs)
        {
            var files = FileSystem.GetFileSystemEntries(path, searchPattern)
                .Select(FileSystem.GetFileSystemInfo)
                .Where(dirEntry => dirEntry.Exists)
                .SelectMany(dirEntry =>
                {
                    var wfsinfo = wfs.GetFileStandardInformation(dirEntry.FullName);

                    var info = new FindFileInformation
                    {
                        Length = dirEntry is DiscFileInfo fileEntry ? fileEntry.Length : 0,
                        FileName = SanitizePath(OSPath, path, dirEntry.Name).AsMemory(),
                        Attributes = FilterAttributes(dirEntry.Attributes),
                        CreationTime = wfsinfo.CreationTime,
                        LastAccessTime = wfsinfo.LastAccessTime,
                        LastWriteTime = wfsinfo.LastWriteTime,
                        ShortFileName = wfs.GetShortName(dirEntry.FullName).AsMemory()
                    };

                    return wfs.GetAlternateDataStreams(dirEntry.FullName).Select(stream =>
                    {
                        var stream_path = $"{dirEntry.FullName}:{stream}";

                        return new FindFileInformation
                        {
                            Attributes = FilterAttributes(FileSystem.GetAttributes(stream_path)),
                            CreationTime = FileSystem.GetCreationTime(stream_path),
                            LastAccessTime = FileSystem.GetLastAccessTime(stream_path),
                            LastWriteTime = FileSystem.GetLastWriteTime(stream_path),
                            Length = FileSystem.GetFileLength(stream_path),
                            FileName = SanitizePath(OSPath, path, stream_path).AsMemory()
                        };
                    }).Prepend(info);
                });

            return files;
        }
        else
        {
            var files = FileSystem.GetFileSystemEntries(path, searchPattern)
                .Select(name =>
                {
                    try
                    {
                        return FileSystem.GetFileSystemInfo(name);
                    }
                    catch (Exception ex)
                    {
                        logger?.Warn("Failed getting information about file {name}: {message}", name, ex.JoinMessages());
                        return null;
                    }
                })
                .Where(dirEntry => dirEntry is not null && dirEntry.Exists)
                .Select(dirEntry =>
                {
                    var info = new FindFileInformation
                    {
                        Attributes = FilterAttributes(dirEntry!.Attributes),
                        CreationTime = dirEntry.CreationTime,
                        LastAccessTime = dirEntry.LastAccessTime,
                        LastWriteTime = dirEntry.LastWriteTime,
                        Length = dirEntry is DiscFileInfo fileEntry ? fileEntry.Length : 0,
                        FileName = SanitizePath(OSPath, path, dirEntry.Name).AsMemory()
                    };

                    if (FileSystem is IDosFileSystem dfs)
                    {
                        info.ShortFileName = dfs.GetShortName(dirEntry.FullName).AsMemory();
                    }

                    return info;
                });

            return files;
        }
    }

    public NtStatus FindFilesWithPattern(ReadOnlyNativeMemory<char> fileNamePtr, ReadOnlyNativeMemory<char> searchPatternPtr, out IEnumerable<FindFileInformation> files,
        ref DokanFileInfo info)
    {
        files = FindFilesHelper(fileNamePtr, searchPatternPtr.ToString());

        return Trace(nameof(FindFilesWithPattern), fileNamePtr, info, DokanResult.Success);
    }

#region IDisposable Support
    public bool IsDisposed { get; private set; } // To detect redundant calls

    int IDokanOperations2.DirectoryListingTimeoutResetIntervalMs => 0;

    protected virtual void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            if (disposing)
            {
                if (!LeaveFSOpen && FileSystem is IDisposable disposable_filesystem)
                {
                    try
                    {
                        disposable_filesystem.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"{nameof(DokanDiscUtils)}.{nameof(Dispose)}: {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
                    }
                }

                // TODO: dispose managed state (managed objects).
#if CONSOLE_LOGGING
                logger.Dispose();
#endif
            }

            // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.

            // TODO: set large fields to null.
            _transl.Clear();

            IsDisposed = true;
        }
    }

    // TODO: override a finalizer only if Dispose(bool disposing) above has code to free unmanaged resources.
    ~DokanDiscUtils()
    {
        // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        Dispose(false);
    }

    // This code added to correctly implement the disposable pattern.
    public void Dispose()
    {
        // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
        Dispose(true);
        // TODO: uncomment the following line if the finalizer is overridden above.
        GC.SuppressFinalize(this);
    }

#endregion

#endregion Implementation of IDokanOperations
}
