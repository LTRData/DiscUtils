using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries.Enums;
using DiscUtils.ReFs.SystemTables;

namespace DiscUtils.ReFs.Entries.Directories;

/// <summary>
///     Ordering of directory table rows: rows sort by their first u16 type header
///     (0x10 own metadata &lt; 0x20 file-id links &lt; 0x30 entries), and entry rows by upper-cased UTF-16 code units
///     (ordinal) of the name, then raw UTF-16 code units. Windows' name comparison ignores row kind.
/// </summary>
internal static class DirectoryKeyOrder
{
    private const byte EntryType = 0x30;
    private const byte LinkRecordType = 0x40;

    /// <summary>
    ///     Compares a row key with the folded name: sign of (row - target).
    ///     Supplying <paramref name="exactName" /> enables the raw-code-unit tie-break; otherwise folded ties compare equal.
    /// </summary>
    public static int Compare(ReadOnlySpan<byte> rowKey, string upperName, UpcaseTable upcase, string? exactName = null)
    {
        if (rowKey.Length < 4)
        {
            return -1;
        }

        var byType = rowKey.U16(0).CompareTo(EntryType);

        if (byType != 0)
        {
            return byType;
        }

        var byName = CompareName(rowKey[4..], upperName, upcase);

        if (byName != 0 || exactName is null)
        {
            return byName;
        }

        for (var i = 0; i < exactName.Length; i++)
        {
            var order = ((char)rowKey.U16(4 + (2 * i))).CompareTo(exactName[i]);

            if (order != 0)
            {
                return order;
            }
        }

        return 0;
    }

    /// <summary>
    ///     Compares a row key with a hard-link record key {u32 0x80000040, u32 0, u64 fileId, u64 holder directory}:
    ///     sign of (row - target). Rows of other types order by their first byte.
    /// </summary>
    public static int CompareLinkRecord(ReadOnlySpan<byte> rowKey, ulong fileId, ulong holderDirectory)
    {
        if (rowKey.Length < 4)
        {
            return -1;
        }

        var byType = rowKey[0].CompareTo(LinkRecordType);

        if (byType != 0 || rowKey.U32(0) != (uint)RowType.LinkRecord)
        {
            return byType != 0 ? byType : rowKey.U32(0).CompareTo((uint)RowType.LinkRecord);
        }

        if (rowKey.Length < 24)
        {
            return -1;
        }

        var byFile = rowKey.U64(8).CompareTo(fileId);
        return byFile != 0 ? byFile : rowKey.U64(16).CompareTo(holderDirectory);
    }

    private static int CompareName(ReadOnlySpan<byte> utf16Name, string upperTarget, UpcaseTable upcase)
    {
        var units = utf16Name.Length / 2;
        var common = Math.Min(units, upperTarget.Length);

        for (var i = 0; i < common; i++)
        {
            var c = upcase.Fold((char)utf16Name.U16(2 * i));
            var order = c.CompareTo(upperTarget[i]);

            if (order != 0)
            {
                return order;
            }
        }

        return units.CompareTo(upperTarget.Length);
    }
}