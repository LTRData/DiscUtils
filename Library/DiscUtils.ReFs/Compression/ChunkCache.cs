using System.Buffers;
using DiscUtils.ReFs.Checksums;

namespace DiscUtils.ReFs.Compression;

/// <summary>
///     One decoded chunk of a <see cref="CompressedContainer" />, owned by a single reader (not thread safe): sequential reads in the same
///     chunk then cost one decode, and readers sharing a container do not evict each other's chunk. Disposing returns the buffer to the pool.
/// </summary>
internal sealed class ChunkCache : IDisposable
{
    internal byte[]? Buffer { get; set; }

    /// <summary>Index of the chunk in <see cref="Buffer" />, or -1 when none (or a decode is in progress).</summary>
    internal long Index { get; set; } = -1;

    /// <summary>The container the chunk belongs to: one reader can move between containers, whose chunk numbers and sizes differ.</summary>
    internal CompressedContainer? Owner { get; set; }

    /// <summary>Valid bytes in <see cref="Buffer" />.</summary>
    internal int Length { get; set; }

    /// <summary>Uncompressed bytes of the chunks decoded through this cache that were checked against a per-chunk digest.</summary>
    internal long VerifiedBytes { get; set; }

    /// <summary>The digest kind of the last chunk counted in <see cref="VerifiedBytes" /> (None before any).</summary>
    internal ChecksumKind VerifiedKind { get; set; }

    public void Dispose()
    {
        if (Buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(Buffer);
            Buffer = null;
        }

        Index = -1;
        Owner = null;
    }
}