using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Replay.Enums;

namespace DiscUtils.ReFs.Replay;

/// <summary>
///     One redo record: operation, flags, the table it applies to, and its values (for row operations value 0 is the key, followed by the row data).
///     Header of 0x38 bytes: 0x00 u32 (unknown; not read); 0x04 u32 operation (<see cref="RedoOperation" />); 0x08 u32 path count; 0x0C u32 path offset
///     (0x38); 0x10 u32 value count; 0x14 u32 value offset (0x38 plus 8 per path, inferred); 0x18 u64 the checkpoint virtual clock when the record was
///     written; 0x20 u64 the restart-clock field (<see cref="RestartClock" />); 0x2C u32 flags (<see cref="RedoFlags" />); 0x30 to 0x37 not decoded.
///     Then 8-byte descriptors for the path and the values.
/// </summary>
internal sealed record RedoRecord
{
    private const int HeaderLength = 0x38;
    private const int DescriptorLength = 8;
    private const int MaxDescriptors = 64;
    private const int PathComponentHeaderLength = 12;

    /// <summary>A record from its fields (synthetic records in tests, and the replay's copies with a redirected path).</summary>
    public RedoRecord(ulong entryLsn, int block, RedoOperation operation, RedoFlags flags, ulong clock, IReadOnlyList<RedoPathComponent> path, bool pathInherited,
        IReadOnlyList<ReadOnlyMemory<byte>> values)
    {
        EntryLsn = entryLsn;
        Block = block;
        Operation = operation;
        Flags = flags;
        Clock = clock;
        Path = path;
        PathInherited = pathInherited;
        Values = values;
    }

    /// <summary>Parses one record of block <paramref name="block" /> of an entry; a record without path components takes <paramref name="previousPath" />.</summary>
    public RedoRecord(ulong entryLsn, int block, ReadOnlySpan<byte> record, IReadOnlyList<RedoPathComponent> previousPath)
    {
        var pathCount = record.U32(8);
        var pathOffset = record.U32(0x0C);
        var valueCount = record.U32(0x10);
        var valueOffset = record.U32(0x14);

        if (pathCount > MaxDescriptors || valueCount > MaxDescriptors
                                       || pathOffset + ((ulong)pathCount * DescriptorLength) > (ulong)record.Length
                                       || valueOffset + ((ulong)valueCount * DescriptorLength) > (ulong)record.Length)
        {
            throw new ReFsFormatException("Malformed redo record descriptors.");
        }

        EntryLsn = entryLsn;
        Block = block;
        Operation = (RedoOperation)record.U32(4);
        Clock = record.U64(0x18);
        RestartClock = record.U64(0x20);
        Flags = (RedoFlags)record.U32(0x2C);
        PathInherited = pathCount == 0;
        Path = PathInherited ? previousPath : PathComponents(record, pathOffset, pathCount);

        var values = new ReadOnlyMemory<byte>[valueCount];

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = Descriptor(record, valueOffset, i).ToArray();
        }

        Values = values;
    }

    /// <summary>LSN of the log entry that holds the record.</summary>
    public ulong EntryLsn { get; }

    /// <summary>Index of the record's block in that entry.</summary>
    public int Block { get; }

    public RedoOperation Operation { get; }

    public RedoFlags Flags { get; init; }

    /// <summary>Header +0x18: the checkpoint virtual clock when the record was written.</summary>
    public ulong Clock { get; }

    /// <summary>The table the record applies to, from the root table down.</summary>
    public IReadOnlyList<RedoPathComponent> Path { get; init; }

    /// <summary>Whether the record had no path of its own and took the previous record's.</summary>
    public bool PathInherited { get; }

    /// <summary>The operation's values (for row operations value 0 is the key, followed by the row data).</summary>
    public IReadOnlyList<ReadOnlyMemory<byte>> Values { get; }

    /// <summary>Header +0x20: the value replay compares with the checkpoint's restart clock.</summary>
    public ulong RestartClock { get; init; }

    public bool IsBegin => Flags.HasFlag(RedoFlags.Begin);

    public bool IsCommit => Flags.HasFlag(RedoFlags.Commit);

    /// <summary>
    ///     Parses the records of one log entry payload: blocks of {u32 length of the records, u32 header length (8)} followed
    ///     by 8-aligned records of at least 0x38 bytes. A record with no path components reuses the previous record's path
    ///     (<paramref name="previousPath" /> carries it from record to record, across entries).
    /// </summary>
    public static List<RedoRecord> ParsePayload(ulong entryLsn, ReadOnlySpan<byte> payload, ref IReadOnlyList<RedoPathComponent> previousPath)
    {
        List<RedoRecord> records = [];
        var position = 0;
        var block = 0;

        while (position + 8 <= payload.Length)
        {
            var recordsLength = payload.U32(position);
            var headerLength = payload.U32(position + 4);

            if (recordsLength == 0)
            {
                break;
            }

            if (headerLength != 8 || (ulong)position + headerLength + recordsLength > (ulong)payload.Length)
            {
                throw new ReFsFormatException($"Malformed redo block at 0x{position:X} of entry 0x{entryLsn:X}.");
            }

            var blockData = payload.Slice(position + (int)headerLength, (int)recordsLength);
            var recordPosition = 0;

            while (recordPosition < blockData.Length)
            {
                if (blockData.Length - recordPosition < HeaderLength)
                {
                    throw new ReFsFormatException("Truncated redo record header.");
                }

                var size = blockData.U32(recordPosition);

                if (size < HeaderLength || (size & 7) != 0 || (ulong)recordPosition + size > (ulong)blockData.Length)
                {
                    throw new ReFsFormatException($"Bad redo record size 0x{size:X} in entry 0x{entryLsn:X}.");
                }

                var parsed = new RedoRecord(entryLsn, block, blockData.Slice(recordPosition, (int)size), previousPath);
                records.Add(parsed);
                previousPath = parsed.Path;
                recordPosition += (int)size;
            }

            position += (int)headerLength + (int)recordsLength;
            block++;
        }

        return records;
    }

    /// <summary>Path components {u32 kind, u32 property, u32 0, key} from their descriptors.</summary>
    private static RedoPathComponent[] PathComponents(ReadOnlySpan<byte> record, uint pathOffset, uint pathCount)
    {
        var components = new RedoPathComponent[pathCount];

        for (var i = 0; i < components.Length; i++)
        {
            var component = Descriptor(record, pathOffset, i);

            if (component.Length < PathComponentHeaderLength)
            {
                throw new ReFsFormatException("Redo path component too short.");
            }

            components[i] = new RedoPathComponent(component.U32(0), component.U32(4), component[PathComponentHeaderLength..].ToArray());
        }

        return components;
    }

    /// <summary>The bytes descriptor <paramref name="index" /> ({u32 offset, u32 length} in the table at <paramref name="table" />) names.</summary>
    private static ReadOnlySpan<byte> Descriptor(ReadOnlySpan<byte> record, uint table, int index)
    {
        var offset = record.U32((int)table + (index * DescriptorLength));
        var length = record.U32((int)table + (index * DescriptorLength) + 4);

        if (offset + (ulong)length > (ulong)record.Length)
        {
            throw new ReFsFormatException("Redo record field out of range.");
        }

        return record.Slice((int)offset, (int)length);
    }
}