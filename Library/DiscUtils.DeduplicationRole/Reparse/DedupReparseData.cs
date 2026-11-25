using DiscUtils.Internal;
using DiscUtils.DeduplicationRole.Reparse.Enums;

namespace DiscUtils.DeduplicationRole.Reparse;

/// <summary>
///     The reparse data of a file optimized by the Data Deduplication role (<c>IO_REPARSE_TAG_DEDUP</c>, 0x80000013). Not documented; layouts from
///     the open-source readers ntfs-3g-dedup-plugin and DedupInspector, checked against the Server 2025 lab image <c>w2025-dedup</c>. It starts with
///     u16 format (<see cref="DedupReparseFormat" />), u16 data length:
///     <list type="bullet">
///         <item>
///             The property formats: then the <see cref="DedupFileEntry" /> block ("FeRp", with the "DdRp" block inside), and a CRC-32 of everything
///             before it as the last 4 bytes.
///         </item>
///         <item>The short format: <see cref="DedupShortReparseData" />.</item>
///     </list>
/// </summary>
internal sealed record DedupReparseData
{
    public const uint Tag = 0x80000013;
    private const int FormatHeaderLength = 4;
    private const int CrcLength = 4;

    public DedupReparseData(ReadOnlyMemory<byte> data)
    {
        ReadOnlySpan<byte> span = data.Span;
        if (span.Length < FormatHeaderLength + CrcLength)
            throw new DedupFormatException("Data Deduplication reparse data is too short.");

        Format = (DedupReparseFormat)span.U16(0);
        switch (Format)
        {
            case DedupReparseFormat.Properties or DedupReparseFormat.Properties3:
            {
                if (Crc32LittleEndian.Compute(Crc32Algorithm.Common, span[..^CrcLength]) != span.U32(span.Length - CrcLength))
                    throw new DedupFormatException("Data Deduplication reparse data fails its CRC-32.");

                DedupFileEntry file = new DedupFileEntry(data[FormatHeaderLength..^CrcLength]);
                if (file.HasRecallBitmap)
                    throw new NotSupportedException("Data Deduplication files with a recall bitmap are not supported (no witness).");

                StoreId = file.StoreId;
                FileSize = file.FileSize;
                Ranges = InFileOrder(file.Data.Ranges, FileSize);
                break;
            }
            case DedupReparseFormat.Short:
            {
                DedupShortReparseData file = new DedupShortReparseData(span);
                StoreId = file.StoreId;
                FileSize = file.FileSize;
                Ranges = InFileOrder([file.Range], FileSize);
                break;
            }
            default:
                throw new NotSupportedException($"Data Deduplication reparse format 0x{(ushort)Format:X4} is not supported.");
        }
    }

    public DedupReparseFormat Format { get; }

    /// <summary>The chunk store (the folder <c>System Volume Information\Dedup\ChunkStore\{GUID}.ddp</c>).</summary>
    public Guid StoreId { get; }

    public ulong FileSize { get; }

    /// <summary>The file's ranges in the chunk store, in file order: from 0, contiguous, ending within the file.</summary>
    public IReadOnlyList<DedupStreamReference> Ranges { get; }

    private static DedupStreamReference[] InFileOrder(IEnumerable<DedupStreamReference> ranges, ulong fileSize)
    {
        DedupStreamReference[] sorted = [.. ranges.OrderBy(range => range.Begin)];
        ulong next = 0;
        foreach (DedupStreamReference range in sorted)
        {
            if (range.Begin != next || range.End < range.Begin)
                throw new DedupFormatException($"Data Deduplication ranges are not contiguous at byte {range.Begin}.");

            next = range.End;
        }

        if (sorted.Length == 0 || next > fileSize)
            throw new DedupFormatException("Data Deduplication reparse data does not describe the file's ranges.");

        return sorted;
    }
}
