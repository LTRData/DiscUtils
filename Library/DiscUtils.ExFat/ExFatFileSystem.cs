// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DiscUtils.ExFat.Internal.Filesystem;
using DiscUtils.Streams;
using DiscUtils.ExFat.Internal.Partition;
using DiscUtils.ExFat.Internal;
using LTRData.Extensions.Buffers;

namespace DiscUtils.ExFat;

public partial class ExFatFileSystem : DiscFileSystem
{
    /// <summary>
    /// </summary>
    public const string Name = "Microsoft exFAT";

    private readonly Stream _rawStream;
    private readonly ExFatPathFilesystem _filesystem;

    /// <summary>
    /// Gets a value indicating whether the file system is thread-safe.
    /// </summary>
    /// <value>true!</value>
    public override bool IsThreadSafe => true;

    /// <inheritdoc />
    public override string FriendlyName => Name;

    /// <inheritdoc />
    public override bool CanWrite => RawStream.CanWrite;

    /// <inheritdoc />
    public override long Size => _filesystem.TotalSpace;

    /// <inheritdoc />
    public override long UsedSpace => _filesystem.UsedSpace;

    /// <inheritdoc />
    public override long AvailableSpace => _filesystem.AvailableSpace;

    public override Stream RawStream => _rawStream;

    /// <summary>
    /// Initializes a new instance of the <see cref="T:DiscUtils.ExFat.ExFatFileSystem" /> class.
    /// </summary>
    /// <param name="partitionStream">The partition stream.</param>
    /// <param name="pathSeparators">The path separators.</param>
    /// <exception cref="InvalidOperationException">Given stream is not exFAT volume</exception>
    /// <exception cref="T:System.InvalidOperationException">Given stream is not exFAT volume</exception>
    /// <inheritdoc />
    public ExFatFileSystem(Stream partitionStream, char[] pathSeparators = null)
    {
        _filesystem = new ExFatPathFilesystem(partitionStream);
        PathSeparators = pathSeparators ?? DefaultSeparators;
        var bootSector = ExFatPartition.ReadBootSector(partitionStream);
        if (!bootSector.IsValid)
        {
            throw new InvalidOperationException("Given stream is not exFAT volume");
        }

        _rawStream = partitionStream;
    }

    /// <inheritdoc />
    /// <summary>
    /// Disposes of this instance.
    /// </summary>
    /// <param name="disposing">The value <c>true</c> if Disposing.</param>
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _filesystem.Dispose();
        }
    }

    /// <summary>
    /// Detects the specified partition stream.
    /// </summary>
    /// <param name="partitionStream">The partition stream.</param>
    /// <returns></returns>
    public static bool Detect(Stream partitionStream)
    {
        try
        {
            var bootSector = ExFatPartition.ReadBootSector(partitionStream);
            return bootSector.IsValid;
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public override void CopyFile(string sourceFile, string destinationFile, bool overwrite)
    {
        using var reader = _filesystem.Open(sourceFile, FileMode.Open, FileAccess.Read);
        using var writer = _filesystem.Open(destinationFile, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write);
        reader.CopyTo(writer);
    }

    /// <inheritdoc />
    public override void CreateDirectory(string path) => _filesystem.CreateDirectory(path);

    /// <inheritdoc />
    public override void DeleteDirectory(string path) => _filesystem.DeleteTree(path);

    /// <inheritdoc />
    public override void DeleteFile(string path) => _filesystem.Delete(path);

    /// <inheritdoc />
    public override bool DirectoryExists(string path)
    {
        var information = _filesystem.GetInformation(path);
        return information != null && information.Attributes.HasAny(FileAttributes.Directory);
    }

    /// <inheritdoc />
    public override bool FileExists(string path)
    {
        var information = _filesystem.GetInformation(path);
        return information != null && !information.Attributes.HasAny(FileAttributes.Directory);
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        return GetEntries(path, searchPattern, searchOption)
            .Where(e => e.Attributes.HasAny(FileAttributes.Directory)).Select(e => e.Path);
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetFiles(string path, string searchPattern, SearchOption searchOption)
    {
        var entries = GetEntries(path, searchPattern, searchOption);
        return entries.Where(e => !e.Attributes.HasAny(FileAttributes.Directory)).Select(e => e.Path);
    }

    /// <inheritdoc />
    public override IEnumerable<string> GetFileSystemEntries(string path) => GetEntries(path, null, SearchOption.TopDirectoryOnly).Select(e => e.Path);

    /// <inheritdoc />
    public override IEnumerable<string> GetFileSystemEntries(string path, string searchPattern) => GetEntries(path, searchPattern, SearchOption.TopDirectoryOnly).Select(e => e.Path);

    /// <inheritdoc />
    public override IEnumerable<string> GetFileSystemEntries(string path, string searchPattern, SearchOption searchOption) => GetEntries(path, searchPattern, searchOption).Select(e => e.Path);

    private static Func<string, bool> ConvertWildcardsToRegEx(string pattern)
    {
        if (pattern is null or "*.*" or "*")
        {
            return null;
        }

        if (pattern.AsSpan().IndexOfAny('*', '?') < 0)
        {
            if (!pattern.Contains('.'))
            {
                pattern += ".";
            }

            return name => StringComparer.OrdinalIgnoreCase.Equals(name, pattern);
        }

        var query = $"^{Regex.Escape(pattern).Replace(@"\*", ".*").Replace(@"\?", "[^.]")}$";
        return new Regex(query, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant).IsMatch;
    }

    private IEnumerable<ExFatEntryInformation> GetEntries(string path, string searchPattern, SearchOption searchOption)
    {
        var regex = ConvertWildcardsToRegEx(searchPattern);
        var entry = _filesystem.GetInformation(path);
        if (entry == null || !entry.Attributes.HasAny(FileAttributes.Directory))
        {
            throw new DirectoryNotFoundException();
        }

        return GetEntries(entry, searchOption == SearchOption.TopDirectoryOnly ? 1 : int.MaxValue).Where(e => IsMatch(regex, e));
    }

    private bool IsMatch(Func<string, bool> regex, ExFatEntryInformation e)
    {
        if (regex == null)
        {
            return true;
        }

        var fileName = GetFileName(e.Path);
        return regex(fileName);
    }

    private readonly ExFatEntryInformation[] _noEntry = [];

    private IEnumerable<ExFatEntryInformation> GetEntries(ExFatEntryInformation entryInformation, int depth)
    {
        if (depth == 0 || !entryInformation.Attributes.HasAny(FileAttributes.Directory))
        {
            return _noEntry;
        }

        return _filesystem.EnumerateEntries(entryInformation.Path).SelectMany(e => GetEntries(e, depth - 1).Prepend(e));
    }

    /// <inheritdoc />
    public override void MoveDirectory(string sourceDirectoryName, string destinationDirectoryName) => _filesystem.Move(sourceDirectoryName, GetDirectoryName(destinationDirectoryName), GetFileName(destinationDirectoryName));

    /// <inheritdoc />
    public override void MoveFile(string sourceName, string destinationName, bool overwrite)
    {
        var information = _filesystem.GetInformation(destinationName);
        string targetDirectory, targetName;
        if (information != null)
        {
            if (information.Attributes.HasAny(FileAttributes.Directory))
            {
                targetDirectory = destinationName;
                targetName = null;
            }
            else
            {
                if (overwrite)
                {
                    DeleteFile(destinationName);
                }

                throw new IOException();
            }
        }
        else
        {
            targetDirectory = GetDirectoryName(destinationName);
            targetName = GetFileName(destinationName);
        }

        _filesystem.Move(sourceName, targetDirectory, targetName);
    }

    /// <inheritdoc />
    public override SparseStream OpenFile(string path, FileMode mode, FileAccess access) => SparseStream.FromStream(_filesystem.Open(path, mode, access), Ownership.Dispose);

    /// <inheritdoc />
    public override FileAttributes GetAttributes(string path)
    {
        var information = _filesystem.GetInformation(path) ?? throw new FileNotFoundException();
        return information.Attributes;
    }

    /// <inheritdoc />
    public override void SetAttributes(string path, FileAttributes newValue)
    {
        var information = _filesystem.GetInformation(path) ?? throw new FileNotFoundException();
        information.Attributes = newValue;
    }

    /// <inheritdoc />
    public override DateTime GetCreationTimeUtc(string path) => _filesystem.GetCreationTimeUtc(path);

    /// <inheritdoc />
    public override void SetCreationTimeUtc(string path, DateTime newTime) => _filesystem.SetCreationTimeUtc(path, newTime);

    /// <inheritdoc />
    public override DateTime GetLastAccessTimeUtc(string path) => _filesystem.GetLastAccessTimeUtc(path);

    /// <inheritdoc />
    public override void SetLastAccessTimeUtc(string path, DateTime newTime) => _filesystem.SetLastAccessTimeUtc(path, newTime);

    /// <inheritdoc />
    public override DateTime GetLastWriteTimeUtc(string path) => _filesystem.GetLastWriteTimeUtc(path);

    /// <inheritdoc />
    public override void SetLastWriteTimeUtc(string path, DateTime newTime) => _filesystem.SetLastWriteTimeUtc(path, newTime);

    public override DiscFileSystemInfo GetFileSystemInfo(string path)
    {
        try
        {
            var information = _filesystem.GetInformation(path);
            if (information != null)
            {
                if (information.Attributes.HasFlag(FileAttributes.Directory))
                {
                    return new CachedDiscDirectoryInfo(this, path, information.Attributes, information.CreationTimeUtc,
                                                       information.LastAccessTimeUtc, information.LastWriteTimeUtc);
                }
                else
                {
                    return new CachedDiscFileInfo(this, path, information.Attributes, information.CreationTimeUtc,
                                                  information.LastAccessTimeUtc, information.LastWriteTimeUtc,
                                                  information.Length);
                }
            }
        }
        catch
        {
        }

        return base.GetFileSystemInfo(path);
    }

    public override DiscDirectoryInfo GetDirectoryInfo(string path)
    {
        try
        {
            var information = _filesystem.GetInformation(path);
            if (information != null && information.Attributes.HasFlag(FileAttributes.Directory))
            {
                return new CachedDiscDirectoryInfo(this, path, information.Attributes, information.CreationTimeUtc,
                                                   information.LastAccessTimeUtc, information.LastWriteTimeUtc);
            }
        }
        catch
        {
        }

        return base.GetDirectoryInfo(path);
    }

    public override DiscFileInfo GetFileInfo(string path)
    {
        try
        {
            var information = _filesystem.GetInformation(path);
            if (information != null && !information.Attributes.HasFlag(FileAttributes.Directory))
            {
                return new CachedDiscFileInfo(this, path, information.Attributes, information.CreationTimeUtc,
                                              information.LastAccessTimeUtc, information.LastWriteTimeUtc,
                                              information.Length);
            }
        }
        catch
        {
        }

        return base.GetFileInfo(path);
    }

    /// <inheritdoc />
    public override long GetFileLength(string path)
    {
        var information = _filesystem.GetInformation(path) ?? throw new FileNotFoundException();
        return information.Length;
    }

    /// <summary>
    /// Formats the specified volume.
    /// </summary>
    /// <param name="volume">The volume.</param>
    /// <param name="options">The options.</param>
    /// <param name="label">The label.</param>
    /// <returns></returns>
    public static ExFatFileSystem Format(PhysicalVolumeInfo volume, ExFatFormatOptions options = null, string label = null)
    {
        var partitionStream = volume.Open();
        using (ExFatPathFilesystem.Format(partitionStream, options, label))
        {
        }

        return new ExFatFileSystem(partitionStream);
    }
}
