using DiscUtils.Streams.Compatibility;
using FuseDotNet;
using FuseDotNet.Extensions;
using FuseDotNet.Logging;
using LTRData.Extensions.Native.Memory;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DiscUtils.MountFuse;

public class FuseDiscUtils : IFuseOperations
{
    public IFileSystem FileSystem { get; }

    private readonly ILogger logger;

    public bool CaseSensitive { get; }

    public bool PosixFileSystem { get; }

    public bool BlockExecute { get; }

    public bool LeaveFSOpen { get; set; }

    private PosixResult Trace(string method, string fileName, PosixResult result)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug($"{method}('{fileName}') -> {result}");
        }

        return result;
    }

    private PosixResult Trace(string method, ReadOnlyNativeMemory<byte> fileNamePtr, PosixResult result)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug($"{method}('{FuseHelper.GetString(fileNamePtr)}') -> {result}");
        }

        return result;
    }

    private PosixResult Trace(string method, ReadOnlyNativeMemory<byte> fileNamePtr, in FuseFileInfo info, PosixResult result)
    {
        if (logger.DebugEnabled)
        {
            logger.Debug($"{method}('{FuseHelper.GetString(fileNamePtr)}', {info}) -> {result}");
        }

        return result;
    }

    public FuseDiscUtils(IFileSystem fileSystem, FuseDiscUtilsOptions options, ILogger? logger = null)
    {
        FileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        
        if (fileSystem is IUnixFileSystem ||
            (fileSystem is VirtualFileSystem.VirtualFileSystem vfs && vfs.Options.CaseSensitive))
        {
            CaseSensitive = true;
        }

        if (fileSystem is IUnixFileSystem &&
            (fileSystem is not Iso9660.CDReader cdReader ||
            cdReader.ActiveVariant == Iso9660.Iso9660Variant.RockRidge))
        {
            PosixFileSystem = true;
        }

        if (options.HasFlag(FuseDiscUtilsOptions.BlockExecute))
        {
            BlockExecute = true;
        }

        if (options.HasFlag(FuseDiscUtilsOptions.AccessCheck))
        {
            throw new NotImplementedException("Access check not implemented");
            //AccessCheck = true;
        }

        if (options.HasFlag(FuseDiscUtilsOptions.LeaveFSOpen))
        {
            LeaveFSOpen = true;
        }

        this.logger = logger ?? new NullLogger();
    }

    public PosixResult Access(ReadOnlyNativeMemory<byte> fileNamePtr, PosixAccessMode mask)
    {
        var path = FuseHelper.GetString(fileNamePtr);

        if (!FileSystem.Exists(path))
        {
            return Trace(nameof(Access), path, PosixResult.ENOENT);
        }

        if (BlockExecute &&
            mask.HasFlag(PosixAccessMode.Execute) &&
            !FileSystem.GetAttributes(path).HasFlag(FileAttributes.Directory))
        {
            return Trace(nameof(Access), path, PosixResult.EPERM);
        }

        if (mask.HasFlag(PosixAccessMode.Write) &&
            !FileSystem.CanWrite)
        {
            return Trace(nameof(Access), path, PosixResult.EROFS);
        }

        return Trace(nameof(Access), path, PosixResult.Success);
    }

    public PosixResult Create(ReadOnlyNativeMemory<byte> fileNamePtr, PosixFileMode mode, ref FuseFileInfo fileInfo)
    {
        if (!FileSystem.CanWrite)
        {
            return Trace(nameof(Create), fileNamePtr, PosixResult.EROFS);
        }

        var path = FuseHelper.GetString(fileNamePtr);

        fileInfo.Context = FileSystem.OpenFile(path, fileInfo.flags.ToFileMode(), fileInfo.flags.ToFileAccess());

        return Trace(nameof(Create), path, PosixResult.Success);
    }

    public PosixResult Flush(ReadOnlyNativeMemory<byte> fileNamePtr, ref FuseFileInfo fileInfo)
    {
        if (fileInfo.Context is Stream stream)
        {
            if (!stream.CanWrite)
            {
                return Trace(nameof(Flush), fileNamePtr, fileInfo, PosixResult.Success);
            }

            stream.Flush();
            return Trace(nameof(Flush), fileNamePtr, fileInfo, PosixResult.Success);
        }

        return Trace(nameof(Flush), fileNamePtr, fileInfo, PosixResult.EBADF);
    }

    public PosixResult FSync(ReadOnlyNativeMemory<byte> fileNamePtr, bool datasync, ref FuseFileInfo fileInfo)
    {
        if (fileInfo.Context is Stream stream)
        {
            if (!stream.CanWrite)
            {
                return Trace(nameof(FSync), fileNamePtr, fileInfo, PosixResult.Success);
            }

            stream.Flush();
            return Trace(nameof(FSync), fileNamePtr, fileInfo, PosixResult.Success);
        }

        return Trace(nameof(FSync), fileNamePtr, fileInfo, PosixResult.EBADF);
    }

    public PosixResult FSyncDir(ReadOnlyNativeMemory<byte> fileNamePtr, bool datasync, ref FuseFileInfo fileInfo)
        => Trace(nameof(FSyncDir), fileNamePtr, fileInfo, PosixResult.Success);

    public PosixResult GetAttr(ReadOnlyNativeMemory<byte> fileNamePtr, out FuseFileStat stat, ref FuseFileInfo fileInfo)
    {
        var path = FuseHelper.GetString(fileNamePtr);

        return GetAttr(path, out stat);
    }

    public PosixResult GetAttr(string path, out FuseFileStat stat)
    {
        var finfo = FileSystem.GetFileSystemInfo(path);

        if (!finfo.Exists)
        {
            stat = default;
            return Trace(nameof(GetAttr), path, PosixResult.ENOENT);
        }

        stat = new()
        {
            st_size = FileSystem.FileExists(path) ?
                FileSystem.GetFileLength(path) : 0,
            st_nlink = 1,
            st_mode = finfo.Attributes.ToPosixFileMode(),
            st_birthtim = finfo.CreationTime,
            st_atim = finfo.LastAccessTime,
            st_mtim = finfo.LastWriteTime
        };

        if (FileSystem is IWindowsFileSystem wfs)
        {
            var fileNo = wfs.GetFileId(path);
            stat.st_ino = fileNo & 0xffffffffffff;  // Skip sequence part of mft number here
            stat.st_nlink = wfs.GetHardLinkCount(path);
            stat.st_gen = fileNo >> 48;
        }

        if (PosixFileSystem && FileSystem is IUnixFileSystem ufs)
        {
            var ufi = ufs.GetUnixFileInfo(path);
            stat.st_ino = ufi.Inode;
            stat.st_nlink = ufi.LinkCount;
            stat.st_rdev = ufi.DeviceId;
            stat.st_gid = (uint)ufi.GroupId;
            stat.st_uid = (uint)ufi.UserId;
            stat.st_mode = GetPosixFileMode(ufi.Permissions, ufi.FileType);
        }

        if (FileSystem is IClusterBasedFileSystem cfs)
        {
            stat.st_blksize = 512;

            if (stat.st_size > 0)
            {
                stat.st_blocks = cfs.GetAllocatedClustersCount(path)
                    * (int)(cfs.ClusterSize / 512);
            }
        }
        else
        {
            stat.st_blksize = 512;
            stat.st_blocks = (stat.st_size + 511) / 512;
        }

        return Trace(nameof(GetAttr), path, PosixResult.Success);
    }

    public static PosixFileMode GetPosixFileMode(UnixFilePermissions permissions, UnixFileType fileType)
        => (PosixFileMode)((int)permissions | ((int)fileType << 0xC));

    public void Init(ref FuseConnInfo fuse_conn_info)
    {
    }

    public PosixResult IoCtl(ReadOnlyNativeMemory<byte> readOnlySpan, int cmd, nint arg, ref FuseFileInfo fileInfo, FuseIoctlFlags flags, nint data)
        => PosixResult.ENOSYS;

    public PosixResult Link(ReadOnlyNativeMemory<byte> from, ReadOnlyNativeMemory<byte> to)
		=> PosixResult.ENOSYS;

    public PosixResult MkDir(ReadOnlyNativeMemory<byte> fileNamePtr, PosixFileMode mode)
    {
        if (!FileSystem.CanWrite)
        {
            return Trace(nameof(MkDir), fileNamePtr, PosixResult.EROFS);
        }

        var path = FuseHelper.GetString(fileNamePtr);

        FileSystem.CreateDirectory(path);

        return Trace(nameof(MkDir), path, PosixResult.Success);
    }

    public PosixResult Open(ReadOnlyNativeMemory<byte> fileNamePtr, ref FuseFileInfo fileInfo)
    {
        var path = FuseHelper.GetString(fileNamePtr);

        fileInfo.Context = FileSystem.OpenFile(path, fileInfo.flags.ToFileMode(), fileInfo.flags.ToFileAccess());

        return Trace(nameof(Open), path, PosixResult.Success);
    }

    public PosixResult OpenDir(ReadOnlyNativeMemory<byte> fileNamePtr, ref FuseFileInfo fileInfo)
    {
        var path = FuseHelper.GetString(fileNamePtr);

        if (FileSystem.DirectoryExists(path))
        {
            return Trace(nameof(OpenDir), path, PosixResult.Success);
        }

        return Trace(nameof(OpenDir), path, PosixResult.ENOENT);
    }

    public PosixResult Read(ReadOnlyNativeMemory<byte> fileNamePtr, NativeMemory<byte> buffer, long position, out int readLength, ref FuseFileInfo fileInfo)
    {
        if (fileInfo.Context is Stream stream)
        {
            if (!stream.CanRead)
            {
                readLength = 0;
                return Trace(nameof(Read), fileNamePtr, fileInfo, PosixResult.EPERM);
            }

            stream.Position = position;
            readLength = stream.Read(buffer.Span);

            return Trace(nameof(Read), fileNamePtr, fileInfo, PosixResult.Success);
        }

        readLength = 0;
        return Trace(nameof(Read), fileNamePtr, fileInfo, PosixResult.EBADF);
    }

    public PosixResult ReadDir(ReadOnlyNativeMemory<byte> fileNamePtr, out IEnumerable<FuseDirEntry> entries, ref FuseFileInfo fileInfo, long offset, FuseReadDirFlags flags)
    {
        var path = FuseHelper.GetString(fileNamePtr);

        if (FileSystem is IWindowsFileSystem wfs)
        {
            entries = FileSystem.GetFileSystemEntries(path)
                .Where(wfs.Exists)
                .Select(wfs.GetFileSystemInfo)
                .SelectMany(dirEntry =>
                {
                    var fullPath = dirEntry.FullName;

                    var rc = GetAttr(fullPath, out var stat);

                    if (rc != PosixResult.Success)
                    {
                        Trace(nameof(GetAttr), fullPath, rc);
                        return [];
                    }

                    var info = new FuseDirEntry(dirEntry.Name,
                                                0,
                                                FuseFillDirFlags.FillDirPlus,
                                                stat);

                    var streamCount = 0L;

                    return wfs.GetAlternateDataStreams(fullPath).Select(stream =>
                    {
                        var fullPath = $"{dirEntry.FullName}:{stream}";

                        var name = $"{dirEntry.Name}:{stream}";

                        var rc = GetAttr(fullPath, out var stat);

                        if (rc != PosixResult.Success)
                        {
                            Trace(nameof(GetAttr), fullPath, rc);

                            return new FuseDirEntry(name,
                                                    0,
                                                    0,
                                                    info.Stat);
                        }

                        stat.st_mode = info.Stat.st_mode;
                        stat.st_ino = (info.Stat.st_ino << 16) + streamCount;

                        streamCount++;

                        return new FuseDirEntry(name,
                                                0,
                                                FuseFillDirFlags.FillDirPlus,
                                                stat);
                    }).Prepend(info);
                });
        }
        else
        {
            entries = FileSystem.GetFileSystemEntries(path)
                .Where(dirEntry => Path.GetFileName(dirEntry) is not "." and not "..")
                .Select(FileSystem.GetFileSystemInfo)
                .Select(dirEntry =>
                {
                    var fullPath = dirEntry.FullName;

                    var rc = GetAttr(fullPath, out var stat);

                    if (rc != PosixResult.Success)
                    {
                        Trace(nameof(GetAttr), fullPath, rc);
                        return new FuseDirEntry(dirEntry.Name,
                                                0,
                                                0,
                                                new() { st_mode = dirEntry.Attributes.ToPosixFileMode() });
                    }

                    return new FuseDirEntry(dirEntry.Name,
                                            0,
                                            FuseFillDirFlags.FillDirPlus,
                                            stat);
                });
        }

        if (GetAttr(Path.GetDirectoryName(path) ?? "/", out var stat) == PosixResult.Success)
        {
            entries = entries.Prepend(new()
            {
                Name = "..",
                Stat = stat
            });
        }

        if (GetAttr(path, out stat) == PosixResult.Success)
        {
            entries = entries.Prepend(new()
            {
                Name = ".",
                Stat = stat
            });
        }

        return Trace(nameof(ReadDir), path, PosixResult.Success);
    }

    public PosixResult ReadLink(ReadOnlyNativeMemory<byte> fileNamePtr, NativeMemory<byte> target)
		=> PosixResult.ENOSYS;

    public PosixResult Release(ReadOnlyNativeMemory<byte> fileNamePtr, ref FuseFileInfo fileInfo)
    {
        if (fileInfo.Context is Stream stream)
        {
            stream.Close();
            return Trace(nameof(Release), fileNamePtr, fileInfo, PosixResult.Success);
        }

        return Trace(nameof(Release), fileNamePtr, fileInfo, PosixResult.EBADF);
    }

    public PosixResult ReleaseDir(ReadOnlyNativeMemory<byte> fileNamePtr, ref FuseFileInfo fileInfo)
		=> PosixResult.Success;

    public PosixResult Rename(ReadOnlyNativeMemory<byte> from, ReadOnlyNativeMemory<byte> to)
    {
        if (!FileSystem.CanWrite)
        {
            return Trace(nameof(Rename), from, PosixResult.EROFS);
        }

        var pathFrom = FuseHelper.GetString(from);
        var pathTo = FuseHelper.GetString(to);

        if (FileSystem.FileExists(pathFrom))
        {
            FileSystem.MoveFile(pathFrom, pathTo);
        }
        else if (FileSystem.DirectoryExists(pathFrom))
        {
            FileSystem.MoveDirectory(pathFrom, pathTo);
        }
        else
        {
            return Trace(nameof(Rename), pathFrom, PosixResult.ENOENT);
        }

        return Trace(nameof(Rename), pathFrom, PosixResult.Success);
    }

    public PosixResult RmDir(ReadOnlyNativeMemory<byte> fileNamePtr)
    {
        if (!FileSystem.CanWrite)
        {
            return Trace(nameof(RmDir), fileNamePtr, PosixResult.EROFS);
        }

        var path = FuseHelper.GetString(fileNamePtr);

        FileSystem.DeleteDirectory(path);

        return Trace(nameof(RmDir), path, PosixResult.Success);
    }

    public PosixResult StatFs(ReadOnlyNativeMemory<byte> fileNamePtr, out FuseVfsStat statvfs)
    {
        statvfs = new()
        {
            f_namemax = 260
        };

        if (FileSystem is IClusterBasedFileSystem cfs)
        {
            statvfs.f_bsize = (uint)cfs.ClusterSize;
            statvfs.f_blocks = cfs.TotalClusters;
            statvfs.f_frsize = (uint)cfs.ClusterSize;
        }

        return Trace(nameof(StatFs), fileNamePtr, PosixResult.Success);
    }

    public PosixResult SymLink(ReadOnlyNativeMemory<byte> from, ReadOnlyNativeMemory<byte> to)
		=> PosixResult.ENOSYS;

    public PosixResult Truncate(ReadOnlyNativeMemory<byte> fileNamePtr, long size)
    {
        if (!FileSystem.CanWrite)
        {
            return Trace(nameof(Truncate), fileNamePtr, PosixResult.EROFS);
        }

        var path = FuseHelper.GetString(fileNamePtr);

        using var stream = FileSystem.OpenFile(path, FileMode.Open, FileAccess.ReadWrite);

        stream.SetLength(size);

        return Trace(nameof(Truncate), path, PosixResult.Success);
    }

    public PosixResult Unlink(ReadOnlyNativeMemory<byte> fileNamePtr)
    {
        if (!FileSystem.CanWrite)
        {
            return Trace(nameof(Unlink), fileNamePtr, PosixResult.EROFS);
        }

        var path = FuseHelper.GetString(fileNamePtr);

        FileSystem.DeleteFile(path);

        return Trace(nameof(Unlink), path, PosixResult.Success);
    }

    public PosixResult UTime(ReadOnlyNativeMemory<byte> fileNamePtr, TimeSpec atime, TimeSpec mtime, ref FuseFileInfo fileInfo)
    {
        string? path = null;

        if (!atime.IsOmit && FileSystem.CanWrite)
        {
            path ??= FuseHelper.GetString(fileNamePtr);
            FileSystem.SetLastAccessTimeUtc(path, atime.ToDateTime().UtcDateTime);
        }

        if (!mtime.IsOmit)
        {
            if (!FileSystem.CanWrite)
            {
                return Trace(nameof(UTime), fileNamePtr, PosixResult.EROFS);
            }

            path ??= FuseHelper.GetString(fileNamePtr);
            FileSystem.SetLastWriteTimeUtc(path, mtime.ToDateTime().UtcDateTime);
        }

        return Trace(nameof(UTime), fileNamePtr, PosixResult.Success);
    }

    public PosixResult Write(ReadOnlyNativeMemory<byte> fileNamePtr, ReadOnlyNativeMemory<byte> buffer, long position, out int writtenLength, ref FuseFileInfo fileInfo)
    {
        if (!FileSystem.CanWrite)
        {
            writtenLength = 0;
            return Trace(nameof(Write), fileNamePtr, fileInfo, PosixResult.EROFS);
        }

        if (fileInfo.Context is Stream stream)
        {
            if (!stream.CanWrite)
            {
                writtenLength = 0;
                return Trace(nameof(Write), fileNamePtr, fileInfo, PosixResult.EROFS);
            }

            stream.Position = position;
            stream.Write(buffer.Span);
            writtenLength = buffer.Length;

            return Trace(nameof(Write), fileNamePtr, fileInfo, PosixResult.Success);
        }

        writtenLength = 0;
        return Trace(nameof(Write), fileNamePtr, fileInfo, PosixResult.EBADF);
    }

    public bool IsDisposed { get; private set; }

    protected virtual void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            if (disposing)
            {
                // TODO: dispose managed state (managed objects)
                if (!LeaveFSOpen && FileSystem is IDisposable disposableFs)
                {
                    disposableFs.Dispose();
                }

                if (logger is IDisposable disposableLogger)
                {
                    disposableLogger.Dispose();
                }
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer

            // TODO: set large fields to null

            IsDisposed = true;
        }
    }

    // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
    ~FuseDiscUtils()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
