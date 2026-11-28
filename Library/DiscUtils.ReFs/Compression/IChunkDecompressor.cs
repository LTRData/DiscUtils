using DiscUtils.ReFs.Compression.Enums;

namespace DiscUtils.ReFs.Compression;

/// <summary>
///     Decompresses single chunks of volume-compressed data. The reader hands over one compressed chunk and a destination
///     span of exactly the chunk's uncompressed length; implementations must not allocate on this path.
///     Implementations must be thread-safe: one instance is shared by every stream of a volume.
/// </summary>
/// <remarks>
///     Supply your own implementation through the open options to replace the default decoder.
/// </remarks>
internal interface IChunkDecompressor
{
    /// <summary>True when <paramref name="format" /> can be decoded by this implementation.</summary>
    bool Supports(CompressionFormat format);

    /// <summary>
    ///     Decompresses one chunk. <paramref name="destination" /> is sized to the chunk's uncompressed length.
    /// </summary>
    /// <returns>The number of bytes written to <paramref name="destination" />.</returns>
    /// <exception cref="NotSupportedException">The format is not supported.</exception>
    /// <exception cref="ReFsFormatException">The compressed data is corrupt or does not fit <paramref name="destination" />.</exception>
    int Decompress(CompressionFormat format, ReadOnlySpan<byte> compressed, Span<byte> destination);
}