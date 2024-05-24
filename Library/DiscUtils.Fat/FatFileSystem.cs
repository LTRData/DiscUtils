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
using System.Linq;
using System.Text;
using DiscUtils.CoreCompat;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Streams.Compatibility;
using LTRData.Extensions.Split;

namespace DiscUtils.Fat;

/// <summary>
/// Class for accessing FAT file systems.
/// </summary>
public sealed class FatFileSystem : DiscFileSystem, IDosFileSystem, IClusterBasedFileSystem
{
    /// <summary>
    /// The Epoch for FAT file systems (1st Jan, 1980).
    /// </summary>
    public static readonly DateTime Epoch = new(1980, 1, 1);

    private readonly Dictionary<uint, Directory> _dirCache;
    private readonly Ownership _ownsData;

    private readonly TimeConverter _timeConverter;
    private byte[] _bootSector;
    private ushort _bpbBkBootSec;

    private ushort _bpbBytesPerSec;
    private ushort _bpbExtFlags;
    private ushort _bpbFATSz16;

    private uint _bpbFATSz32;
    private ushort _bpbFSInfo;
    private ushort _bpbFSVer;
    private uint _bpbHiddSec;
    private ushort _bpbNumHeads;
    private uint _bpbRootClus;
    private ushort _bpbRootEntCnt;
    private ushort _bpbRsvdSecCnt;
    private ushort _bpbSecPerTrk;
    private ushort _bpbTotSec16;
    private uint _bpbTotSec32;

    private byte _bsBootSig;
    private uint _bsVolId;
    private string _bsVolLab;
    private Stream _data;
    private Directory _rootDir;
    
    static FatFileSystem()
    {
        EncodingHelper.RegisterEncodings();
    }

    /// <summary>
    /// Initializes a new instance of the FatFileSystem class.
    /// </summary>
    /// <param name="data">The stream containing the file system.</param>
    /// <remarks>
    /// Local time is the effective timezone of the new instance.
    /// </remarks>
    public FatFileSystem(Stream data)
        : base(new FatFileSystemOptions())
    {
        _dirCache = [];
        _timeConverter = DefaultTimeConverter;
        Initialize(data);
    }

    /// <summary>
    /// Initializes a new instance of the FatFileSystem class.
    /// </summary>
    /// <param name="data">The stream containing the file system.</param>
    /// <param name="ownsData">Indicates if the new instance should take ownership
    /// of <paramref name="data"/>.</param>
    /// <remarks>
    /// Local time is the effective timezone of the new instance.
    /// </remarks>
    public FatFileSystem(Stream data, Ownership ownsData)
        : base(new FatFileSystemOptions())
    {
        _dirCache = [];
        _timeConverter = DefaultTimeConverter;
        Initialize(data);
        _ownsData = ownsData;
    }

    /// <summary>
    /// Initializes a new instance of the FatFileSystem class.
    /// </summary>
    /// <param name="data">The stream containing the file system.</param>
    /// <param name="timeConverter">A delegate to convert to/from the file system's timezone.</param>
    public FatFileSystem(Stream data, TimeConverter timeConverter)
        : base(new FatFileSystemOptions())
    {
        _dirCache = [];
        _timeConverter = timeConverter;
        Initialize(data);
    }

    /// <summary>
    /// Initializes a new instance of the FatFileSystem class.
    /// </summary>
    /// <param name="data">The stream containing the file system.</param>
    /// <param name="ownsData">Indicates if the new instance should take ownership
    /// of <paramref name="data"/>.</param>
    /// <param name="timeConverter">A delegate to convert to/from the file system's timezone.</param>
    public FatFileSystem(Stream data, Ownership ownsData, TimeConverter timeConverter)
        : base(new FatFileSystemOptions())
    {
        _dirCache = [];
        _timeConverter = timeConverter;
        Initialize(data);
        _ownsData = ownsData;
    }

    /// <summary>
    /// Initializes a new instance of the FatFileSystem class.
    /// </summary>
    /// <param name="data">The stream containing the file system.</param>
    /// <param name="ownsData">Indicates if the new instance should take ownership
    /// of <paramref name="data"/>.</param>
    /// <param name="parameters">The parameters for the file system.</param>
    public FatFileSystem(Stream data, Ownership ownsData, FileSystemParameters parameters)
        : base(new FatFileSystemOptions(parameters))
    {
        _dirCache = [];

        if (parameters != null && parameters.TimeConverter != null)
        {
            _timeConverter = parameters.TimeConverter;
        }
        else
        {
            _timeConverter = DefaultTimeConverter;
        }

        Initialize(data);
        _ownsData = ownsData;
    }

    /// <summary>
    /// Gets the active FAT (zero-based index).
    /// </summary>
    public byte ActiveFat => (byte)((_bpbExtFlags & 0x08) != 0 ? _bpbExtFlags & 0x7 : 0);

    /// <summary>
    /// Gets the Sector location of the backup boot sector (FAT32 only).
    /// </summary>
    public int BackupBootSector => _bpbBkBootSec;

    /// <summary>
    /// Gets the BIOS drive number for BIOS Int 13h calls.
    /// </summary>
    public byte BiosDriveNumber { get; private set; }

    /// <summary>
    /// Gets the number of bytes per sector (as stored in the file-system meta data).
    /// </summary>
    public int SectorSize => _bpbBytesPerSec;

    public long ClusterSize => ClusterReader.ClusterSize;

    public long TotalClusters => TotalSectors / ClusterReader.SectorsPerCluster;

    public long OffsetToCluster(long offset) => offset / ClusterReader.SectorsPerCluster;

    public long ClusterToOffset(long cluster) => cluster * ClusterReader.SectorsPerCluster;

    /// <summary>
    /// Indicates if this file system is read-only or read-write.
    /// </summary>
    /// <returns>.</returns>
    public override bool CanWrite => _data.CanWrite;

    internal ClusterReader ClusterReader { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the VolumeId, VolumeLabel and FileSystemType fields are valid.
    /// </summary>
    public bool ExtendedBootSignaturePresent => _bsBootSig == 0x29;

    internal FileAllocationTable Fat { get; private set; }

    /// <summary>
    /// Gets the number of FATs present.
    /// </summary>
    public byte FatCount { get; private set; }

    /// <summary>
    /// Gets the FAT file system options, which can be modified.
    /// </summary>
    public FatFileSystemOptions FatOptions => (FatFileSystemOptions)Options;

    /// <summary>
    /// Gets the size of a single FAT, in sectors.
    /// </summary>
    public long FatSize => _bpbFATSz16 != 0 ? _bpbFATSz16 : _bpbFATSz32;

    /// <summary>
    /// Gets the FAT variant of the file system.
    /// </summary>
    public FatType FatVariant { get; private set; }

    /// <summary>
    /// Gets the (informational only) file system type recorded in the meta-data.
    /// </summary>
    public string FileSystemType { get; private set; }

    /// <summary>
    /// Gets the friendly name for the file system, including FAT variant.
    /// </summary>
    public override string FriendlyName => FatVariant switch
    {
        FatType.Fat12 => "Microsoft FAT12",
        FatType.Fat16 => "Microsoft FAT16",
        FatType.Fat32 => "Microsoft FAT32",
        _ => "Unknown FAT",
    };

    /// <summary>
    /// Gets the sector location of the FSINFO structure (FAT32 only).
    /// </summary>
    public int FSInfoSector => _bpbFSInfo;

    /// <summary>
    /// Gets the number of logical heads.
    /// </summary>
    public int Heads => _bpbNumHeads;

    /// <summary>
    /// Gets the number of hidden sectors, hiding partition tables, etc.
    /// </summary>
    public long HiddenSectors => _bpbHiddSec;

    /// <summary>
    /// Gets the maximum number of root directory entries (on FAT variants that have a limit).
    /// </summary>
    public int MaxRootDirectoryEntries => _bpbRootEntCnt;

    /// <summary>
    /// Gets the Media marker byte, which indicates fixed or removable media.
    /// </summary>
    public byte Media { get; private set; }

    /// <summary>
    /// Gets a value indicating whether FAT changes are mirrored to all copies of the FAT.
    /// </summary>
    public bool MirrorFat => (_bpbExtFlags & 0x08) == 0;

    /// <summary>
    /// Gets the OEM name from the file system.
    /// </summary>
    public string OemName { get; private set; }

    /// <summary>
    /// Gets the number of reserved sectors at the start of the disk.
    /// </summary>
    public int ReservedSectorCount => _bpbRsvdSecCnt;

    /// <summary>
    /// Gets the cluster number of the first cluster of the root directory (FAT32 only).
    /// </summary>
    public long RootDirectoryCluster => _bpbRootClus;

    /// <summary>
    /// Gets the number of contiguous sectors that make up one cluster.
    /// </summary>
    public byte SectorsPerCluster { get; private set; }

    /// <summary>
    /// Gets the number of sectors per logical track.
    /// </summary>
    public int SectorsPerTrack => _bpbSecPerTrk;

    /// <summary>
    /// Gets the total number of sectors on the disk.
    /// </summary>
    public long TotalSectors => _bpbTotSec16 != 0 ? _bpbTotSec16 : _bpbTotSec32;

    /// <summary>
    /// Gets the file-system version (usually 0).
    /// </summary>
    public int Version => _bpbFSVer;

    /// <summary>
    /// Gets the volume serial number.
    /// </summary>
    public override uint VolumeId => _bsVolId;

    /// <summary>
    /// Gets the volume label.
    /// </summary>
    public override string VolumeLabel
    {
        get
        {
            var volId = _rootDir.FindVolumeId();
            if (volId < 0)
            {
                return _bsVolLab;
            }

            return _rootDir.GetEntry(volId).Name.GetRawName(FatOptions.FileNameEncoding);
        }
    }

    /// <summary>
    /// Detects if a stream contains a FAT file system.
    /// </summary>
    /// <param name="stream">The stream to inspect.</param>
    /// <returns><c>true</c> if the stream appears to be a FAT file system, else <c>false</c>.</returns>
    public static bool Detect(Stream stream)
    {
        if (stream.Length < 512)
        {
            return false;
        }

        stream.Position = 0;
        Span<byte> bytes = stackalloc byte[512];
        stream.ReadExactly(bytes);
        var bpbBytesPerSec = EndianUtilities.ToUInt16LittleEndian(bytes.Slice(11));
        if (bpbBytesPerSec != 512)
        {
            return false;
        }

        var bpbNumFATs = bytes[16];
        if (bpbNumFATs is 0 or > 2)
        {
            return false;
        }

        var bpbTotSec16 = EndianUtilities.ToUInt16LittleEndian(bytes.Slice(19));
        var bpbTotSec32 = EndianUtilities.ToUInt32LittleEndian(bytes.Slice(32));

        if (!((bpbTotSec16 == 0) ^ (bpbTotSec32 == 0)))
        {
            return false;
        }

        var totalSectors = bpbTotSec16 + bpbTotSec32;
        return totalSectors * (long)bpbBytesPerSec <= stream.Length;
    }

    /// <summary>
    /// Opens a file for reading and/or writing.
    /// </summary>
    /// <param name="path">The full path to the file.</param>
    /// <param name="mode">The file mode.</param>
    /// <param name="access">The desired access.</param>
    /// <returns>The stream to the opened file.</returns>
    public override SparseStream OpenFile(string path, FileMode mode, FileAccess access)
    {
        Directory parent;
        long entryId;
        try
        {
            entryId = GetDirectoryEntry(_rootDir, path, out parent);
        }
        catch (ArgumentException)
        {
            throw new IOException($"Invalid path: {path}");
        }

        if (parent == null)
        {
            throw new FileNotFoundException("Could not locate file", path);
        }

        if (entryId < 0)
        {
            return parent.OpenFile(FileName.FromPath(path, FatOptions.FileNameEncoding), mode, access);
        }

        var dirEntry = parent.GetEntry(entryId);

        if ((dirEntry.Attributes & FatAttributes.Directory) != 0)
        {
            throw new IOException("Attempt to open directory as a file");
        }

        return parent.OpenFile(dirEntry.Name, mode, access);
    }

    public IEnumerable<StreamExtent> PathToExtents(string path)
        => PathToClusters(path).Select(range => new StreamExtent(range.Offset * ClusterSize, range.Count * ClusterSize));

    public IEnumerable<Range<long, long>> PathToClusters(string path)
    {
        var stream = (FatFileStream)OpenFile(path, FileMode.Open, FileAccess.Read);
        return stream.EnumerateAllocatedClusters();
    }

    public long GetAllocatedClustersCount(string path)
        => PathToClusters(path).Sum(range => range.Count);

    public long GetFileId(string path)
    {
        // Simulate a root directory entry - doesn't really exist though
        if (IsRootPath(path))
        {
            return 0;
        }

        var dirEntry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("No such file", path);

        // Luckily, FAT and .NET FileAttributes match, bit-for-bit
        return dirEntry.FirstCluster;
    }

    /// <summary>
    /// Gets the attributes of a file or directory.
    /// </summary>
    /// <param name="path">The file or directory to inspect.</param>
    /// <returns>The attributes of the file or directory.</returns>
    public override FileAttributes GetAttributes(string path)
    {
        // Simulate a root directory entry - doesn't really exist though
        if (IsRootPath(path))
        {
            return FileAttributes.Directory;
        }

        var dirEntry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("No such file", path);

        // Luckily, FAT and .NET FileAttributes match, bit-for-bit
        return (FileAttributes)dirEntry.Attributes;
    }

    /// <summary>
    /// Sets the attributes of a file or directory.
    /// </summary>
    /// <param name="path">The file or directory to change.</param>
    /// <param name="newValue">The new attributes of the file or directory.</param>
    public override void SetAttributes(string path, FileAttributes newValue)
    {
        if (IsRootPath(path))
        {
            if (newValue != FileAttributes.Directory)
            {
                throw new IOException("The attributes of the root directory cannot be modified");
            }

            return;
        }

        var id = GetDirectoryEntry(path, out var parent);
        var dirEntry = parent.GetEntry(id);

        var newFatAttr = (FatAttributes)newValue;

        if ((newFatAttr & FatAttributes.Directory) != (dirEntry.Attributes & FatAttributes.Directory))
        {
            throw new ArgumentException("Attempted to change the directory attribute");
        }

        dirEntry.Attributes = newFatAttr;
        parent.UpdateEntry(id, dirEntry);

        // For directories, need to update their 'self' entry also
        if ((dirEntry.Attributes & FatAttributes.Directory) != 0)
        {
            var dir = GetDirectory(path);
            dirEntry = dir.SelfEntry;
            dirEntry.Attributes = newFatAttr;
            dir.SelfEntry = dirEntry;
        }
    }

    /// <summary>
    /// Gets the standard file information for a file.
    /// </summary>
    /// <param name="path">The full path to the file or directory to query.</param>
    /// <returns>The standard file information.</returns>
    public WindowsFileInformation GetFileStandardInformation(string path)
    {
        // Simulate a root directory entry - doesn't really exist though
        if (IsRootPath(path))
        {
            return new WindowsFileInformation
            {
                FileAttributes = FileAttributes.Directory,
                ChangeTime = Epoch,
                CreationTime = Epoch,
                LastAccessTime = Epoch,
                LastWriteTime = Epoch
            };
        }

        var dirEntry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("No such file", path);

        return new WindowsFileInformation
        {
            CreationTime = dirEntry.CreationTime,
            LastAccessTime = dirEntry.LastAccessTime,
            ChangeTime = dirEntry.LastWriteTime,
            LastWriteTime = dirEntry.LastWriteTime,
            FileAttributes = (FileAttributes)dirEntry.Attributes
        };
    }

    /// <summary>
    /// Sets the standard file information for a file.
    /// </summary>
    /// <param name="path">The full path to the file or directory to query.</param>
    /// <param name="info">The standard file information.</param>
    public void SetFileStandardInformation(string path, WindowsFileInformation info)
    {
        // Simulate a root directory entry - doesn't really exist though
        if (IsRootPath(path))
        {
            throw new IOException("Root directory cannot be modified");
        }

        var dirEntry = GetDirectoryEntry(path)
            ?? throw new FileNotFoundException("No such file", path);

        dirEntry.CreationTime = info.CreationTime;
        dirEntry.LastAccessTime = info.LastAccessTime;
        dirEntry.LastWriteTime = info.LastWriteTime;
        dirEntry.Attributes = (FatAttributes)info.FileAttributes;
    }

    public string GetShortName(string path)
    {
        if (IsRootPath(path))
        {
            return null;
        }

        return GetDirectoryEntry(path).Name.GetShortName(FatOptions.FileNameEncoding);
    }

    public void SetShortName(string path, string name)
    {
        if (IsRootPath(path))
        {
            throw new InvalidOperationException("Cannot set short name on root directory");
        }

        GetDirectoryEntry(path).Name.SetShortName(name, FatOptions.FileNameEncoding);
    }

    /// <summary>
    /// Gets the creation time (in local time) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The creation time.</returns>
    public override DateTime GetCreationTime(string path)
    {
        if (IsRootPath(path))
        {
            return Epoch;
        }

        return GetDirectoryEntry(path).CreationTime;
    }

    /// <summary>
    /// Sets the creation time (in local time) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetCreationTime(string path, DateTime newTime)
    {
        if (IsRootPath(path))
        {
            if (newTime != Epoch)
            {
                throw new IOException("The creation time of the root directory cannot be modified");
            }

            return;
        }

        UpdateDirEntry(path, e => { e.CreationTime = newTime; });
    }

    /// <summary>
    /// Gets the creation time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The creation time.</returns>
    public override DateTime GetCreationTimeUtc(string path)
    {
        if (IsRootPath(path))
        {
            return ConvertToUtc(Epoch);
        }

        return ConvertToUtc(GetDirectoryEntry(path).CreationTime);
    }

    /// <summary>
    /// Sets the creation time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetCreationTimeUtc(string path, DateTime newTime)
    {
        if (IsRootPath(path))
        {
            if (ConvertFromUtc(newTime) != Epoch)
            {
                throw new IOException("The last write time of the root directory cannot be modified");
            }

            return;
        }

        UpdateDirEntry(path, e => { e.CreationTime = ConvertFromUtc(newTime); });
    }

    /// <summary>
    /// Gets the last access time (in local time) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The time the file or directory was last accessed.</returns>
    public override DateTime GetLastAccessTime(string path)
    {
        if (IsRootPath(path))
        {
            return Epoch;
        }

        return GetDirectoryEntry(path).LastAccessTime;
    }

    /// <summary>
    /// Sets the last access time (in local time) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetLastAccessTime(string path, DateTime newTime)
    {
        if (IsRootPath(path))
        {
            if (newTime != Epoch)
            {
                throw new IOException("The last access time of the root directory cannot be modified");
            }

            return;
        }

        UpdateDirEntry(path, e => { e.LastAccessTime = newTime; });
    }

    /// <summary>
    /// Gets the last access time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The time the file or directory was last accessed.</returns>
    public override DateTime GetLastAccessTimeUtc(string path)
    {
        if (IsRootPath(path))
        {
            return ConvertToUtc(Epoch);
        }

        return ConvertToUtc(GetDirectoryEntry(path).LastAccessTime);
    }

    /// <summary>
    /// Sets the last access time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetLastAccessTimeUtc(string path, DateTime newTime)
    {
        if (IsRootPath(path))
        {
            if (ConvertFromUtc(newTime) != Epoch)
            {
                throw new IOException("The last write time of the root directory cannot be modified");
            }

            return;
        }

        UpdateDirEntry(path, e => { e.LastAccessTime = ConvertFromUtc(newTime); });
    }

    /// <summary>
    /// Gets the last modification time (in local time) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The time the file or directory was last modified.</returns>
    public override DateTime GetLastWriteTime(string path)
    {
        if (IsRootPath(path))
        {
            return Epoch;
        }

        return GetDirectoryEntry(path).LastWriteTime;
    }

    /// <summary>
    /// Sets the last modification time (in local time) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetLastWriteTime(string path, DateTime newTime)
    {
        if (IsRootPath(path))
        {
            if (newTime != Epoch)
            {
                throw new IOException("The last write time of the root directory cannot be modified");
            }

            return;
        }

        UpdateDirEntry(path, e => { e.LastWriteTime = newTime; });
    }

    /// <summary>
    /// Gets the last modification time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <returns>The time the file or directory was last modified.</returns>
    public override DateTime GetLastWriteTimeUtc(string path)
    {
        if (IsRootPath(path))
        {
            return ConvertToUtc(Epoch);
        }

        return ConvertToUtc(GetDirectoryEntry(path).LastWriteTime);
    }

    /// <summary>
    /// Sets the last modification time (in UTC) of a file or directory.
    /// </summary>
    /// <param name="path">The path of the file or directory.</param>
    /// <param name="newTime">The new time to set.</param>
    public override void SetLastWriteTimeUtc(string path, DateTime newTime)
    {
        if (IsRootPath(path))
        {
            if (ConvertFromUtc(newTime) != Epoch)
            {
                throw new IOException("The last write time of the root directory cannot be modified");
            }

            return;
        }

        UpdateDirEntry(path, e => { e.LastWriteTime = ConvertFromUtc(newTime); });
    }

    /// <summary>
    /// Gets the length of a file.
    /// </summary>
    /// <param name="path">The path to the file.</param>
    /// <returns>The length in bytes.</returns>
    public override long GetFileLength(string path)
    {
        return GetDirectoryEntry(path)?.FileSize ??
            throw new FileNotFoundException("File not found", path);
    }

    public override DiscFileSystemInfo GetFileSystemInfo(string path)
    {
        try
        {
            var dirEntry = GetDirectoryEntry(path);
            if (dirEntry != null)
            {
                if (dirEntry.Attributes.HasFlag(FatAttributes.Directory))
                {
                    return new CachedDiscDirectoryInfo(this, path, (FileAttributes)dirEntry.Attributes,
                                                    ConvertToUtc(dirEntry.CreationTime), ConvertToUtc(dirEntry.LastAccessTime),
                                                    ConvertToUtc(dirEntry.LastWriteTime));
                }
                else
                {
                    return new CachedDiscFileInfo(this, path, (FileAttributes)dirEntry.Attributes, ConvertToUtc(dirEntry.CreationTime),
                                               ConvertToUtc(dirEntry.LastAccessTime), ConvertToUtc(dirEntry.LastWriteTime),
                                               dirEntry.FileSize);
                }
            }
        }
        catch
        {
        }

        return new(this, path);
    }

    public override DiscFileInfo GetFileInfo(string path)
    {
        try
        {
            var dirEntry = GetDirectoryEntry(path);
            if (dirEntry != null && !dirEntry.Attributes.HasFlag(FatAttributes.Directory))
            {
                return new CachedDiscFileInfo(this, path, (FileAttributes)dirEntry.Attributes, ConvertToUtc(dirEntry.CreationTime),
                                           ConvertToUtc(dirEntry.LastAccessTime), ConvertToUtc(dirEntry.LastWriteTime),
                                           dirEntry.FileSize);
            }
        }
        catch
        {
        }

        return new(this, path);
    }

    public override DiscDirectoryInfo GetDirectoryInfo(string path)
    {
        try
        {
            var dirEntry = GetDirectoryEntry(path);
            if (dirEntry != null)
            {
                return new CachedDiscDirectoryInfo(this, path, (FileAttributes)dirEntry.Attributes,
                                                ConvertToUtc(dirEntry.CreationTime), ConvertToUtc(dirEntry.LastAccessTime),
                                                ConvertToUtc(dirEntry.LastWriteTime));
            }
        }
        catch
        {
        }

        return new(this, path);
    }

    /// <summary>
    /// Copies an existing file to a new file, allowing overwriting of an existing file.
    /// </summary>
    /// <param name="sourceFile">The source file.</param>
    /// <param name="destinationFile">The destination file.</param>
    /// <param name="overwrite">Whether to permit over-writing of an existing file.</param>
    public override void CopyFile(string sourceFile, string destinationFile, bool overwrite)
    {
        var sourceEntryId = GetDirectoryEntry(sourceFile, out var sourceDir);

        if (sourceDir == null || sourceEntryId < 0)
        {
            throw new FileNotFoundException($"The source file '{sourceFile}' was not found");
        }

        var sourceEntry = sourceDir.GetEntry(sourceEntryId);

        if ((sourceEntry.Attributes & FatAttributes.Directory) != 0)
        {
            throw new IOException("The source file is a directory");
        }

        var newEntry = new DirectoryEntry(sourceEntry)
        {
            Name = FileName.FromPath(destinationFile, FatOptions.FileNameEncoding),
            FirstCluster = 0
        };

        var destEntryId = GetDirectoryEntry(destinationFile, out var destDir);

        if (destDir == null)
        {
            throw new DirectoryNotFoundException($"The destination directory for '{destinationFile}' was not found");
        }

        // If the destination is a directory, use the old file name to construct a full path.
        if (destEntryId >= 0)
        {
            var destEntry = destDir.GetEntry(destEntryId);
            if ((destEntry.Attributes & FatAttributes.Directory) != 0)
            {
                newEntry.Name = FileName.FromPath(sourceFile, FatOptions.FileNameEncoding);
                destinationFile = Utilities.CombinePaths(destinationFile, Utilities.GetFileFromPath(sourceFile));

                destEntryId = GetDirectoryEntry(destinationFile, out destDir);
            }
        }

        // If there's an existing entry...
        if (destEntryId >= 0)
        {
            var destEntry = destDir.GetEntry(destEntryId);

            if ((destEntry.Attributes & FatAttributes.Directory) != 0)
            {
                throw new IOException("Destination file is an existing directory");
            }

            if (!overwrite)
            {
                throw new IOException("Destination file already exists");
            }

            // Remove the old file
            destDir.DeleteEntry(destEntryId, true);
        }

        // Add the new file's entry
        destEntryId = destDir.AddEntry(newEntry);

        // Copy the contents...
        using Stream sourceStream = new FatFileStream(this, sourceDir, sourceEntryId, FileAccess.Read),
                      destStream = new FatFileStream(this, destDir, destEntryId, FileAccess.Write);
        sourceStream.CopyTo(destStream);
    }

    /// <summary>
    /// Creates a directory.
    /// </summary>
    /// <param name="path">The directory to create.</param>
    public override void CreateDirectory(string path)
    {
        var pathElements = path.AsMemory().Split('\\', '/', StringSplitOptions.RemoveEmptyEntries);

        var focusDir = _rootDir;

        foreach (var pathElement in pathElements)
        {
            FileName name;
            try
            {
                name = new FileName(pathElement.ToString(), FatOptions.FileNameEncoding);
            }
            catch (ArgumentException ae)
            {
                throw new IOException("Invalid path", ae);
            }

            var child = focusDir.GetChildDirectory(name) ?? focusDir.CreateChildDirectory(name);
            focusDir = child;
        }
    }

    /// <summary>
    /// Deletes a directory, optionally with all descendants.
    /// </summary>
    /// <param name="path">The path of the directory to delete.</param>
    public override void DeleteDirectory(string path)
    {
        var dir = GetDirectory(path)
            ?? throw new DirectoryNotFoundException($"No such directory: {path}");

        if (!dir.IsEmpty)
        {
            throw new IOException("Unable to delete non-empty directory");
        }

        var id = GetDirectoryEntry(path, out var parent);
        if (parent == null && id == 0)
        {
            throw new IOException("Unable to delete root directory");
        }

        if (parent != null && id >= 0)
        {
            var deadEntry = parent.GetEntry(id);
            parent.DeleteEntry(id, true);
            ForgetDirectory(deadEntry);
        }
        else
        {
            throw new DirectoryNotFoundException($"No such directory: {path}");
        }
    }

    /// <summary>
    /// Deletes a file.
    /// </summary>
    /// <param name="path">The path of the file to delete.</param>
    public override void DeleteFile(string path)
    {
        var id = GetDirectoryEntry(path, out var parent);
        if (parent == null || id < 0)
        {
            throw new FileNotFoundException("No such file", path);
        }

        var entry = parent.GetEntry(id);
        if (entry == null || (entry.Attributes & FatAttributes.Directory) != 0)
        {
            throw new FileNotFoundException("No such file", path);
        }

        parent.DeleteEntry(id, true);
    }

    /// <summary>
    /// Indicates if a directory exists.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>true if the directory exists.</returns>
    public override bool DirectoryExists(string path)
    {
        // Special case - root directory
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        var dirEntry = GetDirectoryEntry(path);
        return dirEntry != null && (dirEntry.Attributes & FatAttributes.Directory) != 0;
    }

    /// <summary>
    /// Indicates if a file exists.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>true if the file exists.</returns>
    public override bool FileExists(string path)
    {
        // Special case - root directory
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var dirEntry = GetDirectoryEntry(path);
        return dirEntry != null && (dirEntry.Attributes & FatAttributes.Directory) == 0;
    }

    /// <summary>
    /// Indicates if a file or directory exists.
    /// </summary>
    /// <param name="path">The path to test.</param>
    /// <returns>true if the file or directory exists.</returns>
    public override bool Exists(string path)
    {
        // Special case - root directory
        if (string.IsNullOrEmpty(path))
        {
            return true;
        }

        return GetDirectoryEntry(path) != null;
    }

    /// <summary>
    /// Gets the names of subdirectories in a specified directory.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <returns>Array of directories.</returns>
    public override IEnumerable<string> GetDirectories(string path)
    {
        var dir = GetDirectory(path)
            ?? throw new DirectoryNotFoundException($"The directory '{path}' was not found");

        var entries = dir.GetDirectories();

        foreach (var dirEntry in entries)
        {
            yield return Utilities.CombinePaths(path, dirEntry.Name.GetDisplayName(FatOptions.FileNameEncoding));
        }
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
        var re = Utilities.ConvertWildcardsToRegEx(searchPattern, ignoreCase: true);

        var dirs = DoSearch(path, re, searchOption == SearchOption.AllDirectories, true, false);
        return dirs;
    }

    /// <summary>
    /// Gets the names of files in a specified directory.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <returns>Array of files.</returns>
    public override IEnumerable<string> GetFiles(string path)
    {
        var dir = GetDirectory(path);
        var entries = dir.GetFiles();

        foreach (var dirEntry in entries)
        {
            yield return Utilities.CombinePaths(path, dirEntry.Name.GetDisplayName(FatOptions.FileNameEncoding));
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
        var filter = Utilities.ConvertWildcardsToRegEx(searchPattern, ignoreCase: true);

        var results = DoSearch(path, filter, searchOption == SearchOption.AllDirectories, false, true);
        return results;
    }

    /// <summary>
    /// Gets the names of all files and subdirectories in a specified directory.
    /// </summary>
    /// <param name="path">The path to search.</param>
    /// <returns>Array of files and subdirectories matching the search pattern.</returns>
    public override IEnumerable<string> GetFileSystemEntries(string path)
    {
        var dir = GetDirectory(path);
        var entries = dir.Entries;

        foreach (var dirEntry in entries)
        {
            yield return Utilities.CombinePaths(path, dirEntry.Name.GetDisplayName(FatOptions.FileNameEncoding));
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
        var re = Utilities.ConvertWildcardsToRegEx(searchPattern, ignoreCase: true);

        var dir = GetDirectory(path);
        var entries = dir.Entries;

        foreach (var dirEntry in entries)
        {
            if (re is null || dirEntry.Name.IsMatch(re, FatOptions.FileNameEncoding))
            {
                yield return Utilities.CombinePaths(path, dirEntry.Name.GetDisplayName(FatOptions.FileNameEncoding));
            }
        }
    }

    /// <summary>
    /// Moves a directory.
    /// </summary>
    /// <param name="sourceDirectoryName">The directory to move.</param>
    /// <param name="destinationDirectoryName">The target directory name.</param>
    public override void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(nameof(destinationDirectoryName));
#else
        if (string.IsNullOrEmpty(destinationDirectoryName))
        {
            if (destinationDirectoryName == null)
            {
                throw new ArgumentNullException(nameof(destinationDirectoryName));
            }

            throw new ArgumentException("Invalid destination name (empty string)");
        }
#endif

        var destId = GetDirectoryEntry(destinationDirectoryName, out var destParent);
        if (destParent == null)
        {
            throw new DirectoryNotFoundException("Target directory doesn't exist");
        }

        if (destId >= 0)
        {
            throw new IOException("Target directory already exists");
        }

        var sourceId = GetDirectoryEntry(sourceDirectoryName, out var sourceParent);
        if (sourceParent == null || sourceId < 0)
        {
            throw new IOException("Source directory doesn't exist");
        }

        destParent.AttachChildDirectory(FileName.FromPath(destinationDirectoryName, FatOptions.FileNameEncoding),
            GetDirectory(sourceDirectoryName));

        sourceParent.DeleteEntry(sourceId, false);
    }

    /// <summary>
    /// Moves a file, allowing an existing file to be overwritten.
    /// </summary>
    /// <param name="sourceName">The file to move.</param>
    /// <param name="destinationName">The target file name.</param>
    /// <param name="overwrite">Whether to permit a destination file to be overwritten.</param>
    public override void MoveFile(string sourceName, string destinationName, bool overwrite)
    {
        var sourceEntryId = GetDirectoryEntry(sourceName, out var sourceDir);

        if (sourceDir == null || sourceEntryId < 0)
        {
            throw new FileNotFoundException($"The source file '{sourceName}' was not found");
        }

        var sourceEntry = sourceDir.GetEntry(sourceEntryId);

        if ((sourceEntry.Attributes & FatAttributes.Directory) != 0)
        {
            throw new IOException("The source file is a directory");
        }

        var newEntry = new DirectoryEntry(sourceEntry)
        {
            Name = FileName.FromPath(destinationName, FatOptions.FileNameEncoding)
        };

        var destEntryId = GetDirectoryEntry(destinationName, out var destDir);

        if (destDir == null)
        {
            throw new DirectoryNotFoundException($"The destination directory for '{destinationName}' was not found");
        }

        // If the destination is a directory, use the old file name to construct a full path.
        if (destEntryId >= 0)
        {
            var destEntry = destDir.GetEntry(destEntryId);
            if ((destEntry.Attributes & FatAttributes.Directory) != 0)
            {
                newEntry.Name = FileName.FromPath(sourceName, FatOptions.FileNameEncoding);
                destinationName = Utilities.CombinePaths(destinationName, Utilities.GetFileFromPath(sourceName));

                destEntryId = GetDirectoryEntry(destinationName, out destDir);
            }
        }

        // If there's an existing entry...
        if (destEntryId >= 0)
        {
            var destEntry = destDir.GetEntry(destEntryId);

            if ((destEntry.Attributes & FatAttributes.Directory) != 0)
            {
                throw new IOException("Destination file is an existing directory");
            }

            if (!overwrite)
            {
                throw new IOException("Destination file already exists");
            }

            // Remove the old file
            destDir.DeleteEntry(destEntryId, true);
        }

        // Add the new file's entry and remove the old link to the file's contents
        destDir.AddEntry(newEntry);
        sourceDir.DeleteEntry(sourceEntryId, false);
    }

    internal DateTime ConvertToUtc(DateTime dateTime)
    {
        return _timeConverter(dateTime, true);
    }

    internal DateTime ConvertFromUtc(DateTime dateTime)
    {
        return _timeConverter(dateTime, false);
    }

    internal Directory GetDirectory(string path)
    {

        if (string.IsNullOrEmpty(path) || path == @"\" || path == "/")
        {
            return _rootDir;
        }

        var id = GetDirectoryEntry(_rootDir, path, out var parent);
        if (id >= 0)
        {
            return GetDirectory(parent, id);
        }

        return null;
    }

    internal Directory GetDirectory(Directory parent, long parentId)
    {
        if (parent == null)
        {
            return _rootDir;
        }

        var dirEntry = parent.GetEntry(parentId);
        if ((dirEntry.Attributes & FatAttributes.Directory) == 0)
        {
            throw new DirectoryNotFoundException();
        }

        // If we have this one cached, return it
        if (_dirCache.TryGetValue(dirEntry.FirstCluster, out var result))
        {
            return result;
        }

        // Not cached - create a new one.
        result = new Directory(parent, parentId);
        _dirCache[dirEntry.FirstCluster] = result;
        return result;
    }

    internal void ForgetDirectory(DirectoryEntry entry)
    {
        var index = entry.FirstCluster;
        if (index != 0 && _dirCache.TryGetValue(index, out var dir))
        {
            _dirCache.Remove(index);
            dir.Dispose();
        }
    }

    internal DirectoryEntry GetDirectoryEntry(string path)
    {

        var id = GetDirectoryEntry(_rootDir, path, out var parent);

        if (parent == null || id < 0)
        {
            return null;
        }

        return parent.GetEntry(id);
    }

    internal long GetDirectoryEntry(string path, out Directory parent)
    {
        return GetDirectoryEntry(_rootDir, path, out parent);
    }

    /// <summary>
    /// Disposes of this instance.
    /// </summary>
    /// <param name="disposing">The value <c>true</c> if Disposing.</param>
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                foreach (var dir in _dirCache.Values)
                {
                    dir.Dispose();
                }

                _rootDir.Dispose();

                if (_ownsData == Ownership.Dispose)
                {
                    _data.Dispose();
                    _data = null;
                }
            }
        }
        finally
        {
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Writes a FAT12/FAT16 BPB.
    /// </summary>
    /// <param name="bootSector">The buffer to fill.</param>
    /// <param name="sectors">The total capacity of the disk (in sectors).</param>
    /// <param name="fatType">The number of bits in each FAT entry.</param>
    /// <param name="maxRootEntries">The maximum number of root directory entries.</param>
    /// <param name="hiddenSectors">The number of hidden sectors before this file system (i.e. partition offset).</param>
    /// <param name="reservedSectors">The number of reserved sectors before the FAT.</param>
    /// <param name="sectorsPerCluster">The number of sectors per cluster.</param>
    /// <param name="diskGeometry">The geometry of the disk containing the Fat file system.</param>
    /// <param name="isFloppy">Indicates if the disk is a removable media (a floppy disk).</param>
    /// <param name="volId">The disk's volume Id.</param>
    /// <param name="label">The disk's label (or null).</param>
    private static void WriteBPB(
        Span<byte> bootSector,
        uint sectors,
        FatType fatType,
        ushort maxRootEntries,
        uint hiddenSectors,
        ushort reservedSectors,
        byte sectorsPerCluster,
        Geometry diskGeometry,
        bool isFloppy,
        uint volId,
        string label)
    {
        var fatSectors = CalcFatSize(sectors, fatType, sectorsPerCluster);

        bootSector[0] = 0xEB;
        bootSector[1] = 0x3C;
        bootSector[2] = 0x90;

        // OEM Name
        "DISCUTIL"u8.CopyTo(bootSector.Slice(3, 8));

        // Bytes Per Sector (512)
        bootSector[11] = 0;
        bootSector[12] = 2;

        // Sectors Per Cluster
        bootSector[13] = sectorsPerCluster;

        // Reserved Sector Count
        EndianUtilities.WriteBytesLittleEndian(reservedSectors, bootSector.Slice(14));

        // Number of FATs
        bootSector[16] = 2;

        // Number of Entries in the root directory
        EndianUtilities.WriteBytesLittleEndian(maxRootEntries, bootSector.Slice(17));

        // Total number of sectors (small)
        EndianUtilities.WriteBytesLittleEndian((ushort)(sectors < 0x10000 ? sectors : 0), bootSector.Slice(19));

        // Media
        bootSector[21] = (byte)(isFloppy ? 0xF0 : 0xF8);

        // FAT size (FAT12/FAT16)
        EndianUtilities.WriteBytesLittleEndian((ushort)(fatType < FatType.Fat32 ? fatSectors : 0), bootSector.Slice(22));

        // Sectors Per Track
        EndianUtilities.WriteBytesLittleEndian((ushort)diskGeometry.SectorsPerTrack, bootSector.Slice(24));

        // Heads Per Cylinder
        EndianUtilities.WriteBytesLittleEndian((ushort)diskGeometry.HeadsPerCylinder, bootSector.Slice(26));

        // Hidden Sectors
        EndianUtilities.WriteBytesLittleEndian(hiddenSectors, bootSector.Slice(28));

        // Total number of sectors (large)
        EndianUtilities.WriteBytesLittleEndian(sectors >= 0x10000 ? sectors : 0, bootSector.Slice(32));

        if (fatType < FatType.Fat32)
        {
            WriteBS(bootSector.Slice(36), isFloppy, volId, label, fatType);
        }
        else
        {
            // FAT size (FAT32)
            EndianUtilities.WriteBytesLittleEndian(fatSectors, bootSector.Slice(36));

            // Ext flags: 0x80 = FAT 1 (i.e. Zero) active, mirroring
            bootSector[40] = 0x00;
            bootSector[41] = 0x00;

            // Filesystem version (0.0)
            bootSector[42] = 0;
            bootSector[43] = 0;

            // First cluster of the root directory, always 2 since we don't do bad sectors...
            EndianUtilities.WriteBytesLittleEndian((uint)2, bootSector.Slice(44));

            // Sector number of FSINFO
            EndianUtilities.WriteBytesLittleEndian((uint)1, bootSector.Slice(48));

            // Sector number of the Backup Boot Sector
            EndianUtilities.WriteBytesLittleEndian((uint)6, bootSector.Slice(50));

            // Reserved area - must be set to 0
            bootSector.Slice(52, 12).Clear();

            WriteBS(bootSector.Slice(64), isFloppy, volId, label, fatType);
        }

        bootSector[510] = 0x55;
        bootSector[511] = 0xAA;
    }

    private static uint CalcFatSize(uint sectors, FatType fatType, byte sectorsPerCluster)
    {
        var numClusters = sectors / sectorsPerCluster;
        var fatBytes = numClusters * (ushort)fatType / 8;
        return (fatBytes + Sizes.Sector - 1) / Sizes.Sector;
    }

    private static void WriteBS(Span<byte> bootSector, bool isFloppy, uint volId, string label,
                                FatType fatType)
    {
        if (string.IsNullOrEmpty(label))
        {
            label = "NO NAME    ";
        }

        var fsType = "FAT32   ";
        if (fatType == FatType.Fat12)
        {
            fsType = "FAT12   ";
        }
        else if (fatType == FatType.Fat16)
        {
            fsType = "FAT16   ";
        }

        // Drive Number (for BIOS)
        bootSector[0] = (byte)(isFloppy ? 0x00 : 0x80);

        // Reserved
        bootSector[1] = 0;

        // Boot Signature (indicates next 3 fields present)
        bootSector[2] = 0x29;

        // Volume Id
        EndianUtilities.WriteBytesLittleEndian(volId, bootSector.Slice(3));

        var encoding = EncodingUtilities.GetLatin1Encoding();

        // Volume Label
        encoding.GetBytes(label.PadRight(11, ' ').AsSpan(0, 11), bootSector.Slice(7, 11));

        // File System Type
        encoding.GetBytes(fsType, bootSector.Slice(18, 8));
    }

    private static FatType DetectFATType(byte[] bpb)
    {
        uint bpbBytesPerSec = EndianUtilities.ToUInt16LittleEndian(bpb, 11);
        if (bpbBytesPerSec == 0)
        {
            throw new InvalidFileSystemException("Bytes per sector is 0, invalid or corrupt filesystem.");
        }

        uint bpbRootEntCnt = EndianUtilities.ToUInt16LittleEndian(bpb, 17);
        uint bpbFATSz16 = EndianUtilities.ToUInt16LittleEndian(bpb, 22);
        var bpbFATSz32 = EndianUtilities.ToUInt32LittleEndian(bpb, 36);
        uint bpbTotSec16 = EndianUtilities.ToUInt16LittleEndian(bpb, 19);
        var bpbTotSec32 = EndianUtilities.ToUInt32LittleEndian(bpb, 32);
        uint bpbResvdSecCnt = EndianUtilities.ToUInt16LittleEndian(bpb, 14);
        uint bpbNumFATs = bpb[16];
        uint bpbSecPerClus = bpb[13];

        var rootDirSectors = (bpbRootEntCnt * 32 + bpbBytesPerSec - 1) / bpbBytesPerSec;
        var fatSz = bpbFATSz16 != 0 ? bpbFATSz16 : bpbFATSz32;
        var totalSec = bpbTotSec16 != 0 ? bpbTotSec16 : bpbTotSec32;

        var dataSec = totalSec - (bpbResvdSecCnt + bpbNumFATs * fatSz + rootDirSectors);
        var countOfClusters = dataSec / bpbSecPerClus;

        if (countOfClusters < 4085)
        {
            return FatType.Fat12;
        }

        if (countOfClusters < 65525)
        {
            return FatType.Fat16;
        }

        return FatType.Fat32;
    }

    public bool Dirty
    {
        get
        {
            if (FatVariant < FatType.Fat32)
            {
                return (_bootSector[0x25] & 0x01) == 0x01;
            }
            else
            {
                return (_bootSector[0x41] & 0x01) == 0x01;
            }
        }
        set
        {
            if (FatVariant < FatType.Fat32)
            {
                _bootSector[0x25] = (byte)((_bootSector[0x25] & 0xFE) | (value ? 0x01 : 0x00));
            }
            else
            {
                _bootSector[0x41] = (byte)((_bootSector[0x41] & 0xFE) | (value ? 0x01 : 0x00));
            }
        }
    }

    private static bool IsRootPath(string path)
    {
        return string.IsNullOrEmpty(path) || path == @"\" || path == "/";
    }

    private static DateTime DefaultTimeConverter(DateTime time, bool toUtc)
    {
        return toUtc ? time.ToUniversalTime() : time.ToLocalTime();
    }

    private void Initialize(Stream data)
    {
        _data = data;
        _data.Position = 0;
        _bootSector = _data.ReadSector();

        FatVariant = DetectFATType(_bootSector);

        ReadBPB();

        LoadFAT();

        LoadClusterReader();

        LoadRootDirectory();
    }

    private void LoadClusterReader()
    {
        var rootDirSectors = (_bpbRootEntCnt * 32 + (_bpbBytesPerSec - 1)) / _bpbBytesPerSec;
        var firstDataSector = (int)(_bpbRsvdSecCnt + FatCount * FatSize + rootDirSectors);
        ClusterReader = new ClusterReader(_data, firstDataSector, SectorsPerCluster, _bpbBytesPerSec);
    }

    private void LoadRootDirectory()
    {
        Stream fatStream;
        if (FatVariant != FatType.Fat32)
        {
            fatStream = new SubStream(_data, (_bpbRsvdSecCnt + FatCount * _bpbFATSz16) * _bpbBytesPerSec,
                _bpbRootEntCnt * 32);
        }
        else
        {
            fatStream = new ClusterStream(this, FileAccess.ReadWrite, _bpbRootClus, uint.MaxValue);
        }

        _rootDir = new Directory(this, fatStream);
    }

    private void LoadFAT()
    {
        Fat = new FileAllocationTable(FatVariant, _data, _bpbRsvdSecCnt, (uint)FatSize, FatCount, ActiveFat);
    }

    private void ReadBPB()
    {
        OemName = Encoding.ASCII.GetString(_bootSector, 3, 8).TrimEnd('\0');
        _bpbBytesPerSec = EndianUtilities.ToUInt16LittleEndian(_bootSector, 11);
        SectorsPerCluster = _bootSector[13];
        _bpbRsvdSecCnt = EndianUtilities.ToUInt16LittleEndian(_bootSector, 14);
        FatCount = _bootSector[16];
        _bpbRootEntCnt = EndianUtilities.ToUInt16LittleEndian(_bootSector, 17);
        _bpbTotSec16 = EndianUtilities.ToUInt16LittleEndian(_bootSector, 19);
        Media = _bootSector[21];
        _bpbFATSz16 = EndianUtilities.ToUInt16LittleEndian(_bootSector, 22);
        _bpbSecPerTrk = EndianUtilities.ToUInt16LittleEndian(_bootSector, 24);
        _bpbNumHeads = EndianUtilities.ToUInt16LittleEndian(_bootSector, 26);
        _bpbHiddSec = EndianUtilities.ToUInt32LittleEndian(_bootSector, 28);
        _bpbTotSec32 = EndianUtilities.ToUInt32LittleEndian(_bootSector, 32);

        if (FatVariant != FatType.Fat32)
        {
            ReadBS(36);
        }
        else
        {
            _bpbFATSz32 = EndianUtilities.ToUInt32LittleEndian(_bootSector, 36);
            _bpbExtFlags = EndianUtilities.ToUInt16LittleEndian(_bootSector, 40);
            _bpbFSVer = EndianUtilities.ToUInt16LittleEndian(_bootSector, 42);
            _bpbRootClus = EndianUtilities.ToUInt32LittleEndian(_bootSector, 44);
            _bpbFSInfo = EndianUtilities.ToUInt16LittleEndian(_bootSector, 48);
            _bpbBkBootSec = EndianUtilities.ToUInt16LittleEndian(_bootSector, 50);
            ReadBS(64);
        }
    }

    private void ReadBS(int offset)
    {
        BiosDriveNumber = _bootSector[offset];
        _bsBootSig = _bootSector[offset + 2];
        _bsVolId = EndianUtilities.ToUInt32LittleEndian(_bootSector, offset + 3);
        _bsVolLab = Encoding.ASCII.GetString(_bootSector, offset + 7, 11);
        FileSystemType = Encoding.ASCII.GetString(_bootSector, offset + 18, 8);
    }

    private long GetDirectoryEntry(Directory dir, string path, out Directory parent)
    {
        var pathElements = path.AsMemory().Split('\\', '/', StringSplitOptions.RemoveEmptyEntries).ToArray();
        return GetDirectoryEntry(dir, pathElements, 0, out parent);
    }

    private long GetDirectoryEntry(Directory dir, ReadOnlyMemory<char>[] pathEntries, int pathOffset, out Directory parent)
    {
        long entryId;

        if (pathEntries.Length == 0)
        {
            // Looking for root directory, simulate the directory entry in its parent...
            parent = null;
            return 0;
        }

        entryId = dir.FindEntry(new FileName(pathEntries[pathOffset].ToString(), FatOptions.FileNameEncoding));
        if (entryId >= 0)
        {
            if (pathOffset == pathEntries.Length - 1)
            {
                parent = dir;
                return entryId;
            }

            return GetDirectoryEntry(GetDirectory(dir, entryId), pathEntries, pathOffset + 1, out parent);
        }

        if (pathOffset == pathEntries.Length - 1)
        {
            parent = dir;
            return -1;
        }

        parent = null;
        return -1;
    }

    private IEnumerable<string> DoSearch(string path, Func<string, bool> filter, bool subFolders, bool dirs, bool files)
    {
        var dir = GetDirectory(path)
            ?? throw new DirectoryNotFoundException($"The directory '{path}' was not found");

        var entries = dir.Entries;

        foreach (var de in entries)
        {
            var isDir = (de.Attributes & FatAttributes.Directory) != 0;

            if ((isDir && dirs) || (!isDir && files))
            {
                if (filter is null || de.Name.IsMatch(filter, FatOptions.FileNameEncoding))
                {
                    yield return Utilities.CombinePaths(path, de.Name.GetDisplayName(FatOptions.FileNameEncoding));
                }
            }

            if (subFolders && isDir)
            {
                foreach (var subdirentry in DoSearch(Utilities.CombinePaths(path, de.Name.GetDisplayName(FatOptions.FileNameEncoding)),
                    filter, subFolders, dirs, files))
                {
                    yield return subdirentry;
                }
            }
        }
    }

    private void UpdateDirEntry(string path, EntryUpdateAction action)
    {
        var id = GetDirectoryEntry(path, out var parent);
        var entry = parent.GetEntry(id);
        action(entry);
        parent.UpdateEntry(id, entry);

        if ((entry.Attributes & FatAttributes.Directory) != 0)
        {
            var dir = GetDirectory(path);
            var selfEntry = dir.SelfEntry;
            action(selfEntry);
            dir.SelfEntry = selfEntry;
        }
    }

    /// <summary>
    /// Size of the Filesystem in bytes
    /// </summary>
    public override long Size => ((TotalSectors - ReservedSectorCount - (FatSize * FatCount)) * SectorSize);

    /// <summary>
    /// Used space of the Filesystem in bytes
    /// </summary>
    public override long UsedSpace
    {
        get
        {
            uint usedCluster = 0;
            for (uint i = 2; i < Fat.NumEntries; i++)
            {
                var fatValue = Fat.GetNext(i);
                if (!FileAllocationTable.IsFree(fatValue))
                {
                    usedCluster++;
                }
            }

            return (usedCluster *SectorsPerCluster*SectorSize);
        }
    }

    /// <summary>
    /// Available space of the Filesystem in bytes
    /// </summary>
    public override long AvailableSpace => Size - UsedSpace;

    private delegate void EntryUpdateAction(DirectoryEntry entry);

#region Disk Formatting

    /// <summary>
    /// Creates a formatted floppy disk image in a stream.
    /// </summary>
    /// <param name="stream">The stream to write the blank image to.</param>
    /// <param name="type">The type of floppy to create.</param>
    /// <param name="label">The volume label for the floppy (or null).</param>
    /// <returns>An object that provides access to the newly created floppy disk image.</returns>
    public static FatFileSystem FormatFloppy(Stream stream, FloppyDiskType type, string label)
    {
        var pos = stream.Position;

        var ticks = DateTime.UtcNow.Ticks;
        var volId = (uint)((ticks & 0xFFFF) | (ticks >> 32));

        // Write the BIOS Parameter Block (BPB) - a single sector
        Span<byte> bpb = stackalloc byte[512];
        bpb.Clear();

        uint sectors;
        if (type == FloppyDiskType.DoubleDensity)
        {
            sectors = 1440;
            WriteBPB(bpb, sectors, FatType.Fat12, 224, 0, 1, 1, new Geometry(80, 2, 9), true, volId, label);
        }
        else if (type == FloppyDiskType.HighDensity)
        {
            sectors = 2880;
            WriteBPB(bpb, sectors, FatType.Fat12, 224, 0, 1, 1, new Geometry(80, 2, 18), true, volId, label);
        }
        else if (type == FloppyDiskType.Extended)
        {
            sectors = 5760;
            WriteBPB(bpb, sectors, FatType.Fat12, 224, 0, 1, 1, new Geometry(80, 2, 36), true, volId, label);
        }
        else
        {
            throw new ArgumentException("Unrecognized Floppy Disk type", nameof(type));
        }

        stream.Write(bpb);

        // Write both FAT copies
        var fatSize = CalcFatSize(sectors, FatType.Fat12, 1);
        var fat = new byte[fatSize * Sizes.Sector];
        var fatBuffer = new FatBuffer(FatType.Fat12, fat);
        fatBuffer.SetNext(0, 0xFFFFFFF0);
        fatBuffer.SetEndOfChain(1);
        stream.Write(fat, 0, fat.Length);
        stream.Write(fat, 0, fat.Length);

        // Write the (empty) root directory
        uint rootDirSectors = (224 * 32 + Sizes.Sector - 1) / Sizes.Sector;
        var rootDir = new byte[rootDirSectors * Sizes.Sector];
        stream.Write(rootDir, 0, rootDir.Length);

        // Write a single byte at the end of the disk to ensure the stream is at least as big
        // as needed for this disk image.
        stream.Position = pos + sectors * Sizes.Sector - 1;
        stream.WriteByte(0);

        // Give the caller access to the new file system
        stream.Position = pos;
        return new FatFileSystem(stream);
    }

    /// <summary>
    /// Formats a virtual hard disk partition.
    /// </summary>
    /// <param name="disk">The disk containing the partition.</param>
    /// <param name="partitionIndex">The index of the partition on the disk.</param>
    /// <param name="label">The volume label for the partition (or null).</param>
    /// <returns>An object that provides access to the newly created partition file system.</returns>
    public static FatFileSystem FormatPartition(VirtualDisk disk, int partitionIndex, string label)
    {
        using Stream partitionStream = disk.Partitions[partitionIndex].Open();
        return FormatPartition(
            partitionStream,
            label,
            disk.Geometry ?? disk.BiosGeometry,
            (int)disk.Partitions[partitionIndex].FirstSector,
            (int)(1 + disk.Partitions[partitionIndex].LastSector - disk.Partitions[partitionIndex].FirstSector),
            0);
    }

    /// <summary>
    /// Creates a formatted hard disk partition in a stream.
    /// </summary>
    /// <param name="stream">The stream to write the new file system to.</param>
    /// <param name="label">The volume label for the partition (or null).</param>
    /// <param name="diskGeometry">The geometry of the disk containing the partition.</param>
    /// <param name="firstSector">The starting sector number of this partition (hide's sectors in other partitions).</param>
    /// <param name="sectorCount">The number of sectors in this partition.</param>
    /// <param name="reservedSectors">The number of reserved sectors at the start of the partition.</param>
    /// <returns>An object that provides access to the newly created partition file system.</returns>
    public static FatFileSystem FormatPartition(
        Stream stream,
        string label,
        Geometry diskGeometry,
        int firstSector,
        int sectorCount,
        short reservedSectors)
    {
        var pos = stream.Position;

        var ticks = DateTime.UtcNow.Ticks;
        var volId = (uint)((ticks & 0xFFFF) | (ticks >> 32));

        byte sectorsPerCluster;
        FatType fatType;
        ushort maxRootEntries;

        /*
         * Write the BIOS Parameter Block (BPB) - a single sector
         */

        if (sectorCount <= 8400)
        {
            throw new ArgumentException("Requested size is too small for a partition");
        }

        if (sectorCount < 1024 * 1024)
        {
            fatType = FatType.Fat16;
            maxRootEntries = 512;
            if (sectorCount <= 32680)
            {
                sectorsPerCluster = 2;
            }
            else if (sectorCount <= 262144)
            {
                sectorsPerCluster = 4;
            }
            else if (sectorCount <= 524288)
            {
                sectorsPerCluster = 8;
            }
            else
            {
                sectorsPerCluster = 16;
            }

            if (reservedSectors < 1)
            {
                reservedSectors = 1;
            }
        }
        else
        {
            fatType = FatType.Fat32;
            maxRootEntries = 0;
            if (sectorCount <= 532480)
            {
                sectorsPerCluster = 1;
            }
            else if (sectorCount <= 16777216)
            {
                sectorsPerCluster = 8;
            }
            else if (sectorCount <= 33554432)
            {
                sectorsPerCluster = 16;
            }
            else if (sectorCount <= 67108864)
            {
                sectorsPerCluster = 32;
            }
            else
            {
                sectorsPerCluster = 64;
            }

            if (reservedSectors < 32)
            {
                reservedSectors = 32;
            }
        }

        Span<byte> bpb = stackalloc byte[512];
        WriteBPB(bpb, (uint)sectorCount, fatType, maxRootEntries, (uint)firstSector, (ushort)reservedSectors,
            sectorsPerCluster, diskGeometry, false, volId, label);
        stream.Write(bpb);

        /*
         * Skip the reserved sectors
         */

        stream.Position = pos + (ushort)reservedSectors * Sizes.Sector;

        /*
         * Write both FAT copies
         */

        var fat = new byte[CalcFatSize((uint)sectorCount, fatType, sectorsPerCluster) * Sizes.Sector];
        var fatBuffer = new FatBuffer(fatType, fat);
        fatBuffer.SetNext(0, 0xFFFFFFF8);
        fatBuffer.SetEndOfChain(1);
        if (fatType >= FatType.Fat32)
        {
            // Mark cluster 2 as End-of-chain (i.e. root directory
            // is a single cluster in length)
            fatBuffer.SetEndOfChain(2);
        }

        stream.Write(fat, 0, fat.Length);
        stream.Write(fat, 0, fat.Length);

        /*
         * Write the (empty) root directory
         */

        uint rootDirSectors;
        if (fatType < FatType.Fat32)
        {
            rootDirSectors = (uint)((maxRootEntries * 32 + Sizes.Sector - 1) / Sizes.Sector);
        }
        else
        {
            rootDirSectors = sectorsPerCluster;
        }

        var rootDir = new byte[rootDirSectors * Sizes.Sector];
        stream.Write(rootDir, 0, rootDir.Length);

        /*
         * Make sure the stream is at least as large as the partition requires.
         */

        if (stream.Length < pos + sectorCount * Sizes.Sector)
        {
            stream.SetLength(pos + sectorCount * Sizes.Sector);
        }

        /*
         * Give the caller access to the new file system
         */

        stream.Position = pos;
        return new FatFileSystem(stream);
    }

#endregion
}
