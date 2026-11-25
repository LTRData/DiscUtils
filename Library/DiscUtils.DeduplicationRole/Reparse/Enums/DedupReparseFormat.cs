namespace DiscUtils.DeduplicationRole.Reparse.Enums;

/// <summary>The layout of Data Deduplication reparse data (u16 at its start).</summary>
internal enum DedupReparseFormat : ushort
{
    /// <summary>Property blocks "FeRp"/"DdRp" (<see cref="DedupFileEntry" />); in the open-source readers, not seen.</summary>
    Properties = 0x0102,

    /// <summary>Property blocks "FeRp"/"DdRp"; seen on ReFS (Server 2025, <c>w2025-dedup</c>).</summary>
    Properties3 = 0x0103,

    /// <summary>Fixed fields (<see cref="DedupShortReparseData" />); NTFS samples of 2025, not seen on ReFS.</summary>
    Short = 0x0201
}
