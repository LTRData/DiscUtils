using DiscUtils.ReFs.Checksums;
using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Compression;
using DiscUtils.ReFs.Entries.Files;
using DiscUtils.ReFs.IO;
using DiscUtils.ReFs.SystemTables;
using DiscUtils.Streams;

namespace DiscUtils.ReFs.Entries.Streams;

/// <summary>
///     Read-only, seekable stream over a run list with sparse holes (holes read as zeros). Runs of integrity streams are
///     verified block by block (CRC32C, CRC-64 or SHA256) unless disabled. Not thread-safe.
/// </summary>
internal sealed class RunFileStream : FileStreamBase
{
    // One verified block of an integrity run is cached so sequential reads do not re-read and re-hash it.
    // The device is asked for up to ReadAheadBytes of the run at once (a 4 KiB request per checksum block is far too slow on spinning
    // disks and raw devices); every block of that chunk is verified the first time it is handed out.
    private const long ReadAheadBytes = 1 << 20;
    private readonly uint _clusterSize;
    private readonly ContainerMap _containers;
    private readonly ILogger _logger;
    private readonly string _name;
    private readonly StreamChecksumPolicy? _policy;
    private readonly Run[] _runs;
    private readonly bool _verifyData;
    private readonly IBlockSource _volume;
    private byte[]? _blockBuffer;
    private bool[]? _blockVerified;
    private long _cachedChunk = -1; // first block of the buffered chunk
    private int _cachedChunkBlocks;
    private int _cachedRun = -1;

    private ChunkCache? _chunks;

    private long _verifiedBytes;

    public RunFileStream(
        IBlockSource volume,
        ContainerMap containers,
        uint clusterSize,
        IEnumerable<Run> runs,
        ulong length,
        StreamChecksumPolicy? policy,
        bool verifyData,
        ILogger logger,
        string name)
        : base(length <= long.MaxValue ? (long)length : throw new ReFsFormatException($"The stream of {name} is {length} bytes long, more than a .NET stream can address."))
    {
        _logger = logger;
        _name = name;
        _volume = volume;
        _containers = containers;
        _clusterSize = clusterSize;
        _runs = [.. runs.OrderBy(r => r.VirtualCluster)];
        _policy = policy;
        _verifyData = verifyData;

        if (clusterSize == 0)
        {
            throw new ReFsFormatException("A stream has a zero cluster size.");
        }

        ulong end = 0;

        foreach (var run in _runs)
        {
            if (run.Count == 0 || run.EndVirtualCluster < run.VirtualCluster || run.VirtualCluster < end
                || run.EndVirtualCluster > (ulong)long.MaxValue / clusterSize)
            {
                throw new ReFsFormatException("A stream has empty, overlapping, overflowing or unaddressable run ranges.");
            }

            if (run.IntegrityRange is { } original && (original.SkippedClusters > original.Count
                                                       || run.Count > original.Count - original.SkippedClusters || original.Lcn > ulong.MaxValue - original.SkippedClusters
                                                       || original.Lcn + original.SkippedClusters != run.Lcn))
            {
                throw new ReFsFormatException("A stream slice is outside its original integrity allocation.");
            }

            end = run.EndVirtualCluster;
        }
    }

    /// <summary>The stream's runs in virtual-cluster order; for tests and diagnostics.</summary>
    internal IReadOnlyList<Run> Runs => _runs;

    /// <summary>The decoded-chunk cache for reads inside compressed containers (a stream is not thread safe, so it owns one).</summary>
    private ChunkCache Chunks => _chunks ??= new ChunkCache();

    /// <summary>
    ///     Bytes read so far that were checked against a checksum: integrity-stream blocks and compressed chunks with digests. A block read
    ///     again after it left the read-ahead buffer counts again, and a last block counts in full, so this can exceed the length.
    /// </summary>
    internal long VerifiedBytes => _verifiedBytes + (_chunks?.VerifiedBytes ?? 0);

    /// <summary>The checksum kinds of the bytes counted in <see cref="VerifiedBytes" />: the integrity blocks' and the compressed chunks'.</summary>
    internal IEnumerable<ChecksumKind> VerifiedKinds => new[] { IntegrityKind, _chunks?.VerifiedKind ?? ChecksumKind.None }.Where(k => k != ChecksumKind.None).Distinct();

    private ChecksumKind IntegrityKind { get; set; }

    /// <summary>
    ///     Bytes read so far that were never written: holes between runs and runs without the Valid flag (allocated, not written). They read as
    ///     zeros and have no checksums, because there is no data to check.
    /// </summary>
    internal long UnwrittenBytes { get; private set; }

    /// <summary>
    ///     The byte ranges that lie in runs, within the stream's length; holes between runs read as zeros and are left out. A file optimized by Data
    ///     Deduplication keeps the ranges written since its optimization here.
    /// </summary>
    public override IEnumerable<StreamExtent> Extents
    {
        get
        {
            var length = Length;

            foreach (var run in _runs)
            {
                var start = (long)Math.Min(run.VirtualCluster * _clusterSize, (ulong)length);
                var end = (long)Math.Min(run.EndVirtualCluster * _clusterSize, (ulong)length);

                if (end > start)
                {
                    yield return new StreamExtent(start, end - start);
                }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        _chunks?.Dispose();
        _blockBuffer = null;
        _blockVerified = null;
        base.Dispose(disposing);
    }

    protected override int ReadAt(long position, Span<byte> destination)
    {
        var read = ReadChunk(position, destination);

        // Guard kept: the run lookup (a binary search) in the arguments runs before the generated method can check the level.
        if (_logger.IsEnabled(LogLevel.Trace))
        {
            Logs.FileRead(_logger, position, read, FindRunAtOrBefore((ulong)(position / _clusterSize)), _verifyData ? " (checksums verified where the run has them)" : string.Empty);
        }

        return read;
    }

    /// <summary>Reads at least one byte (at most <paramref name="destination" />.Length) at <paramref name="position" />.</summary>
    private int ReadChunk(long position, Span<byte> destination)
    {
        var cluster = (ulong)(position / _clusterSize);
        var offsetInCluster = (int)(position % _clusterSize);
        var index = FindRunAtOrBefore(cluster);

        if (index >= 0 && cluster < _runs[index].EndVirtualCluster)
        {
            return ReadFromRun(index, cluster, offsetInCluster, destination);
        }

        // Hole: zeros until the next run starts.
        var next = index + 1;
        var holeEnd = next < _runs.Length
            ? (long)Math.Min(_runs[next].VirtualCluster * _clusterSize, long.MaxValue)
            : long.MaxValue;

        var count = (int)Math.Min(destination.Length, holeEnd - position);
        destination[..count].Clear();
        UnwrittenBytes += count;
        return count;
    }

    private int ReadFromRun(int runIndex, ulong cluster, int offsetInCluster, Span<byte> destination)
    {
        var run = _runs[runIndex];

        if (!run.IsSupported)
        {
            throw new NotSupportedException($"Run at virtual cluster {run.VirtualCluster} has unsupported flags {run.Flags} (0x{(ushort)run.Flags:X}).");
        }

        if (!run.IsWritten)
        {
            // Allocated but never written: the clusters still hold whatever was there before (another file's bytes), so they read as zeros, like a hole.
            var runEnd = (long)run.EndVirtualCluster * _clusterSize;
            var unwritten = (int)Math.Min(destination.Length, runEnd - (((long)cluster * _clusterSize) + offsetInCluster));
            destination[..unwritten].Clear();
            UnwrittenBytes += unwritten;
            return unwritten;
        }

        if (_verifyData && (run.HasChecksums || run.Checksums is not null))
        {
            var checksums = run.Checksums
                            ?? throw new ReFsFormatException($"Run at virtual cluster {run.VirtualCluster} of '{_name}' requires checksums, but its digests are unavailable.");

            var offsetInRun = ((long)(cluster - run.VirtualCluster) * _clusterSize) + offsetInCluster;

            if (run.IntegrityRange is { } original)
            {
                var visibleBytes = ((long)run.Count * _clusterSize) - offsetInRun;
                destination = destination[..(int)Math.Min(destination.Length, visibleBytes)];
                offsetInRun += (long)original.SkippedClusters * _clusterSize;
                run = new Run(0, original.Count, original.Lcn, run.Flags, checksums);
            }

            return ReadVerified(runIndex, run, checksums, offsetInRun, destination);
        }

        var clusterInRun = cluster - run.VirtualCluster;
        var lcn = run.Lcn + clusterInRun;
        var clusters = Math.Min(run.Count - clusterInRun, _containers.ContiguousClusters(lcn));
        var available = ((long)clusters * _clusterSize) - offsetInCluster;
        var count = (int)Math.Min(destination.Length, available);

        ReadContainerExtent(lcn, offsetInCluster, destination[..count]);
        return count;
    }

    /// <summary>
    ///     Reads from an integrity run: loads and verifies the whole checksum block containing the position
    ///     (one digest per complete block; the checksum kind and block size come from the volume rule).
    /// </summary>
    private int ReadVerified(int runIndex, Run run, byte[] checksums, long offsetInRun, Span<byte> destination)
    {
        var policy = _policy
                     ?? throw new NotSupportedException($"Run at virtual cluster {run.VirtualCluster} has checksums, but the volume's checksum configuration is not supported.");

        var digestLength = policy.DigestLength;
        long blockSize = policy.BlockSize;

        // The run needs one digest per block, from the start of its checksum bytes. Fewer (or an unexplained layout) fails closed. MORE is accepted,
        // as Windows does: it indexes the array from its start by the block number within the run and requires at least 24 +
        // the digest bytes (no equality check). Rows with a longer array exist on a real Dev Drive (a 1-cluster run with 8 checksum bytes whose
        // second digest is the next run's digest; an 11-cluster run with 52 instead of 44): Windows' own trim/split/merge operations rewrite the
        // size exactly, so where such tails come from is not established. For block-aligned allocations only the complete-block count * digest bytes
        // belong to the run.
        var runBytes = (long)run.Count * _clusterSize;

        if (digestLength == 0 || runBytes % blockSize != 0 || checksums.Length < runBytes / blockSize * digestLength)
        {
            throw new NotSupportedException($"Run at virtual cluster {run.VirtualCluster}: {checksums.Length} checksum bytes do not match {runBytes} bytes in {blockSize}-byte blocks.");
        }

        var block = offsetInRun / blockSize;
        var totalBlocks = runBytes / blockSize;
        var chunkBlocks = (int)Math.Max(1, Math.Min(ReadAheadBytes / blockSize, totalBlocks - block));
        var buffered = _cachedRun == runIndex && block >= _cachedChunk && block < _cachedChunk + _cachedChunkBlocks;

        if (!buffered)
        {
            if (_blockBuffer is null || _blockBuffer.Length != Math.Max(blockSize, ReadAheadBytes))
            {
                _blockBuffer = new byte[Math.Max(blockSize, ReadAheadBytes)];
                _blockVerified = new bool[_blockBuffer.Length / blockSize];
            }

            _cachedRun = -1;
            ReadRunBytes(run, block * blockSize, _blockBuffer.AsSpan(0, (int)(chunkBlocks * blockSize)));
            Array.Clear(_blockVerified!, 0, _blockVerified!.Length);
            _cachedRun = runIndex;
            _cachedChunk = block;
            _cachedChunkBlocks = chunkBlocks;
        }

        var slot = (int)(block - _cachedChunk);
        ReadOnlySpan<byte> blockBytes = _blockBuffer.AsSpan((int)(slot * blockSize), (int)blockSize);

        if (!_blockVerified![slot])
        {
            ReadOnlySpan<byte> digest = checksums.AsSpan((int)(block * digestLength), digestLength);

            // A match-all placeholder digest (written by Windows over a run it has not hashed yet). Windows accepts it on file data without
            // comparing. The block is returned the same way, but it is not counted as verified: it is unchecked data.
            if (PageChecksum.IsDataPlaceholder(policy.Kind, digest))
            {
                Logs.DataChecksumPlaceholder(_logger, run.Lcn + (ulong)(block * blockSize / _clusterSize), _name, policy.Kind.ToString());
                return CopyFromBlock(blockBytes, offsetInRun - (block * blockSize), blockSize, destination);
            }

            if (!PageChecksum.VerifyData(policy.Kind, blockBytes, digest))
            {
                Logs.DataChecksumFailed(_logger, run.Lcn + (ulong)(block * blockSize / _clusterSize), _name, policy.Kind.ToString());
                throw new ReFsCorruptionException(run.Lcn + (ulong)(block * blockSize / _clusterSize), policy.Kind, "file data");
            }

            _blockVerified[slot] = true;
            _verifiedBytes += blockSize;
            IntegrityKind = policy.Kind;
        }

        return CopyFromBlock(blockBytes, offsetInRun - (block * blockSize), blockSize, destination);
    }

    private static int CopyFromBlock(ReadOnlySpan<byte> blockBytes, long inBlock, long blockSize, Span<byte> destination)
    {
        var count = (int)Math.Min(destination.Length, blockSize - inBlock);
        blockBytes.Slice((int)inBlock, count).CopyTo(destination);
        return count;
    }

    /// <summary>Reads <paramref name="destination" />.Length bytes of a run starting at a byte offset within it.</summary>
    private void ReadRunBytes(Run run, long offsetInRun, Span<byte> destination)
    {
        var done = 0;

        while (done < destination.Length)
        {
            var position = offsetInRun + done;
            var lcn = run.Lcn + (ulong)(position / _clusterSize);
            var inCluster = (int)(position % _clusterSize);
            var available = ((long)_containers.ContiguousClusters(lcn) * _clusterSize) - inCluster;
            var count = (int)Math.Min(destination.Length - done, available);

            ReadContainerExtent(lcn, inCluster, destination.Slice(done, count));
            done += count;
        }
    }

    private void ReadContainerExtent(ulong lcn, int inCluster, Span<byte> destination)
    {
        if (_containers.TryGetCompressed(lcn, out var compressed))
        {
            compressed.Read(((long)_containers.OffsetInContainer(lcn) * _clusterSize) + inCluster, destination, Chunks);
        }
        else
        {
            _volume.ReadExactly(BlockSourceExtensions.ClusterOffset(_containers.Translate(lcn), _clusterSize) + inCluster, destination);
        }
    }

    /// <summary>Index of the last run starting at or before <paramref name="cluster" />, or -1.</summary>
    private int FindRunAtOrBefore(ulong cluster)
    {
        var low = 0;
        var high = _runs.Length - 1;
        var found = -1;

        while (low <= high)
        {
            var mid = low + ((high - low) / 2);

            if (_runs[mid].VirtualCluster <= cluster)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }
}