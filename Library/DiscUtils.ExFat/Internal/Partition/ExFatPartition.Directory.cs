// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System.Collections.Generic;
using System.IO;
using DiscUtils.ExFat.Internal.Partition.Entries;
using DiscUtils.ExFat.Internal.IO;

namespace DiscUtils.ExFat.Internal.Partition;
partial class ExFatPartition
{
    private readonly object _directoryLock = new();

    /// <summary>
    /// Gets the entries, totally raw (includes the deleted entries).
    /// </summary>
    /// <returns></returns>
    public IEnumerable<ExFatDirectoryEntry> GetEntries(DataDescriptor dataDescriptor)
    {
        var entryBytes = new byte[32];

        lock (_directoryLock)
        {
            using var readerStream = OpenDataStream(dataDescriptor, FileAccess.Read);
            for (var offset = 0L; ; offset += 32)
            {
                // cluster offset before reading data, since it's the start
                if (readerStream.Read(entryBytes, 0, entryBytes.Length) != 32)
                {
                    break;
                }

                var directoryEntry = ExFatDirectoryEntry.Create(new(entryBytes), offset);
                if (directoryEntry != null)
                {
                    yield return directoryEntry;
                    entryBytes = new byte[32];
                }
            }
        }
    }

    /// <summary>
    /// Gets the entries grouped: one primary followed by its secondaries.
    /// </summary>
    /// <returns></returns>
    public IEnumerable<ExFatMetaDirectoryEntry> GetMetaEntries(DataDescriptor dataDescriptor)
    {
        var entriesStack = new List<ExFatDirectoryEntry>();
        foreach (var directoryEntry in GetEntries(dataDescriptor)) // locked on _directoryLock
        {
            if (!directoryEntry.InUse)
            {
                continue;
            }

            if (directoryEntry.IsSecondary)
            {
                entriesStack.Add(directoryEntry);
            }
            else
            {
                if (entriesStack.Count > 0)
                {
                    yield return new ExFatMetaDirectoryEntry(entriesStack);
                }

                entriesStack.Clear();
                entriesStack.Add(directoryEntry);
            }
        }

        if (entriesStack.Count > 0)
        {
            yield return new ExFatMetaDirectoryEntry(entriesStack);
        }
    }

    /// <summary>
    /// Finds the available slot.
    /// </summary>
    /// <param name="directoryStream">The directory stream.</param>
    /// <param name="entriesCount">The entries count.</param>
    /// <returns></returns>
    /// <exception cref="System.InvalidOperationException"></exception>
    private long FindAvailableSlot(ClusterStream directoryStream, int entriesCount)
    {
        lock (_directoryLock)
        {
            long availableSlot = -1;
            var availableCount = 0;
            for (var offset = 0L; ; offset += 32)
            {
                directoryStream.Seek(offset, SeekOrigin.Begin);
                var typeByte = directoryStream.ReadByte();
                // when we reach the end, we can append from here
                if (typeByte == -1)
                {
                    if (availableSlot == -1)
                    {
                        availableSlot = offset;
                    }

                    return availableSlot;
                }

                var type = (ExFatDirectoryEntryType)typeByte;
                if (type.HasAny(ExFatDirectoryEntryType.InUse))
                {
                    availableSlot = -1;
                }
                else
                {
                    if (availableSlot == -1)
                    {
                        availableSlot = offset;
                        availableCount = 0;
                    }

                    if (++availableCount == entriesCount)
                    {
                        return availableSlot;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Adds the entry to directory (given by <see cref="DataDescriptor" />.
    /// </summary>
    /// <param name="targetDirectoryDataDescriptor">The data descriptor.</param>
    /// <param name="metaEntry">The meta entry.</param>
    /// <returns>A <see cref="DataDescriptor"/> describing directory after append</returns>
    public DataDescriptor AddEntry(DataDescriptor targetDirectoryDataDescriptor, ExFatMetaDirectoryEntry metaEntry)
    {
        var r = targetDirectoryDataDescriptor;
        lock (_directoryLock)
        {
            using var directoryStream = OpenDataStream(targetDirectoryDataDescriptor, FileAccess.ReadWrite, d => r = d);
            var availableSlot = FindAvailableSlot(directoryStream, metaEntry.Entries.Count);
            directoryStream.Seek(availableSlot, SeekOrigin.Begin);
            foreach (var entry in metaEntry.Entries)
            {
                entry.EntryType.Value |= ExFatDirectoryEntryType.InUse;
            }

            metaEntry.Write(directoryStream);
        }

        return r;
    }

    /// <summary>
    /// Adds the entry to directory (given by <see cref="DataDescriptor" />).
    /// </summary>
    /// <param name="dataDescriptor">The data descriptor.</param>
    /// <param name="metaEntry">The meta entry.</param>
    /// <returns>A <see cref="DataDescriptor"/> describing directory after append</returns>
    public void UpdateEntry(DataDescriptor dataDescriptor, ExFatMetaDirectoryEntry metaEntry)
    {
        lock (_directoryLock)
        {
            using var directoryStream = OpenDataStream(dataDescriptor, FileAccess.ReadWrite);
            directoryStream.Seek(metaEntry.Primary.DirectoryPosition, SeekOrigin.Begin);
            metaEntry.Write(directoryStream);
        }
    }

    public bool CanWrite => _partitionStream.CanWrite;
}
