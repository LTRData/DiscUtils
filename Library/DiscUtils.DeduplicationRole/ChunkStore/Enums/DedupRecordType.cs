namespace DiscUtils.DeduplicationRole.ChunkStore.Enums;

/// <summary>The kind of a "Ckhr" record in a chunk store container (u16 at +0x10 of its header).</summary>
internal enum DedupRecordType : ushort
{
    /// <summary>A chunk of file data (in the Data and Hotspot containers).</summary>
    DataChunk = 1,

    /// <summary>A stream map: the chunks of one file range (in the Stream containers).</summary>
    StreamMap = 2,

    /// <summary>Filler up to a 512-byte boundary (0xFF bytes).</summary>
    Padding = 4
}
