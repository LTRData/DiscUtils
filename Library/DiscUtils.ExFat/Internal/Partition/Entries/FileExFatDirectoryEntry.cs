// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System;
using System.Collections.Generic;
using System.Diagnostics;
using DiscUtils.ExFat.Internal.Buffers;
using DiscUtils.ExFat.Internal.Filesystem;

namespace DiscUtils.ExFat.Internal.Partition.Entries;
/// <summary>
/// Represents a directory entry for <see cref="ExFatEntryFilesystem"/>
/// </summary>
/// <seealso cref="ExFatDirectoryEntry" />
[DebuggerDisplay("File")]
public class FileExFatDirectoryEntry : ExFatDirectoryEntry
{
    /// <summary>
    /// Gets or sets the secondary entries count.
    /// </summary>
    /// <value>
    /// The secondary count.
    /// </value>
    public IValueProvider<byte> SecondaryCount { get; }
    /// <summary>
    /// Gets or sets the set checksum.
    /// </summary>
    /// <value>
    /// The set checksum.
    /// </value>
    public IValueProvider<ushort> SetChecksum { get; }
    /// <summary>
    /// Gets or sets the file attributes.
    /// </summary>
    /// <value>
    /// The file attributes.
    /// </value>
    public IValueProvider<ExFatFileAttributes> FileAttributes { get; }
    /// <summary>
    /// Gets or sets the creation time stamp.
    /// </summary>
    /// <value>
    /// The creation time stamp.
    /// </value>
    public IValueProvider<uint> CreationTimeStamp { get; }
    /// <summary>
    /// Gets or sets the last write time stamp.
    /// </summary>
    /// <value>
    /// The last write time stamp.
    /// </value>
    public IValueProvider<uint> LastWriteTimeStamp { get; }
    /// <summary>
    /// Gets or sets the last access time stamp.
    /// </summary>
    /// <value>
    /// The last access time stamp.
    /// </value>
    public IValueProvider<uint> LastAccessTimeStamp { get; }
    /// <summary>
    /// Gets or sets the creation time 10ms increment.
    /// </summary>
    /// <value>
    /// The creation10ms increment.
    /// </value>
    public IValueProvider<byte> Creation10msIncrement { get; }
    /// <summary>
    /// Gets or sets the last write time 10ms increment.
    /// </summary>
    /// <value>
    /// The last write10ms increment.
    /// </value>
    public IValueProvider<byte> LastWrite10msIncrement { get; }
    /// <summary>
    /// Gets or the creation time zone offset.
    /// </summary>
    /// <value>
    /// The creation time zone offset.
    /// </value>
    public IValueProvider<byte> CreationTimeZoneOffset { get; }
    /// <summary>
    /// Gets or sets the last write time zone offset.
    /// </summary>
    /// <value>
    /// The last write time zone offset.
    /// </value>
    public IValueProvider<byte> LastWriteTimeZoneOffset { get; }
    /// <summary>
    /// Gets or sets the last access time zone offset.
    /// </summary>
    /// <value>
    /// The last access time zone offset.
    /// </value>
    public IValueProvider<byte> LastAccessTimeZoneOffset { get; }

    /// <summary>
    /// Gets or sets the creation time.
    /// </summary>
    /// <value>
    /// The creation time.
    /// </value>
    public IValueProvider<DateTime> CreationTime { get; }
    /// <summary>
    /// Gets or sets the last write time.
    /// </summary>
    /// <value>
    /// The last write time.
    /// </value>
    public IValueProvider<DateTime> LastWriteTime { get; }
    /// <summary>
    /// Gets or sets the last access time.
    /// </summary>
    /// <value>
    /// The last access time.
    /// </value>
    public IValueProvider<DateTime> LastAccessTime { get; }

    /// <summary>
    /// Gets or sets the creation time offset.
    /// </summary>
    /// <value>
    /// The creation time offset.
    /// </value>
    public IValueProvider<TimeSpan> CreationTimeOffset { get; }
    /// <summary>
    /// Gets or sets the last write time offset.
    /// </summary>
    /// <value>
    /// The last write time offset.
    /// </value>
    public IValueProvider<TimeSpan> LastWriteTimeOffset { get; }
    /// <summary>
    /// Gets or sets the last access time offset.
    /// </summary>
    /// <value>
    /// The last access time offset.
    /// </value>
    public IValueProvider<TimeSpan> LastAccessTimeOffset { get; }

    /// <summary>
    /// Gets or sets the creation date time offset.
    /// </summary>
    /// <value>
    /// The creation date time offset.
    /// </value>
    public IValueProvider<DateTimeOffset> CreationDateTimeOffset { get; }
    /// <summary>
    /// Gets or sets the last write date time offset.
    /// </summary>
    /// <value>
    /// The last write date time offset.
    /// </value>
    public IValueProvider<DateTimeOffset> LastWriteDateTimeOffset { get; }
    /// <summary>
    /// Gets or sets the last access date time offset.
    /// </summary>
    /// <value>
    /// The last access date time offset.
    /// </value>
    public IValueProvider<DateTimeOffset> LastAccessDateTimeOffset { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileExFatDirectoryEntry"/> class.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    public FileExFatDirectoryEntry(Memory<byte> buffer) : base(buffer)
    {
        // the raw
        SecondaryCount = new BufferUInt8(buffer.Slice(1));
        SetChecksum = new BufferUInt16(buffer.Slice(2));
        FileAttributes = new EnumValueProvider<ExFatFileAttributes, ushort>(new BufferUInt16(buffer.Slice(4)));
        CreationTimeStamp = new BufferUInt32(buffer.Slice(8));
        LastWriteTimeStamp = new BufferUInt32(buffer.Slice(12));
        LastAccessTimeStamp = new BufferUInt32(buffer.Slice(16));
        Creation10msIncrement = new BufferUInt8(buffer.Slice(20));
        LastWrite10msIncrement = new BufferUInt8(buffer.Slice(21));
        CreationTimeZoneOffset = new BufferUInt8(buffer.Slice(22));
        LastWriteTimeZoneOffset = new BufferUInt8(buffer.Slice(23));
        LastAccessTimeZoneOffset = new BufferUInt8(buffer.Slice(24));

        // the cooked
        CreationTime = new EntryDateTime(CreationTimeStamp, Creation10msIncrement);
        LastWriteTime = new EntryDateTime(LastWriteTimeStamp, LastWrite10msIncrement);
        LastAccessTime = new EntryDateTime(LastAccessTimeStamp);

        CreationTimeOffset = new EntryTimeZone(CreationTimeZoneOffset);
        LastWriteTimeOffset = new EntryTimeZone(LastWriteTimeZoneOffset);
        LastAccessTimeOffset = new EntryTimeZone(LastAccessTimeZoneOffset);

        CreationDateTimeOffset = new EntryDateTimeOffset(CreationTime, CreationTimeOffset);
        LastWriteDateTimeOffset = new EntryDateTimeOffset(LastWriteTime, LastWriteTimeOffset);
        LastAccessDateTimeOffset = new EntryDateTimeOffset(LastAccessTime, LastAccessTimeOffset);
    }

    /// <summary>
    /// Updates the specified secondary entries.
    /// </summary>
    /// <param name="secondaryEntries">The secondary entries.</param>
    public override void Update(ICollection<ExFatDirectoryEntry> secondaryEntries)
    {
        SecondaryCount.Value = (byte)secondaryEntries.Count;
        SetChecksum.Value = ComputeChecksum(secondaryEntries);
    }

    /// <summary>
    /// Computes the checksum.
    /// </summary>
    /// <param name="secondaryEntries">The secondary entries.</param>
    /// <returns></returns>
    public ushort ComputeChecksum(IEnumerable<ExFatDirectoryEntry> secondaryEntries)
    {
        var checksum = Buffer.Span.Slice(0, 2).GetChecksum16();
        checksum = Buffer.Span.Slice(4, 28).GetChecksum16(checksum);
        foreach (var secondaryEntry in secondaryEntries)
        {
            checksum = secondaryEntry.Buffer.Span.Slice(0, 32).GetChecksum16(checksum);
        }

        return checksum;
    }
}
