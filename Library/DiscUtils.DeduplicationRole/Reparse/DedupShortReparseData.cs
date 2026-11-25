using DiscUtils.Streams;

namespace DiscUtils.DeduplicationRole.Reparse;

/// <summary>
///     Data Deduplication reparse data in the short format (<see cref="Enums.DedupReparseFormat.Short" />; from NTFS samples in the open-source readers,
///     not seen on ReFS). Offsets from the start of the reparse data:
///     <code>
///     +0x00 u16  format (0x0201)
///     +0x02 u16  data length
///     +0x08 u64  file size
///     +0x18      chunk store GUID
///     +0x38      the first 16 bytes of a range entry (<see cref="DedupStreamReference" />), for the whole file
///     </code>
/// </summary>
internal sealed class DedupShortReparseData
{
    private const int Length = 0x58;

    public DedupShortReparseData(ReadOnlySpan<byte> data)
    {
        if (data.Length < Length)
            throw new DedupFormatException("Data Deduplication reparse data is too short.");

        FileSize = data.U64(0x08);
        StoreId = EndianUtilities.ToGuidLittleEndian(data.Slice(0x18, 16));
        Range = new DedupStreamReference(data.Slice(0x38, 16), 0, FileSize);
    }

    public ulong FileSize { get; }

    public Guid StoreId { get; }

    public DedupStreamReference Range { get; }
}
