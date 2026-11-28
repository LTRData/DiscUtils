using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using DiscUtils.ReFs.BTree.Enums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries.Files;

namespace DiscUtils.ReFs.BTree.Editing;

/// <summary>
///     Parses a row value that is an embedded table ({u32 node offset, u16 payload offset, header..., payload, leaf node})
///     into its parts, lets rows be inserted, replaced and removed, and builds a new value that every reader of this library
///     parses unchanged. Only leaf nodes are supported (embedded tables seen on disk are leaves unless very large).
///     Rows are kept in key order (unsigned little-endian 32-bit words from the start, then length).
/// </summary>
internal sealed class EmbeddedTableEditor
{
    private const int NodeHeaderLength = NodeView.HeaderSize;
    private const int EntryHeaderLength = 16;
    private const int CompactEntryLength = 24;
    private const int GapBeforeSlots = 4;
    private const ushort SlotAux = 0xFFFF;
    private const int LegacyStreamUserOffset = 12;
    private const int LegacyStreamUserLength = 84;
    private const int FileRecordPayloadLength = 128;
    private readonly byte[] _nodeHeader;

    private readonly byte[] _prefix; // bytes [0, payload offset): the embedded header, copied as is
    private readonly List<EmbeddedRow> _rows;

    private EmbeddedTableEditor(byte[] prefix, byte[] payload, byte[] nodeHeader, List<EmbeddedRow> rows)
    {
        _prefix = prefix;
        Payload = payload;
        _nodeHeader = nodeHeader;
        _rows = rows;
    }

    /// <summary>The fixed payload (standard information of a file record); editable in place.</summary>
    public byte[] Payload { get; }

    public IReadOnlyList<EmbeddedRow> Rows => _rows;

    /// <summary>Sets the u16 at +0x16 of a run table's header: the checksum kind of its runs (0 none, 1 CRC-32C, 2 CRC-64).</summary>
    public void SetChecksumKind(ushort kind) => BitConverter.TryWriteBytes(_prefix.AsSpan(0x16), kind);

    /// <summary>
    ///     Parses an embedded table. When its node is a branch (ReFS 3.1 keeps the rows of every file record in a page of
    ///     their own), <paramref name="readBranchRows" /> supplies the rows and the table is rebuilt as one leaf, which exists
    ///     only in memory. Without the delegate a branch is not supported.
    /// </summary>
    public static EmbeddedTableEditor Parse(ReadOnlySpan<byte> value, EmbeddedRowReader? readBranchRows = null)
    {
        var table = new EmbeddedTable(value);
        int payloadOffset = value.U16(4);
        var node = table.Node;
        var nodeHeader = value.Slice(table.NodeOffset, NodeHeaderLength).ToArray();
        List<EmbeddedRow> rows;

        if (!node.IsLeaf)
        {
            if (readBranchRows is null)
            {
                throw new NotSupportedException("Editing an embedded table that is a branch node is not supported.");
            }

            rows = readBranchRows(value, table.NodeOffset);
            rows.Sort((a, b) => CompareKeys(a.Key, b.Key));
            nodeHeader[12] = 0; // height: a leaf
            nodeHeader[13] = (byte)(((NodeProperties)nodeHeader[13] & ~(NodeProperties.Interior | NodeProperties.KeyIndexValid)) | NodeProperties.Root); // other flags (a run table's) stay
        }
        else
        {
            rows = new List<EmbeddedRow>(node.Count);

            for (var i = 0; i < node.Count; i++)
            {
                var entry = node[i];
                rows.Add(new EmbeddedRow(entry.Flags, entry.Key.ToArray(), entry.Value.ToArray()));
            }
        }

        return new EmbeddedTableEditor(
            value[..payloadOffset].ToArray(),
            table.Payload.ToArray(),
            nodeHeader,
            rows);
    }

    /// <summary>
    ///     A new, empty run table in the layout of ReFS 3.1 to 3.5 (seen on Server 2016 and 2019 lab images): the embedded header
    ///     carries the table's schema id at +0x0C and +0x10 (0x120 on 3.1, 0x1A0 on 3.4 and 3.5), and the 96-byte payload is
    ///     {u64 1, u32 0} followed by 84 bytes that the log supplies as the value of the row it inserts before
    ///     <c>CreateTable</c> (the stream summary sits at their start: allocated, size, valid length).
    /// </summary>
    public static EmbeddedTableEditor CreateLegacyStreamTable(ReadOnlySpan<byte> rowValue, uint schema)
    {
        if (rowValue.Length != LegacyStreamUserLength)
        {
            throw new NotSupportedException($"a new run table row of {rowValue.Length} bytes is not understood");
        }

        var table = CreateStreamTable();
        BitConverter.TryWriteBytes(table._prefix.AsSpan(0x0C), schema);
        BitConverter.TryWriteBytes(table._prefix.AsSpan(0x10), schema);
        table.Payload.AsSpan().Clear();
        BitConverter.TryWriteBytes(table.Payload.AsSpan(), 1UL);
        rowValue.CopyTo(table.Payload.AsSpan(LegacyStreamUserOffset));
        return table;
    }

    /// <summary>
    ///     An empty file record as <c>CreateTable</c> on a row path makes it (ReFS 3.1 to 3.5 create a file this way; the
    ///     standard information and the rows follow as records of their own): the embedded header, a zeroed 128-byte payload
    ///     and a leaf without rows. The schema id words of the header (+0x0C/+0x10) are left 0: the record does not log
    ///     them and this reader does not use them.
    /// </summary>
    public static EmbeddedTableEditor CreateFileRecord()
    {
        var prefix = new byte[0x28];
        BitConverter.TryWriteBytes(prefix.AsSpan(4), 0x28);
        BitConverter.TryWriteBytes(prefix.AsSpan(6), 1);
        BitConverter.TryWriteBytes(prefix.AsSpan(8), 1u);
        BitConverter.TryWriteBytes(prefix.AsSpan(0x14), 2u);

        var nodeHeader = new byte[NodeHeaderLength];
        nodeHeader[13] = (byte)NodeProperties.Root;
        return new EmbeddedTableEditor(prefix, new byte[FileRecordPayloadLength], nodeHeader, []);
    }

    /// <summary>
    ///     A new, empty stream run table as <c>CreateTable</c> makes it: embedded header {nodeOffset, u16 0x28, u16 1, u32 1, u32 schema,
    ///     u32 schema, u32 2, zeros} (schema 0x200 on every ReFS 3.14 stream table, 0x1A0 on 3.10), a 96-byte payload (u32 0, u32 0x28, then the 40-byte
    ///     stream summary starting at +0: allocated @+8, size @+0x10, valid length @+0x18) and a leaf node without rows.
    /// </summary>
    public static EmbeddedTableEditor CreateStreamTable(uint schema = 0x200)
    {
        var prefix = new byte[0x28];
        BitConverter.TryWriteBytes(prefix.AsSpan(4), 0x28);
        BitConverter.TryWriteBytes(prefix.AsSpan(6), 1);
        BitConverter.TryWriteBytes(prefix.AsSpan(8), 1u);
        BitConverter.TryWriteBytes(prefix.AsSpan(0x0C), schema);
        BitConverter.TryWriteBytes(prefix.AsSpan(0x10), schema);
        BitConverter.TryWriteBytes(prefix.AsSpan(0x14), 2u);

        var payload = new byte[96];
        BitConverter.TryWriteBytes(payload.AsSpan(4), 0x28u);

        var nodeHeader = new byte[NodeHeaderLength];
        nodeHeader[13] = 0x06; // run table leaf (0x0E / 0x06 seen on disk)
        return new EmbeddedTableEditor(prefix, payload, nodeHeader, []);
    }

    /// <summary>Replaces all rows (used after a run table was edited as a whole).</summary>
    public void ReplaceRows(IEnumerable<EmbeddedRow> rows)
    {
        List<EmbeddedRow> sorted = [.. rows];
        sorted.Sort((a, b) => CompareKeys(a.Key, b.Key));
        _rows.Clear();
        _rows.AddRange(sorted);
    }

    public bool TryFind(byte[] key, [NotNullWhen(true)] out EmbeddedRow? found)
    {
        foreach (var row in _rows)
        {
            if (row.Key.AsSpan().SequenceEqual(key))
            {
                found = row;
                return true;
            }
        }

        found = null;
        return false;
    }

    /// <summary>Adds the row, or replaces the row with the same key.</summary>
    public void Set(EmbeddedRow row)
    {
        var existing = _rows.FindIndex(r => r.Key.AsSpan().SequenceEqual(row.Key));

        if (existing >= 0)
        {
            _rows[existing] = row;
            return;
        }

        var index = _rows.FindIndex(r => CompareKeys(r.Key, row.Key) > 0);
        _rows.Insert(index < 0 ? _rows.Count : index, row);
    }

    public bool Remove(byte[] key)
    {
        var existing = _rows.FindIndex(r => r.Key.AsSpan().SequenceEqual(key));

        if (existing < 0)
        {
            return false;
        }

        _rows.RemoveAt(existing);
        return true;
    }

    public byte[] Build()
    {
        List<byte[]> entries = [];
        var dataLength = 0;

        foreach (var row in _rows)
        {
            var entry = BuildEntry(row);
            entries.Add(entry);
            dataLength += entry.Length;
        }

        var dataStart = NodeHeaderLength;
        var dataEnd = dataStart + dataLength;
        var slotOffset = dataEnd + GapBeforeSlots;
        var nodeLength = slotOffset + (4 * _rows.Count);
        var nodeOffset = _prefix.Length + Payload.Length;

        // A slot on disk is {u16 entry offset, u16 key index}, which cannot describe a node above 64 KiB. A table that large
        // (many runs, rebuilt from a branch) exists only in memory: it is marked WideSlots and each slot is one u32 offset.
        var wide = dataEnd > ushort.MaxValue;

        var result = new byte[nodeOffset + nodeLength];
        _prefix.CopyTo(result, 0);
        BitConverter.TryWriteBytes(result.AsSpan(0), (uint)nodeOffset);
        Payload.CopyTo(result, _prefix.Length);

        var node = result.AsSpan(nodeOffset);
        _nodeHeader.CopyTo(node);
        BitConverter.TryWriteBytes(node[..], (uint)dataStart);
        BitConverter.TryWriteBytes(node[4..], (uint)dataEnd);
        BitConverter.TryWriteBytes(node[8..], (uint)GapBeforeSlots);
        BitConverter.TryWriteBytes(node[16..], (uint)slotOffset);
        BitConverter.TryWriteBytes(node[20..], (uint)_rows.Count);
        BitConverter.TryWriteBytes(node[0x20..], (uint)nodeLength);
        node[13] = (byte)(wide ? node[13] | (byte)NodeProperties.WideSlots : node[13] & ~(byte)NodeProperties.WideSlots);

        var position = dataStart;

        for (var i = 0; i < entries.Count; i++)
        {
            entries[i].CopyTo(node[position..]);

            if (wide)
            {
                BitConverter.TryWriteBytes(node[(slotOffset + (4 * i))..], (uint)position);
            }
            else
            {
                BitConverter.TryWriteBytes(node[(slotOffset + (4 * i))..], (ushort)position);
                BitConverter.TryWriteBytes(node[(slotOffset + (4 * i) + 2)..], SlotAux);
            }

            position += entries[i].Length;
        }

        return result;
    }

    /// <summary>Key order of embedded tables: unsigned little-endian 32-bit words from the start (zero padded), then the shorter key first.</summary>
    internal static int CompareKeys(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        var words = (Math.Max(a.Length, b.Length) + 3) / 4;

        for (var i = 0; i < words; i++)
        {
            var c = Word(a, i).CompareTo(Word(b, i));

            if (c != 0)
            {
                return c;
            }
        }

        return a.Length.CompareTo(b.Length);
    }

    private static uint Word(ReadOnlySpan<byte> key, int index)
    {
        var offset = index * 4;

        if (offset >= key.Length)
        {
            return 0;
        }

        Span<byte> word = stackalloc byte[4];
        key.Slice(offset, Math.Min(4, key.Length - offset)).CopyTo(word);
        return BinaryPrimitives.ReadUInt32LittleEndian(word);
    }

    private static byte[] BuildEntry(EmbeddedRow row)
    {
        if (row.IsCompact)
        {
            if (row.Value.Length < CompactEntryLength || row.Value.AsSpan().U16(10) != row.Value.Length)
            {
                throw new NotSupportedException("Only compact entries whose size field matches their length can be rebuilt.");
            }

            var padded = new byte[(row.Value.Length + 7) & ~7]; // the size field is exact; entries sit 8-byte aligned in the node
            row.Value.CopyTo(padded, 0);
            return padded;
        }

        var valueOffset = (EntryHeaderLength + row.Key.Length + 7) & ~7;
        var size = (valueOffset + row.Value.Length + 7) & ~7;
        var entry = new byte[size];
        BitConverter.TryWriteBytes(entry.AsSpan(0), (uint)size);
        BitConverter.TryWriteBytes(entry.AsSpan(4), EntryHeaderLength);
        BitConverter.TryWriteBytes(entry.AsSpan(6), (ushort)row.Key.Length);
        BitConverter.TryWriteBytes(entry.AsSpan(8), (ushort)row.Flags);
        BitConverter.TryWriteBytes(entry.AsSpan(10), (ushort)valueOffset);
        BitConverter.TryWriteBytes(entry.AsSpan(12), (uint)row.Value.Length);
        row.Key.CopyTo(entry, EntryHeaderLength);
        row.Value.CopyTo(entry, valueOffset);
        return entry;
    }
}