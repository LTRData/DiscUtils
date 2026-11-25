namespace DiscUtils.DeduplicationRole.Reparse.Enums;

/// <summary>The value type of one property of a Data Deduplication reparse property block (u16 in the block's property table).</summary>
internal enum DedupPropertyType : ushort
{
    /// <summary>An unused slot (size 0).</summary>
    None = 0,

    /// <summary>A u32 id.</summary>
    Id = 5,

    /// <summary>A u64 value.</summary>
    QWord = 6,

    /// <summary>A u32 bitmap type or flag.</summary>
    BitmapFlag = 9,

    /// <summary>A u32 revision or flags word.</summary>
    Version = 10,

    /// <summary>A FILETIME.</summary>
    FileTime = 11,

    /// <summary>A GUID.</summary>
    Guid = 12,

    /// <summary>Nested bytes: another property block, or a 64-byte range entry.</summary>
    Blob = 13,

    /// <summary>{u32 sequence number, u32 stream id}.</summary>
    IdRef = 14
}
