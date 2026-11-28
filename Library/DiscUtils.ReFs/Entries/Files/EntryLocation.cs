using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Mount;

namespace DiscUtils.ReFs.Entries.Files;

/// <summary>
///     Where a directory row came from: a leaf page and slot (valid for the checkpoint the volume was opened at), or, for a
///     row produced by redo-log replay, the replayed key and value themselves. <see cref="IncompleteReason" /> is set when
///     replay could not apply every change to the row's embedded table.
/// </summary>
internal readonly record struct EntryLocation(PageRef Page, int Slot, byte[]? ReplayedKey = null, byte[]? ReplayedValue = null, string? IncompleteReason = null)
{
    public bool IsReplayed => ReplayedValue is not null;

    public static EntryLocation Replayed(byte[] key, byte[] value, string? incompleteReason) => new EntryLocation(default, -1, key, value, incompleteReason);

    /// <summary>The key and value of the row, copied out of its page (or the replayed row itself).</summary>
    public (byte[] Key, byte[] Value) ReadRow(PageReader pages)
    {
        if (IsReplayed)
        {
            return (ReplayedKey!, ReplayedValue!);
        }

        var row = StoredRow(pages);
        return (row.Key.ToArray(), row.Value.ToArray());
    }

    /// <summary>The value of the row, copied out of its page (or the replayed value itself).</summary>
    public byte[] ReadValue(PageReader pages) => IsReplayed ? ReplayedValue! : StoredRow(pages).Value.ToArray();

    private EntryView StoredRow(PageReader pages)
    {
        var node = pages.Read(Page).Node;
        return Slot < node.Count ? node[Slot] : throw new ReFsFormatException("Directory entry location is stale.");
    }
}