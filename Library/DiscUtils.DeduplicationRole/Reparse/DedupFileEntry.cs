using DiscUtils.Streams;
using DiscUtils.DeduplicationRole.Reparse.Enums;

namespace DiscUtils.DeduplicationRole.Reparse;

/// <summary>
///     The "FeRp" property block of Data Deduplication reparse data (<see cref="DedupPropertyBlock" />). Properties by index, as in the open-source
///     readers and on the lab image <c>w2025-dedup</c>:
///     <code>
///     0 Version     revision (1)
///     1 Version     flags (0)
///     2 Guid        chunk store id (the folder {GUID}.ddp)
///     3 FileTime    when the file was optimized
///     4 QWord       file size
///     5 Id          (0)
///     6 BitmapFlag  bitmap type (2)
///     7 Blob        "RbRp" recall bitmap (empty on every lab file)
///     8 Blob        "DdRp" (<see cref="DedupDataBlock" />)
///     </code>
/// </summary>
internal sealed record DedupFileEntry
{
    private const uint Signature = 0x70526546; // "FeRp"
    private const int StoreIdIndex = 2;
    private const int OptimizedIndex = 3;
    private const int FileSizeIndex = 4;
    private const int BitmapTypeIndex = 6;
    private const int RecallBitmapIndex = 7;
    private const int DataIndex = 8;

    public DedupFileEntry(ReadOnlyMemory<byte> bytes)
    {
        DedupPropertyBlock block = new DedupPropertyBlock(bytes, Signature, "FeRp");
        StoreId = EndianUtilities.ToGuidLittleEndian(block.Get(StoreIdIndex, DedupPropertyType.Guid, 16).Span);
        Optimized = DateTimeOffset.FromFileTime((long)block.Get(OptimizedIndex, DedupPropertyType.FileTime, 8).Span.U64(0));
        FileSize = block.Get(FileSizeIndex, DedupPropertyType.QWord, 8).Span.U64(0);
        BitmapType = block.Get(BitmapTypeIndex, DedupPropertyType.BitmapFlag, 4).Span.U32(0);
        HasRecallBitmap = block.Has(RecallBitmapIndex);
        Data = new DedupDataBlock(block.Get(DataIndex, DedupPropertyType.Blob));
    }

    /// <summary>The chunk store (the folder <c>System Volume Information\Dedup\ChunkStore\{GUID}.ddp</c>).</summary>
    public Guid StoreId { get; }

    public DateTimeOffset Optimized { get; }

    public ulong FileSize { get; }

    public uint BitmapType { get; }

    public bool HasRecallBitmap { get; }

    public DedupDataBlock Data { get; }
}
