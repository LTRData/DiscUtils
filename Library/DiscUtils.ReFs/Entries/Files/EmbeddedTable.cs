using DiscUtils.ReFs.BTree;

namespace DiscUtils.ReFs.Entries.Files;

/// <summary>
///     An embedded table stored as a row value (file records, directory metadata, run tables): the index root (<see cref="IndexRoot" />, the same
///     descriptor a root page has at 0x50), then the table's fixed payload (for a file record, the standard information), then the node.
///     The payload is the bytes from the end of the descriptor to the node.
/// </summary>
internal readonly ref struct EmbeddedTable
{
    public EmbeddedTable(ReadOnlySpan<byte> value)
    {
        if (value.Length < IndexRoot.MinimumLength)
        {
            throw new ReFsFormatException("Embedded table too short.");
        }

        Root = new IndexRoot(value);

        // The descriptor length is read as stored (0x28 on every table seen); a value without a descriptor has an empty payload offset of 0.
        if (Root.Length > Root.NodeOffset || Root.NodeOffset + (long)NodeView.HeaderSize > value.Length)
        {
            throw new ReFsFormatException("Embedded table offsets out of range.");
        }

        Value = value;
        NodeOffset = (int)Root.NodeOffset;
        Payload = value[Root.Length..NodeOffset];
    }

    public ReadOnlySpan<byte> Value { get; }

    /// <summary>The table's descriptor (schema id, row count).</summary>
    public IndexRoot Root { get; }

    public int NodeOffset { get; }

    public ReadOnlySpan<byte> Payload { get; }

    public NodeView Node => new NodeView(Value, NodeOffset);
}