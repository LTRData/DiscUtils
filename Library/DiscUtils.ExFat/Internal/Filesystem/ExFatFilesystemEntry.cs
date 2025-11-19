// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Diagnostics;
using System.IO;
using DiscUtils.ExFat.Internal.IO;
using DiscUtils.ExFat.Internal.Partition.Entries;

namespace DiscUtils.ExFat.Internal.Filesystem;
/// <summary>
/// Entry for <see cref="ExFatEntryFilesystem"/>.
/// Links to <see cref="ExFatMetaDirectoryEntry"/> for partition-level manipulation
/// </summary>
[DebuggerDisplay("{" + nameof(DebugLiteral) + "}")]
public class ExFatFilesystemEntry
{
    private readonly DataDescriptor? dataDescriptorOverride;
    private FileExFatDirectoryEntry FileEntry => MetaEntry?.Primary as FileExFatDirectoryEntry;
    private ExFatFileAttributes? _attributesOverride;
    private string DebugLiteral => Name + (IsDirectory ? "/" : "");

    internal DataDescriptor ParentDataDescriptor { get; }
    internal ExFatMetaDirectoryEntry MetaEntry { get; }

    /// <summary>
    /// Gets the attributes.
    /// </summary>
    /// <value>
    /// The attributes.
    /// </value>
    public FileAttributes Attributes
    {
        get
        {
            if (_attributesOverride.HasValue)
            {
                return (FileAttributes)_attributesOverride.Value;
            }
            // this is always the case
            if (FileEntry != null)
            {
                return (FileAttributes)FileEntry?.FileAttributes.Value;
            }

            return 0;
        }
        set
        {
            // this one won't change, it would be baaaad.
            value &= ~FileAttributes.Directory;
            if (_attributesOverride.HasValue)
            {
                _attributesOverride = (ExFatFileAttributes)value;
                return;
            }

            if (FileEntry != null)
            {
                FileEntry.FileAttributes.Value = (ExFatFileAttributes)value;
                return;
            }

            throw new IOException();
        }
    }

    /// <summary>
    /// Gets a value indicating whether this instance is directory.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance is directory; otherwise, <c>false</c>.
    /// </value>
    public bool IsDirectory => Attributes.HasAny(FileAttributes.Directory);

    /// <summary>
    /// Gets the name.
    /// </summary>
    /// <value>
    /// The name.
    /// </value>
    public string Name => MetaEntry?.ExtensionsFileName ?? "";

    /// <summary>
    /// Gets the length.
    /// </summary>
    /// <value>
    /// The length.
    /// </value>
    public long Length => (long)(MetaEntry?.SecondaryStreamExtension.DataLength.Value ?? 0);

    /// <summary>
    /// Gets the creation time.
    /// </summary>
    /// <value>
    /// The creation date.
    /// </value>
    public DateTime CreationTime => FileEntry?.CreationTime.Value ?? default;

    /// <summary>
    /// Gets the creation time, UTC.
    /// </summary>
    /// <value>
    /// The creation date UTC.
    /// </value>
    public DateTime CreationTimeUtc => FileEntry?.CreationDateTimeOffset.Value.UtcDateTime ?? default;

    /// <summary>
    /// Gets or sets the creation date time offset.
    /// </summary>
    /// <value>
    /// The creation date time offset.
    /// </value>
    public DateTimeOffset CreationDateTimeOffset
    {
        get => FileEntry?.CreationDateTimeOffset.Value ?? default;
        set => FileEntry.CreationDateTimeOffset.Value = value;
    }

    /// <summary>
    /// Gets the last write time.
    /// </summary>
    /// <value>
    /// The last write time.
    /// </value>
    public DateTime LastWriteTime => FileEntry?.LastWriteTime.Value ?? default;

    /// <summary>
    /// Gets the last write time, UTC.
    /// </summary>
    /// <value>
    /// The last write time UTC.
    /// </value>
    public DateTime LastWriteTimeUtc => FileEntry?.LastWriteDateTimeOffset.Value.UtcDateTime ?? default;

    /// <summary>
    /// Gets or sets the last write date time offset.
    /// </summary>
    /// <value>
    /// </value>
    public DateTimeOffset LastWriteDateTimeOffset
    {
        get => FileEntry?.LastWriteDateTimeOffset.Value ?? default;
        set => FileEntry.LastWriteDateTimeOffset.Value = value;
    }

    /// <summary>
    /// Gets the last access time.
    /// </summary>
    /// <value>
    /// The last access time.
    /// </value>
    public DateTime LastAccessTime => FileEntry?.LastAccessTime.Value ?? default;

    /// <summary>
    /// Gets the last access time, UTC.
    /// </summary>
    /// <value>
    /// The last access time UTC.
    /// </value>
    public DateTime LastAccessTimeUtc => FileEntry?.LastAccessDateTimeOffset.Value.UtcDateTime ?? default;

    /// <summary>
    /// Gets or sets the last write date time offset.
    /// </summary>
    /// <value>
    /// </value>
    public DateTimeOffset LastAccessDateTimeOffset
    {
        get => FileEntry?.LastAccessDateTimeOffset.Value ?? default;
        set => FileEntry.LastAccessDateTimeOffset.Value = value;
    }

    /// <summary>
    /// Gets the data descriptor.
    /// </summary>
    /// <value>
    /// The data descriptor.
    /// </value>
    public DataDescriptor? DataDescriptor => dataDescriptorOverride ?? MetaEntry?.DataDescriptor;

    internal ExFatFilesystemEntry(DataDescriptor parentDataDescriptor, ExFatMetaDirectoryEntry metaEntry = null, DataDescriptor? dataDescriptorOverride = null, ExFatFileAttributes? attributesOverride = null)
    {
        this.dataDescriptorOverride = dataDescriptorOverride;
        ParentDataDescriptor = parentDataDescriptor;
        MetaEntry = metaEntry;
        _attributesOverride = attributesOverride;
    }
}
