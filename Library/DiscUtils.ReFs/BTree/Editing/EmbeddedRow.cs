using DiscUtils.ReFs.BTree.Enums;

namespace DiscUtils.ReFs.BTree.Editing;

/// <summary>A row of an embedded table: normal rows keep key and value apart, compact rows keep their 24-byte entry whole (the key is its last 12 bytes).</summary>
internal sealed record EmbeddedRow(EntryProperties Flags, byte[] Key, byte[] Value)
{
    public bool IsCompact => Flags.HasFlag(EntryProperties.Compact);

    public bool IsEmbeddedTable => !IsCompact && Flags.HasFlag(EntryProperties.EmbeddedTable);
}