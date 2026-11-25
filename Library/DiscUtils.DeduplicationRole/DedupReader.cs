using DiscUtils.Streams;
using DiscUtils.DeduplicationRole.ChunkStore;
using DiscUtils.DeduplicationRole.Reparse;
using DiscUtils.DeduplicationRole.Streams;

namespace DiscUtils.DeduplicationRole;

/// <summary>
///     Reads files optimized by the Windows Data Deduplication role as Windows reads them through the dedup filter: the content comes from the volume's
///     chunk store, except for the clusters the file has written since its optimization. Independent of the file system: <see cref="IDedupSource" /> gives
///     the chunk store's files. Keeps each chunk store's container files open until disposed. Not thread-safe.
/// </summary>
/// <param name="source">The file system that holds the chunk store.</param>
/// <param name="verify">Whether to check each chunk's SHA-512 and each record's CRC-64.</param>
public sealed class DedupReader(IDedupSource source, bool verify = true) : IDisposable
{
    /// <summary>The reparse tag of a deduplicated file (IO_REPARSE_TAG_DEDUP).</summary>
    public const uint ReparseTag = DedupReparseData.Tag;

    private readonly Dictionary<Guid, DedupChunkStore> _stores = [];
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (DedupChunkStore store in _stores.Values)
            store.Dispose();

        _stores.Clear();
    }

    /// <summary>
    ///     Opens the content of a deduplicated file. <paramref name="stored" /> is the file's default stream as stored (its sparse stub). When it is a
    ///     <see cref="SparseStream" />, its <see cref="SparseStream.Extents" /> tell the ranges written since the optimization, which are read from the
    ///     stub; otherwise every range named by the reparse data is read from the chunk store. The returned stream owns <paramref name="stored" /> (also
    ///     when this throws) and is valid until this reader is disposed.
    /// </summary>
    /// <param name="reparseData">The data of the file's reparse point (tag <see cref="ReparseTag" />).</param>
    /// <param name="stored">The file's default stream as stored.</param>
    /// <exception cref="DedupFormatException">The reparse data, a stream map or a container is malformed.</exception>
    /// <exception cref="NotSupportedException">The reparse data uses a layout this reader does not handle (a recall bitmap, an unknown format).</exception>
    public Stream Open(ReadOnlyMemory<byte> reparseData, Stream stored)
    {
        if (stored is null)
        {
            throw new ArgumentNullException(nameof(stored));
        }

        try
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DedupReader));
            }

            DedupReparseData data = new DedupReparseData(reparseData);
            if (!_stores.TryGetValue(data.StoreId, out DedupChunkStore? store))
                _stores[data.StoreId] = store = new DedupChunkStore(data.StoreId, source.ListFiles, source.OpenFile, verify);

            List<DedupChunk> chunks = [.. data.Ranges.SelectMany(store.ReadStreamMap)];
            return new DedupFileStream(stored, store, chunks, stored.Length);
        }
        catch
        {
            stored.Dispose();
            throw;
        }
    }
}
