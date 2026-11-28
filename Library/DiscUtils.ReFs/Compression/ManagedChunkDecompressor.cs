using DiscUtils.ReFs.Compression.Enums;
#if !NET46
using ZstdSharp;
#endif

namespace DiscUtils.ReFs.Compression;

/// <summary>
///     Managed decoder for ReFS volume compression: LZ4 (block format, implemented here) and Zstandard (ZstdSharp.Port; not available on net46).
/// </summary>
internal sealed class ManagedChunkDecompressor : IChunkDecompressor
{
    public static ManagedChunkDecompressor Instance { get; } = new ManagedChunkDecompressor();

    public bool Supports(CompressionFormat format)
    {
        return format switch
        {
            CompressionFormat.Lz4 => true,
#if !NET46
            CompressionFormat.Zstd => true,
#endif
            _ => false
        };
    }

    public int Decompress(CompressionFormat format, ReadOnlySpan<byte> compressed, Span<byte> destination)
    {
        try
        {
            switch (format)
            {
                case CompressionFormat.Lz4:
                    return Lz4Block(compressed, destination);
#if !NET46
                case CompressionFormat.Zstd:
                    using (var decompressor = new Decompressor())
                    {
                        return decompressor.Unwrap(compressed, destination);
                    }
#endif
                default:
                    throw new NotSupportedException($"Compression format {format} is not supported on this target framework.");
            }
        }
        catch (Exception ex) when (ex is not NotSupportedException and not ReFsFormatException)
        {
            throw new ReFsFormatException($"A {format} compressed chunk is corrupt or larger than expected: {ex.Message}", ex);
        }
    }

    /// <summary>Decodes one raw LZ4 block (no frame header) into <paramref name="destination" /> and returns the decoded length.</summary>
    internal static int Lz4Block(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        var s = 0;
        var d = 0;

        while (s < source.Length)
        {
            var token = source[s++];

            // Literal run.
            long literals = token >> 4;

            if (literals == 15)
            {
                int extra;

                do
                {
                    if (s >= source.Length)
                    {
                        throw new ReFsFormatException("Truncated LZ4 literal length.");
                    }

                    extra = source[s++];
                    literals += extra;
                } while (extra == 255 && literals <= destination.Length + 255L);
            }

            if (literals > source.Length - s || literals > destination.Length - d)
            {
                throw new ReFsFormatException("LZ4 literal run exceeds the input or the output.");
            }

            source.Slice(s, (int)literals).CopyTo(destination.Slice(d));
            s += (int)literals;
            d += (int)literals;

            // The last sequence has literals only.
            if (s == source.Length)
            {
                break;
            }

            if (s + 2 > source.Length)
            {
                throw new ReFsFormatException("Truncated LZ4 match offset.");
            }

            var offset = source[s] | (source[s + 1] << 8);
            s += 2;

            if (offset == 0 || offset > d)
            {
                throw new ReFsFormatException("Invalid LZ4 match offset.");
            }

            long matchLength = token & 0xF;

            if (matchLength == 15)
            {
                int extra;

                do
                {
                    if (s >= source.Length)
                    {
                        throw new ReFsFormatException("Truncated LZ4 match length.");
                    }

                    extra = source[s++];
                    matchLength += extra;
                } while (extra == 255 && matchLength <= destination.Length + 255L);
            }

            matchLength += 4;

            if (matchLength > destination.Length - d)
            {
                throw new ReFsFormatException("LZ4 match exceeds the output.");
            }

            // Matches may overlap their own output, so copy byte by byte.
            var from = d - offset;

            for (var i = 0; i < (int)matchLength; i++)
            {
                destination[d++] = destination[from++];
            }
        }

        return d;
    }
}