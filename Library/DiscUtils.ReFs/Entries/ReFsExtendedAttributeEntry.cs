using System.Text;
using DiscUtils.ReFs.BTree;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Entries.Enums;
using DiscUtils.ReFs.Entries.Files;
#if NET46
using DiscUtils.Streams.Compatibility;
#endif

namespace DiscUtils.ReFs.Entries;

/// <summary>
///     One extended attribute (EA) of a file or directory. <see cref="Name" /> is stored on disk as OEM-code-page bytes
///     (1 to 253 legal filename characters) and decoded as Latin-1 so every byte survives. Windows
///     upper-cases names when it stores them (verified: "mixedCase" is read back as "MIXEDCASE"), so the original case
///     is not recoverable; compare names case-insensitively. <see cref="Value" /> is the raw value (0 to 65,535 bytes).
/// </summary>
internal sealed record ReFsExtendedAttributeEntry(string Name, ReFsExtendedAttributeOptions Flags, ReadOnlyMemory<byte> Value)
{
    /// <summary>
    ///     Largest PACKED EA list Windows accepts: the sum of (name length + value length + 5) over all entries.
    /// </summary>
    internal const int MaximumListLength = 0xFFFB;

    private const int EntryHeaderLength = 8; // u32 next entry offset, u8 flags, u8 name length, u16 value length
    private const int FragmentedValueHeaderLength = 12; // u32 0, u32 data length, u32 data offset (0x0C)


    // The limit applies to the PACKED size (sum of name + value + 5 per entry). Entries are stored padded to 4 bytes with an
    // 8-byte header, so the stored list can reach about 12/7 of it (one-character names with one-byte values); allow 2x.
    internal const int MaximumValueLength = FragmentedValueHeaderLength + (2 * MaximumListLength) + 0x1000;

    public bool IsNeedEa => Flags.HasFlag(ReFsExtendedAttributeOptions.NeedEa);

    public bool Equals(ReFsExtendedAttributeEntry? other) => other is not null && Name == other.Name && Flags == other.Flags && Value.Span.SequenceEqual(other.Value.Span);

    public override int GetHashCode() => HashCode.Combine(Name, Flags, Value.Length);

    /// <summary>
    ///     Reads the EA list out of a file record or a directory's own metadata row (both embedded tables). The EA data
    ///     is the row with key {u32 total size, u32 fragment offset, 0x80000001, 0xE0}; large lists are split into
    ///     several such rows (4080 bytes each) that concatenate into {u32 0, u32 data length, u32 0x0C, entries}.
    ///     The entries are FILE_FULL_EA_INFORMATION records whose NextEntryOffset is the 4-byte aligned entry size
    ///     (also on the last entry). The list is walked as Windows walks it: by the computed size (name + value + 9, aligned to 4), not by the
    ///     stored NextEntryOffset, to the end of the data.
    /// </summary>
    internal static IReadOnlyList<ReFsExtendedAttributeEntry> Read(TreeReader tree, ReadOnlySpan<byte> recordValue)
    {
        var table = new EmbeddedTable(recordValue);
        byte[]? assembled = null;
        var assembledLength = 0;
        var covered = 0;

        foreach (var row in tree.Rows(recordValue, table.NodeOffset))
        {
            var key = row.Key;

            if (FileRecord.Classify(row) != FileRowKind.ExtendedAttributes)
            {
                continue;
            }

            var total = key.U32(0);
            var offset = key.U32(4);
            var fragment = row.Value;

            if (total < FragmentedValueHeaderLength || total > MaximumValueLength || offset + (ulong)fragment.Length > total)
            {
                throw new ReFsFormatException("Malformed extended attribute data row.");
            }

            if (assembled is null)
            {
                assembled = new byte[total];
                assembledLength = (int)total;
            }
            else if (total != assembledLength)
            {
                throw new ReFsFormatException("Extended attribute fragments disagree about the total size.");
            }

            // The fragments must follow each other in key order without gaps or overlaps, and the data ends at the total
            // (Windows refuses any other fragment).
            if (covered == assembledLength)
            {
                break;
            }

            if (offset != covered)
            {
                throw new ReFsFormatException($"Extended attribute fragment at offset {offset} does not continue the data at {covered}.");
            }

            fragment.CopyTo(assembled.AsSpan((int)offset));
            covered += fragment.Length;
        }

        if (assembled is null)
        {
            return [];
        }

        if (covered != assembledLength)
        {
            throw new ReFsFormatException($"Extended attribute data is incomplete ({covered} of {assembledLength} bytes).");
        }

        return ParseList(assembled);
    }

    internal static List<ReFsExtendedAttributeEntry> ParseList(ReadOnlySpan<byte> value)
    {
        var dataLength = value.U32(4);
        var dataOffset = value.U32(8);

        if (dataOffset < FragmentedValueHeaderLength || dataOffset + (ulong)dataLength > (ulong)value.Length)
        {
            throw new ReFsFormatException("Extended attribute list header is out of range.");
        }

        var list = value.Slice((int)dataOffset, (int)dataLength);
        List<ReFsExtendedAttributeEntry> result = [];
        var position = 0;

        while (position < list.Length)
        {
            var entry = list[position..];

            if (entry.Length < EntryHeaderLength)
            {
                throw new ReFsFormatException("Truncated extended attribute entry.");
            }

            var flags = (ReFsExtendedAttributeOptions)entry[4];
            int nameLength = entry[5];
            int valueLength = entry.U16(6);
            var used = EntryHeaderLength + nameLength + 1 + valueLength;

            if (used > entry.Length || entry[EntryHeaderLength + nameLength] != 0)
            {
                throw new ReFsFormatException("Malformed extended attribute entry.");
            }

            result.Add(new ReFsExtendedAttributeEntry(
                Encoding.Latin1.GetString(entry.Slice(EntryHeaderLength, nameLength)),
                flags,
                entry.Slice(EntryHeaderLength + nameLength + 1, valueLength).ToArray()));

            position += (used + 3) & ~3;
        }

        return result;
    }
}