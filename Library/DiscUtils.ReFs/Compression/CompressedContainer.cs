using System.Buffers;
using System.Buffers.Binary;
using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Compression.Enums;
using DiscUtils.ReFs.IO;
using DiscUtils.ReFs.SystemTables;

namespace DiscUtils.ReFs.Compression;

/// <summary>
///     Reads the uncompressed contents of one volume-compressed container. The container's data is stored as a packed
///     stream of chunks (one real cluster run); compression headers give each chunk's compressed end offset. A chunk whose
///     stored size equals its uncompressed size is stored raw (it did not shrink, or it is the final partial chunk).
///     The container itself is immutable and safe to share between threads; the one decoded chunk that makes sequential reads cheap lives in a
///     <see cref="ChunkCache" /> owned by each reader, so readers neither lock nor evict each other's chunk.
/// </summary>
internal sealed class CompressedContainer
{
    private readonly ulong[] _bitmap; // one bit per container cluster: data present (LSB first)
    private readonly uint _chunkSize;
    private readonly uint _clusterSize;
    private readonly IChunkDecompressor _decompressor;
    private readonly long[] _firstChunk;
    private readonly CompressionFormat _format;
    private readonly CompressionHeader[] _headers;
    private readonly ContainerMap _map;
    private readonly long[] _rankBefore; // number of set bits before each 64-bit word
    private readonly long[] _runStart; // stream cluster index of each run
    private readonly long _streamLength;
    private readonly (ulong Start, uint Clusters)[] _streamRuns; // the stored stream, in order
    private readonly bool _verify;
    private readonly IBlockSource _volume;


    public CompressedContainer(
        IBlockSource volume,
        (ulong Start, uint Clusters)[] streamRuns,
        ContainerMap map,
        CompressionFormat format,
        uint chunkSize,
        IReadOnlyList<CompressionHeader> headers,
        IChunkDecompressor decompressor,
        uint clusterSize,
        byte[] clusterBitmap,
        bool verifyChecksums)
    {
        var packed = format == CompressionFormat.None;

        if (!packed && (chunkSize == 0 || chunkSize > int.MaxValue || !uint.IsPow2(chunkSize) || headers.Count == 0))
        {
            throw new ReFsFormatException("Invalid compressed container description.");
        }

        if (clusterSize == 0 || clusterBitmap.Length == 0 || clusterBitmap.Length % sizeof(ulong) != 0)
        {
            throw new ReFsFormatException("Invalid compressed container cluster bitmap.");
        }

        if (streamRuns.Length == 0)
        {
            throw new ReFsFormatException("Invalid compressed container stream runs.");
        }

        _clusterSize = clusterSize;
        _verify = verifyChecksums;
        _bitmap = new ulong[clusterBitmap.Length / sizeof(ulong)];

        for (var i = 0; i < _bitmap.Length; i++)
        {
            _bitmap[i] = BinaryPrimitives.ReadUInt64LittleEndian(clusterBitmap.AsSpan(i * sizeof(ulong)));
        }

        _rankBefore = new long[_bitmap.Length + 1];

        for (var i = 0; i < _bitmap.Length; i++)
        {
            _rankBefore[i + 1] = _rankBefore[i] + Hashing.PopCount(_bitmap[i]);
        }

        _volume = volume;
        _streamRuns = streamRuns;
        _runStart = new long[streamRuns.Length];
        long streamClusters = 0;

        for (var i = 0; i < streamRuns.Length; i++)
        {
            _runStart[i] = streamClusters;
            streamClusters += streamRuns[i].Clusters;
        }

        var streamLength = streamClusters * clusterSize;
        _map = map;
        _streamLength = streamLength;
        _format = format;
        _chunkSize = chunkSize;
        _decompressor = decompressor;
        _headers = [.. headers.OrderBy(h => h.Index)];

        _firstChunk = new long[_headers.Length];

        if (packed)
        {
            UncompressedLength = (ulong)streamLength;
            return;
        }

        // Headers must tile the container's uncompressed range in whole chunks.
        ulong expectedOffset = 0;
        long chunks = 0;

        for (var i = 0; i < _headers.Length; i++)
        {
            var header = _headers[i];

            if (header.UncompressedOffset != expectedOffset || header.UncompressedOffset % chunkSize != 0
                                                            || header.UncompressedBytes > (ulong)header.ChunkCount * chunkSize
                                                            || header.UncompressedBytes <= (ulong)(header.ChunkCount - 1) * chunkSize)
            {
                throw new ReFsFormatException($"Compression header {header.Index} does not match the chunk size {chunkSize}.");
            }

            _firstChunk[i] = chunks;
            chunks += header.ChunkCount;
            expectedOffset = header.UncompressedOffset + header.UncompressedBytes;
        }

        UncompressedLength = expectedOffset;
    }

    /// <summary>Number of valid uncompressed bytes; reads beyond are zero filled.</summary>
    public ulong UncompressedLength { get; }


    /// <summary>
    ///     Reads bytes at a byte offset within the container's address space (cluster index times cluster size). Clusters
    ///     without data read as zeros; clusters with data are found in the packed stream through the cluster bitmap.
    /// </summary>
    public void Read(long offset, Span<byte> destination, ChunkCache cache)
    {
        while (!destination.IsEmpty)
        {
            var cluster = offset / _clusterSize;
            var inCluster = (int)(offset % _clusterSize);

            if (!IsSet(cluster))
            {
                var hole = (int)Math.Min(destination.Length, _clusterSize - inCluster);
                destination[..hole].Clear();
                destination = destination[hole..];
                offset += hole;
                continue;
            }

            // Consecutive clusters with data are consecutive in the packed stream.
            var wanted = (inCluster + destination.Length + _clusterSize - 1) / _clusterSize;
            long run = 1;

            while (run < wanted && IsSet(cluster + run))
            {
                run++;
            }

            var count = (int)Math.Min(destination.Length, (run * _clusterSize) - inCluster);
            ReadPacked((Rank(cluster) * _clusterSize) + inCluster, destination[..count], cache);
            destination = destination[count..];
            offset += count;
        }
    }

    private bool IsSet(long cluster)
    {
        var word = cluster >> 6;
        return word < _bitmap.Length && (_bitmap[word] & (1UL << (int)(cluster & 63))) != 0;
    }

    /// <summary>Number of clusters with data before <paramref name="cluster" /> (its index in the packed stream).</summary>
    private long Rank(long cluster)
    {
        var word = cluster >> 6;
        return _rankBefore[word] + Hashing.PopCount(_bitmap[word] & ((1UL << (int)(cluster & 63)) - 1));
    }

    /// <summary>Reads uncompressed bytes of the packed stream; bytes past its end read as zero.</summary>
    private void ReadPacked(long offset, Span<byte> destination, ChunkCache cache)
    {
        while (!destination.IsEmpty)
        {
            if ((ulong)offset >= UncompressedLength)
            {
                destination.Clear();
                return;
            }

            if (_format == CompressionFormat.None)
            {
                // Packed but not compressed: the stream holds the data clusters as they are.
                var raw = (int)Math.Min((ulong)destination.Length, UncompressedLength - (ulong)offset);
                ReadStream(offset, destination[..raw]);
                destination = destination[raw..];
                offset += raw;
                continue;
            }

            var chunk = offset / _chunkSize;
            LoadChunk(chunk, cache);

            var inChunk = (int)(offset - (chunk * _chunkSize));
            var count = Math.Min(destination.Length, cache.Length - inChunk);
            cache.Buffer.AsSpan(inChunk, count).CopyTo(destination);
            destination = destination[count..];
            offset += count;
        }
    }

    /// <summary>
    ///     Reads bytes of the packed stream. The stream's start is a container-space cluster address; the stream may
    ///     continue into following containers, so each cluster run is translated through the container map.
    /// </summary>
    private void ReadStream(long offset, Span<byte> destination)
    {
        var done = 0;

        while (done < destination.Length)
        {
            var position = offset + done;
            var streamCluster = position / _clusterSize;
            var runIndex = Array.BinarySearch(_runStart, streamCluster);

            if (runIndex < 0)
            {
                runIndex = ~runIndex - 1;
            }

            var inRun = streamCluster - _runStart[runIndex];
            var lcn = _streamRuns[runIndex].Start + (ulong)inRun;
            var inCluster = (int)(position % _clusterSize);
            var runLeft = _streamRuns[runIndex].Clusters - inRun;
            var available = (Math.Min(runLeft, (long)_map.ContiguousClusters(lcn)) * _clusterSize) - inCluster;
            var count = (int)Math.Min(destination.Length - done, available);

            _volume.ReadExactly(BlockSourceExtensions.ClusterOffset(_map.Translate(lcn), _clusterSize) + inCluster, destination.Slice(done, count));
            done += count;
        }
    }

    private ulong StreamLcn(long streamOffset)
    {
        var streamCluster = streamOffset / _clusterSize;
        var runIndex = Array.BinarySearch(_runStart, streamCluster);

        if (runIndex < 0)
        {
            runIndex = ~runIndex - 1;
        }

        return _streamRuns[runIndex].Start + (ulong)(streamCluster - _runStart[runIndex]);
    }

    /// <summary>
    ///     The per-chunk digest (when the header has them) is the checksum of the chunk as stored, compressed or raw. Returns true when the chunk
    ///     was checked (false when the header has no digests or verification is off).
    /// </summary>
    private bool VerifyStored(CompressionHeader header, int local, long start, ReadOnlySpan<byte> stored)
    {
        if (!_verify || header.ChecksumKind == ChecksumKind.None)
        {
            return false;
        }

        var digestLength = PageChecksum.DigestLength(header.ChecksumKind);

        if (!PageChecksum.Verify(header.ChecksumKind, stored, header.Digests.AsSpan(local * digestLength, digestLength)))
        {
            throw new ReFsCorruptionException(StreamLcn(start), header.ChecksumKind, "compressed chunk");
        }

        return true;
    }

    private void LoadChunk(long chunk, ChunkCache cache)
    {
        if (!ReferenceEquals(cache.Owner, this))
        {
            cache.Owner = this;
            cache.Index = -1;

            if (cache.Buffer is not null && cache.Buffer.Length < _chunkSize)
            {
                ArrayPool<byte>.Shared.Return(cache.Buffer);
                cache.Buffer = null;
            }
        }

        if (cache.Index == chunk)
        {
            return;
        }

        var headerIndex = Array.BinarySearch(_firstChunk, chunk);

        if (headerIndex < 0)
        {
            headerIndex = ~headerIndex - 1;
        }

        if (headerIndex < 0)
        {
            throw new ReFsFormatException("Chunk index outside the compression headers.");
        }

        var header = _headers[headerIndex];
        var local = (int)(chunk - _firstChunk[headerIndex]);

        if (local >= header.ChunkCount)
        {
            throw new ReFsFormatException("Chunk index outside the compression headers.");
        }

        var start = local == 0 ? (long)header.CompressedOffset : header.Ends[local - 1];
        long end = header.Ends[local];
        var compressedLength = end - start;

        var chunkStart = chunk * _chunkSize;
        var uncompressedLength = (int)Math.Min(_chunkSize, (long)(header.UncompressedOffset + header.UncompressedBytes) - chunkStart);

        if (start < 0 || compressedLength <= 0 || compressedLength > uncompressedLength || end > _streamLength)
        {
            throw new ReFsFormatException($"Compressed chunk {chunk} has an invalid stored size ({compressedLength}).");
        }

        cache.Buffer ??= ArrayPool<byte>.Shared.Rent((int)_chunkSize);
        cache.Index = -1;

        if (compressedLength == uncompressedLength)
        {
            ReadStream(start, cache.Buffer.AsSpan(0, uncompressedLength)); // stored raw

            if (VerifyStored(header, local, start, cache.Buffer.AsSpan(0, uncompressedLength)))
            {
                cache.VerifiedBytes += uncompressedLength;
                cache.VerifiedKind = header.ChecksumKind;
            }
        }
        else
        {
            var scratch = ArrayPool<byte>.Shared.Rent((int)compressedLength);

            try
            {
                ReadStream(start, scratch.AsSpan(0, (int)compressedLength));

                if (VerifyStored(header, local, start, scratch.AsSpan(0, (int)compressedLength)))
                {
                    cache.VerifiedBytes += uncompressedLength;
                    cache.VerifiedKind = header.ChecksumKind;
                }

                var written = _decompressor.Decompress(_format, scratch.AsSpan(0, (int)compressedLength), cache.Buffer.AsSpan(0, uncompressedLength));

                if (written != uncompressedLength)
                {
                    throw new ReFsFormatException($"Compressed chunk {chunk} decompressed to {written} bytes, expected {uncompressedLength}.");
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(scratch);
            }
        }

        cache.Length = uncompressedLength;
        cache.Index = chunk;
    }
}