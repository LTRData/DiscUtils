namespace DiscUtils.DeduplicationRole.ChunkStore.Enums;

/// <summary>The two sets of container files in a chunk store.</summary>
internal enum DedupContainerKind
{
    /// <summary>Stream maps, in <c>Stream\</c>.</summary>
    Stream,

    /// <summary>Data chunks, in <c>Data\</c> (and the copies of much-shared chunks in <c>Hotspot\</c>).</summary>
    Data
}
