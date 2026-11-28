namespace DiscUtils.ReFs.Compression.Enums;

/// <summary>Compression format ids used by ReFS volume compression.</summary>
internal enum CompressionFormat : ushort
{
    None = 0,

    /// <summary>LZ4 block format (one raw LZ4 block per chunk).</summary>
    Lz4 = 1,

    /// <summary>Zstandard (one complete frame per chunk).</summary>
    Zstd = 2,

    /// <summary>LZ4 produced by an Intel QAT accelerator; the stream layout is not understood yet.</summary>
    Lz4Qat = 3
}