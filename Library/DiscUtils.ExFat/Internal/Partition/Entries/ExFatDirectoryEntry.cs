// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Collections.Generic;
using System.IO;
using DiscUtils.ExFat.Internal.Buffers;
using DiscUtils.Streams.Compatibility;

namespace DiscUtils.ExFat.Internal.Partition.Entries;
/// <summary>
/// Simple (raw) directory entry
/// </summary>
public class ExFatDirectoryEntry
{
    /// <summary>
    /// Gets the buffer.
    /// </summary>
    /// <value>
    /// The buffer.
    /// </value>
    protected internal Memory<byte> Buffer { get; }

    /// <summary>
    /// Gets or sets the type of the entry.
    /// </summary>
    /// <value>
    /// The type of the entry.
    /// </value>
    public IValueProvider<ExFatDirectoryEntryType> EntryType { get; }

    /// <summary>
    /// Gets the position in directory.
    /// </summary>
    /// <value>
    /// The directory position.
    /// </value>
    public long DirectoryPosition { get; private set; }

    /// <summary>
    /// Indicates if the entry is in use
    /// </summary>
    /// <value>
    ///   <c>true</c> if [in use]; otherwise, <c>false</c>.
    /// </value>
    public bool InUse
    {
        get => (EntryType.Value & ExFatDirectoryEntryType.InUse) != 0;
        set
        {
            if (value)
            {
                EntryType.Value |= ExFatDirectoryEntryType.InUse;
            }
            else
            {
                EntryType.Value &= ~ExFatDirectoryEntryType.InUse;
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether this instance is secondary.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance is secondary; otherwise, <c>false</c>.
    /// </value>
    public bool IsSecondary => (EntryType.Value & ExFatDirectoryEntryType.Secondary) != 0;
    /// <summary>
    /// Gets a value indicating whether this instance is benign.
    /// </summary>
    /// <value>
    ///   <c>true</c> if this instance is benign; otherwise, <c>false</c>.
    /// </value>
    public bool IsBenign => (EntryType.Value & ExFatDirectoryEntryType.Benign) != 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExFatDirectoryEntry"/> class.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    protected ExFatDirectoryEntry(Memory<byte> buffer)
    {
        Buffer = buffer;
        EntryType = new EnumValueProvider<ExFatDirectoryEntryType, byte>(new BufferUInt8(buffer));
    }

    /// <summary>
    /// Creates a <see cref="ExFatDirectoryEntry" /> given a buffer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="directoryPosition">The directory position.</param>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static ExFatDirectoryEntry Create(Memory<byte> buffer, long directoryPosition)
    {
        var entry = Create(buffer);
        if (entry != null)
        {
            entry.DirectoryPosition = directoryPosition;
        }

        return entry;
    }

    private static ExFatDirectoryEntry Create(Memory<byte> buffer)
    {
        switch ((ExFatDirectoryEntryType)(buffer.Span[0] & (byte)~ExFatDirectoryEntryType.InUse))
        {
            case 0:
                return null;
            case ExFatDirectoryEntryType.AllocationBitmap:
                return new AllocationBitmapExFatDirectoryEntry(buffer);
            case ExFatDirectoryEntryType.UpCaseTable:
                return new UpCaseTableExFatDirectoryEntry(buffer);
            case ExFatDirectoryEntryType.VolumeLabel:
                return new VolumeLabelExFatDirectoryEntry(buffer);
            case ExFatDirectoryEntryType.File:
                return new FileExFatDirectoryEntry(buffer);
            case ExFatDirectoryEntryType.Stream:
                return new StreamExtensionExFatDirectoryEntry(buffer);
            case ExFatDirectoryEntryType.FileName:
                return new FileNameExtensionExFatDirectoryEntry(buffer);
            default:
                // unhandled entries
                return new ExFatDirectoryEntry(buffer);
        }
    }

    /// <summary>
    /// Updates this entry using the specified secondary entries.
    /// </summary>
    /// <param name="secondaryEntries">The secondary entries.</param>
    public virtual void Update(ICollection<ExFatDirectoryEntry> secondaryEntries)
    {
    }

    /// <summary>
    /// Writes the instance to specified stream.
    /// </summary>
    /// <param name="stream">The stream.</param>
    public void Write(Stream stream)
    {
        DirectoryPosition = stream.Position;
        stream.Write(Buffer.Span);
    }
}