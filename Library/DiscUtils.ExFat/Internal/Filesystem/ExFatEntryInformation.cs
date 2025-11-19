// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Diagnostics;
using System.IO;

namespace DiscUtils.ExFat.Internal.Filesystem;

/// <summary>
/// Information about file system entry.
/// High-level, related to <see cref="ExFatPathFilesystem"/>
/// </summary>
[DebuggerDisplay("{" + nameof(Path) + "}")]
public class ExFatEntryInformation
{
    private readonly ExFatEntryFilesystem entryFilesystem;
    private readonly ExFatFilesystemEntry entry;

    /// <summary>
    /// Gets the path.
    /// </summary>
    /// <value>
    /// The path.
    /// </value>
    public string Path { get; }

    /// <summary>
    /// Gets the attributes.
    /// </summary>
    /// <value>
    /// The attributes.
    /// </value>
    public FileAttributes Attributes
    {
        get => entry.Attributes;
        set
        {
            entry.Attributes = value;
            Update();
        }
    }

    /// <summary>
    /// Gets or sets the creation time.
    /// </summary>
    /// <value>
    /// The creation time.
    /// </value>
    public DateTime CreationTime
    {
        get => entry.CreationDateTimeOffset.LocalDateTime;
        set
        {
            entry.CreationDateTimeOffset = value.ToLocalTime();
            Update();
        }
    }

    /// <summary>
    /// Gets or sets the creation time, UTC.
    /// </summary>
    /// <value>
    /// The creation time.
    /// </value>
    public DateTime CreationTimeUtc
    {
        get => entry.CreationDateTimeOffset.UtcDateTime;
        set
        {
            entry.CreationDateTimeOffset = value.ToUniversalTime();
            Update();
        }
    }

    /// <summary>
    /// Gets or sets the last write time.
    /// </summary>
    /// <value>
    /// The last write time.
    /// </value>
    public DateTime LastWriteTime
    {
        get => entry.LastWriteDateTimeOffset.LocalDateTime;
        set
        {
            entry.LastWriteDateTimeOffset = value.ToLocalTime();
            Update();
        }
    }

    /// <summary>
    /// Gets or sets the last write time, UTC.
    /// </summary>
    /// <value>
    /// The last write time.
    /// </value>
    public DateTime LastWriteTimeUtc
    {
        get => entry.LastWriteDateTimeOffset.UtcDateTime;
        set
        {
            entry.LastWriteDateTimeOffset = value.ToUniversalTime();
            Update();
        }
    }

    /// <summary>
    /// Gets or sets the last write time.
    /// </summary>
    /// <value>
    /// The last write time.
    /// </value>
    public DateTime LastAccessTime
    {
        get => entry.LastAccessDateTimeOffset.LocalDateTime;
        set
        {
            entry.LastAccessDateTimeOffset = value.ToLocalTime();
            Update();
        }
    }

    /// <summary>
    /// Gets or sets the last write time, UTC.
    /// </summary>
    /// <value>
    /// The last write time.
    /// </value>
    public DateTime LastAccessTimeUtc
    {
        get => entry.LastAccessDateTimeOffset.UtcDateTime;
        set
        {
            entry.LastAccessDateTimeOffset = value.ToUniversalTime();
            Update();
        }
    }

    /// <summary>
    /// Gets the length.
    /// </summary>
    /// <value>
    /// The length.
    /// </value>
    public long Length => entry.Length;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExFatEntryInformation"/> class.
    /// </summary>
    /// <param name="entryFilesystem">The entry filesystem.</param>
    /// <param name="entry">The entry.</param>
    /// <param name="cleanPath">The path.</param>
    internal ExFatEntryInformation(ExFatEntryFilesystem entryFilesystem, ExFatFilesystemEntry entry, string cleanPath)
    {
        Path = cleanPath;
        this.entryFilesystem = entryFilesystem;
        this.entry = entry;
    }

    private void Update() => entryFilesystem.Update(entry);
}