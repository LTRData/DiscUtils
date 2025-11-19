// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using DiscUtils.Streams.Compatibility;
using DiscUtils.ExFat.Internal.IO;
using DiscUtils.ExFat.Internal.Partition;
using DiscUtils.ExFat.Internal.Partition.Entries;
using System;
using System.Collections.Generic;
using System.IO;

namespace DiscUtils.ExFat.Internal.Filesystem;
/// <summary>
/// Filesystem access at low-level: entry manipulation
/// (high level is <see cref="ExFatPathFilesystem"/> which works with paths)
/// </summary>
/// <seealso cref="IDisposable" />
/// <remarks>
/// Initializes a new instance of the <see cref="ExFatEntryFilesystem"/> class.
/// </remarks>
/// <param name="partition">The partition.</param>
/// <param name="options">The flags.</param>
public class ExFatEntryFilesystem(ExFatPartition partition, ExFatOptions options = ExFatOptions.Default) : IDisposable
{
    private readonly ExFatOptions _options = options;
    private readonly ExFatPartition _partition = partition;
    private readonly object _entryLock = new();

    private ExFatFilesystemEntry _rootDirectory;

    /// <summary>
    /// Gets the total size.
    /// </summary>
    /// <value>
    /// The total size.
    /// </value>
    public long TotalSpace => _partition.TotalSpace;

    /// <summary>
    /// Gets the used space
    /// </summary>
    /// <value>
    /// The used space.
    /// </value>
    public long UsedSpace => _partition.UsedSpace;

    /// <summary>
    /// Gets the available size.
    /// </summary>
    /// <value>
    /// The available size.
    /// </value>
    public long AvailableSpace => _partition.AvailableSpace;

    /// <summary>
    /// Gets the root directory.
    /// </summary>
    /// <value>
    /// The root directory.
    /// </value>
    public ExFatFilesystemEntry RootDirectory => _rootDirectory ??= CreateRootDirectory();

    /// <inheritdoc />
    /// <summary>
    /// Initializes a new instance of the <see cref="T:ExFat.Filesystem.ExFatEntryFilesystem" /> class.
    /// </summary>
    /// <param name="partitionStream">The partition stream.</param>
    /// <param name="options">The flags.</param>
    public ExFatEntryFilesystem(Stream partitionStream, ExFatOptions options = ExFatOptions.Default)
        : this(new ExFatPartition(partitionStream), options)
    {
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        _partition.Dispose();
        GC.SuppressFinalize(this);
    }

    private ExFatFilesystemEntry CreateRootDirectory()
    {
        return new ExFatFilesystemEntry(_partition.RootDirectoryDataDescriptor, dataDescriptorOverride: _partition.RootDirectoryDataDescriptor,
            attributesOverride: ExFatFileAttributes.Directory);
    }

    /// <summary>
    /// Enumerates the file system entries.
    /// </summary>
    /// <param name="directoryEntry">The directory entry.</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public IEnumerable<ExFatFilesystemEntry> EnumerateFileSystemEntries(ExFatFilesystemEntry directoryEntry)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(directoryEntry);
#else
        if (directoryEntry == null)
        {
            throw new ArgumentNullException(nameof(directoryEntry));
        }
#endif

        if (!directoryEntry.IsDirectory)
        {
            throw new InvalidOperationException();
        }

        lock (_entryLock)
        {
            foreach (var metaEntry in _partition.GetMetaEntries(directoryEntry.DataDescriptor.Value))
            {
                // keep only file entries
                if (metaEntry.Primary is FileExFatDirectoryEntry)
                {
                    yield return new ExFatFilesystemEntry(directoryEntry.DataDescriptor.Value, metaEntry);
                }
            }
        }
    }

    /// <summary>
    /// Finds a child with given name.
    /// </summary>
    /// <param name="directoryEntry">The directory entry.</param>
    /// <param name="name">The name.</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public ExFatFilesystemEntry FindChild(ExFatFilesystemEntry directoryEntry, string name)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(directoryEntry);
#else
        if (directoryEntry == null)
        {
            throw new ArgumentNullException(nameof(directoryEntry));
        }
#endif

        if (!directoryEntry.IsDirectory)
        {
            throw new InvalidOperationException();
        }

        var nameHash = _partition.ComputeNameHash(name);
        lock (_entryLock)
        {
            foreach (var metaEntry in _partition.GetMetaEntries(directoryEntry.DataDescriptor.Value))
            {
                var streamExtension = metaEntry.SecondaryStreamExtension;
                // keep only file entries
                if (streamExtension != null && streamExtension.NameHash.Value == nameHash && metaEntry.ExtensionsFileName == name)
                {
                    return new ExFatFilesystemEntry(directoryEntry.DataDescriptor.Value, metaEntry);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Opens the specified entry.
    /// </summary>
    /// <param name="fileEntry">The entry.</param>
    /// <param name="access">The access.</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    public Stream OpenFile(ExFatFilesystemEntry fileEntry, FileAccess access)
    {
        if (fileEntry.IsDirectory)
        {
            throw new InvalidOperationException();
        }

        return OpenData(fileEntry, access);
    }

    /// <summary>
    /// Creates the file.
    /// </summary>
    /// <param name="parentDirectory">The parent directory.</param>
    /// <param name="fileName">Name of the file.</param>
    /// <returns></returns>
    public Stream CreateFile(ExFatFilesystemEntry parentDirectory, string fileName)
    {
        if (!parentDirectory.IsDirectory)
        {
            throw new InvalidOperationException();
        }

        var existingFile = FindChild(parentDirectory, fileName);
        if (existingFile != null)
        {
            var stream = OpenData(existingFile, FileAccess.ReadWrite);
            stream.SetLength(0);
            return stream;
        }

        var fileEntry = CreateEntry(parentDirectory, fileName, FileAttributes.Archive);
        var updatedDataDescriptor = _partition.AddEntry(parentDirectory.DataDescriptor.Value, fileEntry.MetaEntry);
        UpdateEntry(parentDirectory, FileAccess.Write, updatedDataDescriptor);
        return OpenData(fileEntry, FileAccess.ReadWrite);
    }

    private ClusterStream OpenData(ExFatFilesystemEntry fileEntry, FileAccess access)
        => _partition.OpenDataStream(fileEntry.DataDescriptor, access,
            CanWrite ? d => UpdateEntry(fileEntry, access, d) : null);

    private void UpdateEntry(ExFatFilesystemEntry entry, FileAccess fileAccess, DataDescriptor dataDescriptor)
    {
        if (entry?.MetaEntry == null)
        {
            return;
        }

        DateTimeOffset? now = null;
        var file = (FileExFatDirectoryEntry)entry.MetaEntry.Primary;

        // if file was open for reading and the flag is set, the entry is updated
        if (fileAccess.HasAny(FileAccess.Read) && _options.HasAny(ExFatOptions.UpdateLastAccessTime))
        {
            now = DateTimeOffset.Now;
            file.LastAccessDateTimeOffset.Value = now.Value;
        }

        // when it was open for writing, its characteristics may have changed, so we update them
        if (fileAccess.HasAny(FileAccess.Write))
        {
            now ??= DateTimeOffset.Now;
            file.FileAttributes.Value |= ExFatFileAttributes.Archive;
            file.LastWriteDateTimeOffset.Value = now.Value;
            var stream = entry.MetaEntry.SecondaryStreamExtension;
            if (dataDescriptor.Contiguous)
            {
                stream.GeneralSecondaryFlags.Value |= ExFatGeneralSecondaryFlags.NoFatChain;
            }
            else
            {
                stream.GeneralSecondaryFlags.Value &= ~ExFatGeneralSecondaryFlags.NoFatChain;
            }

            stream.FirstCluster.Value = (uint)dataDescriptor.FirstCluster.Value;
            stream.ValidDataLength.Value = dataDescriptor.LogicalLength;
            stream.DataLength.Value = dataDescriptor.PhysicalLength;
        }

        // now has value only if it was used before, so we spare a flag :)
        if (now.HasValue)
        {
            Update(entry);
        }
    }

    /// <summary>
    /// Creates a <see cref="ExFatFilesystemEntry" />.
    /// </summary>
    /// <param name="parent">The parent.</param>
    /// <param name="name">The name.</param>
    /// <param name="attributes">The attributes.</param>
    /// <returns></returns>
    private ExFatFilesystemEntry CreateEntry(ExFatFilesystemEntry parent, string name, FileAttributes attributes)
    {
        var now = DateTimeOffset.Now;
        var entries = new List<ExFatDirectoryEntry>
            {
                new FileExFatDirectoryEntry(new(new byte[32]))
                {
                    EntryType = {Value = ExFatDirectoryEntryType.File},
                    FileAttributes = {Value = (ExFatFileAttributes) attributes},
                    CreationDateTimeOffset = {Value = now},
                    LastWriteDateTimeOffset = {Value = now},
                    LastAccessDateTimeOffset = {Value = now},
                },
                new StreamExtensionExFatDirectoryEntry(new(new byte[32]))
                {
                    FirstCluster = {Value = 0},
                    EntryType = {Value = ExFatDirectoryEntryType.Stream},
                    GeneralSecondaryFlags = {Value = ExFatGeneralSecondaryFlags.ClusterAllocationPossible},
                    NameLength = {Value = (byte) name.Length},
                    NameHash = {Value = _partition.ComputeNameHash(name)},
                }
            };
        for (var nameIndex = 0; nameIndex < name.Length; nameIndex += 15)
        {
            var namePart = name.Substring(nameIndex, Math.Min(15, name.Length - nameIndex));
            entries.Add(new FileNameExtensionExFatDirectoryEntry(new(new byte[32]))
            {
                EntryType = { Value = ExFatDirectoryEntryType.FileName },
                FileName = { Value = namePart }
            });
        }

        var metaEntry = new ExFatMetaDirectoryEntry(entries);
        var entry = new ExFatFilesystemEntry(parent.DataDescriptor.Value, metaEntry);
        return entry;
    }

    /// <summary>
    /// Creates a directory or returns the existing.
    /// </summary>
    /// <param name="parentDirectoryEntry">The parent directory.</param>
    /// <param name="directoryName">Name of the directory.</param>
    /// <returns></returns>
    /// <exception cref="InvalidOperationException"></exception>
    /// <exception cref="IOException"></exception>
    public ExFatFilesystemEntry CreateDirectory(ExFatFilesystemEntry parentDirectoryEntry, string directoryName)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(parentDirectoryEntry);
#else
        if (parentDirectoryEntry == null)
        {
            throw new ArgumentNullException(nameof(parentDirectoryEntry));
        }
#endif

        if (!parentDirectoryEntry.IsDirectory)
        {
            throw new InvalidOperationException();
        }

        lock (_entryLock)
        {
            var existingEntry = FindChild(parentDirectoryEntry, directoryName);
            if (existingEntry != null)
            {
                if (!existingEntry.IsDirectory)
                {
                    throw new IOException();
                }

                return existingEntry;
            }

            var directoryEntry = CreateEntry(parentDirectoryEntry, directoryName, FileAttributes.Directory);
            var updatedDataDescriptor = _partition.AddEntry(parentDirectoryEntry.DataDescriptor.Value, directoryEntry.MetaEntry);
            using (var directoryStream = OpenData(directoryEntry, FileAccess.ReadWrite))
            {
                // at least one empty entry, otherwise CHKDSK doesn't understand (the dumbass)
                Span<byte> empty = stackalloc byte[32];
                empty.Clear();
                directoryStream.Write(empty);
            }

            UpdateEntry(parentDirectoryEntry, FileAccess.Write, updatedDataDescriptor);
            return directoryEntry;
        }
    }

    /// <summary>
    /// Deletes the specified entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public void Delete(ExFatFilesystemEntry entry)
    {
        lock (_entryLock)
        {
            _partition.Free(entry.DataDescriptor.Value);
            foreach (var e in entry.MetaEntry.Entries)
            {
                e.EntryType.Value &= ~ExFatDirectoryEntryType.InUse;
            }

            Update(entry);
        }
    }

    /// <summary>
    /// Deletes the tree for specified entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    public void DeleteTree(ExFatFilesystemEntry entry)
    {
        lock (_entryLock)
        {
            if (entry.IsDirectory)
            {
                foreach (var childEntry in EnumerateFileSystemEntries(entry))
                {
                    DeleteTree(childEntry);
                }
            }

            Delete(entry);
        }
    }

    /// <summary>
    /// Writes entry to partition (after changes).
    /// </summary>
    /// <param name="entry">The entry.</param>
    public void Update(ExFatFilesystemEntry entry)
    {
        lock (_entryLock)
        {
            _partition.UpdateEntry(entry.ParentDataDescriptor, entry.MetaEntry);
        }
    }

    /// <summary>
    /// Moves the specified entry from source to target.
    /// </summary>
    /// <param name="source">The source.</param>
    /// <param name="targetDirectory">The target directory.</param>
    /// <param name="targetName">Name of the target.</param>
    public void Move(ExFatFilesystemEntry source, ExFatFilesystemEntry targetDirectory, string targetName = null)
    {
        if (source.MetaEntry.Primary is not FileExFatDirectoryEntry sourceFile)
        {
            throw new InvalidOperationException();
        }

        lock (_entryLock)
        {
            // create new entry, similar to source
            targetName ??= source.Name;
            var newEntry = CreateEntry(targetDirectory, targetName, source.Attributes);
            var newFile = (FileExFatDirectoryEntry)newEntry.MetaEntry.Primary;
            newFile.CreationTimeStamp.Value = sourceFile.CreationTimeStamp.Value;
            newFile.Creation10msIncrement.Value = sourceFile.Creation10msIncrement.Value;
            newFile.CreationTimeZoneOffset.Value = sourceFile.CreationTimeZoneOffset.Value;
            newFile.LastWriteTimeStamp.Value = sourceFile.LastWriteTimeStamp.Value;
            newFile.LastWrite10msIncrement.Value = sourceFile.LastWrite10msIncrement.Value;
            newFile.LastWriteTimeZoneOffset.Value = sourceFile.LastWriteTimeZoneOffset.Value;
            newFile.LastAccessTimeStamp.Value = sourceFile.LastAccessTimeStamp.Value;
            newFile.LastAccessTimeZoneOffset.Value = sourceFile.LastAccessTimeZoneOffset.Value;
            newEntry.MetaEntry.SecondaryStreamExtension.DataDescriptor = source.MetaEntry.SecondaryStreamExtension.DataDescriptor;
            newEntry.MetaEntry.SecondaryStreamExtension.ValidDataLength.Value = source.MetaEntry.SecondaryStreamExtension.ValidDataLength.Value;
            // add it to target directory
            var newDataDescriptor = _partition.AddEntry(targetDirectory.DataDescriptor.Value, newEntry.MetaEntry);
            UpdateEntry(targetDirectory, FileAccess.ReadWrite, newDataDescriptor);
            // and mark previous as deleted
            foreach (var e in source.MetaEntry.Entries)
            {
                e.EntryType.Value &= ~ExFatDirectoryEntryType.InUse;
            }

            Update(source);
        }
    }

    /// <summary>
    /// Formats the specified partition.
    /// </summary>
    /// <param name="partitionStream">The partition stream.</param>
    /// <param name="options">The options.</param>
    /// <param name="volumeLabel">The volume label.</param>
    /// <returns></returns>
    public static ExFatEntryFilesystem Format(Stream partitionStream, ExFatFormatOptions options, string volumeLabel = null)
    {
        var partition = ExFatPartition.Format(partitionStream, options, volumeLabel);
        return new ExFatEntryFilesystem(partition);
    }

    public bool CanWrite => _partition.CanWrite;
}
