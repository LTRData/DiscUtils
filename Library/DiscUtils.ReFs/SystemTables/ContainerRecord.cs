using DiscUtils.ReFs.Compat;
using DiscUtils.ReFs.Compression;
using DiscUtils.ReFs.Compression.Enums;
using DiscUtils.ReFs.SystemTables.Enums;

namespace DiscUtils.ReFs.SystemTables;

/// <summary>
/// One row of the container table. Key u64 container id at 0. Value: 0x14 u32 flags (<see cref="ContainerFlags" />);
/// 0x18 u32 size (read here) and, apparently only in memory, bit 0x01 of the byte at 0x18 and bit 0x10 of the byte at
/// 0x19; 0x20 u32 used clusters (not read); 0x28 u32 a second count (not read); 0x30 u16 compression format and 0x34 u32 chunk size (read here;
/// inferred); 0x4C u32 run count; 0x50 the runs, 16 bytes each ({u64 start, u64 count}; this reader takes them from the end of the
/// value).
/// </summary>
internal readonly record struct ContainerRecord
{
    private const int RunsOffset = 0x50;
    private const int RunLength = 16;
    private const int MaxRuns = 4096;

    /// <summary>Parses a row and checks it on its own (the size against the other rows is the table's check).</summary>
    public ContainerRecord(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, uint clusterSize)
    {
        if (key.Length < sizeof(ulong) || value.Length < RunsOffset + RunLength)
        {
            throw new ReFsFormatException("Malformed container table row.");
        }

        Id = key.U64(0);
        Flags = (ContainerFlags)value.U32(0x14);
        Size = value.U32(0x18);
        Format = (CompressionFormat)value.U16(0x30);
        ChunkSize = value.U32(0x34);
        uint runCount = value.U32(0x4C);
        if (!uint.IsPow2(Size) || runCount == 0 || runCount > MaxRuns || value.Length < RunsOffset + RunLength * (int)runCount)
        {
            throw new NotSupportedException($"Container {Id}: unsupported layout (size {Size}, runs {runCount}).");
        }

        // Runs {u64 start, u64 count} are the last bytes of the value. A compressed or packed container's runs, in
        // order, make up its stored stream; any other container's runs, in order, cover the whole container (usually one run;
        // Windows walks several).
        ReadOnlySpan<byte> runs = value[^(RunLength * (int)runCount)..];
        (ulong Start, uint Clusters)[] streamRuns = new (ulong, uint)[runCount];
        ulong totalClusters = 0;
        for (int i = 0; i < streamRuns.Length; i++)
        {
            ulong start = runs.U64(i * RunLength);
            ulong count = runs.U64(i * RunLength + 8);
            if (count == 0 || count > uint.MaxValue || totalClusters + count > uint.MaxValue
                || start > (ulong)long.MaxValue / clusterSize || count > (ulong)long.MaxValue / clusterSize - start)
            {
                throw new ReFsFormatException($"Container {Id} has an empty, overflowing or unaddressable physical run.");
            }

            streamRuns[i] = (start, (uint)count);
            totalClusters += count;
        }

        Start = streamRuns[0].Start;
        RunClusters = (uint)totalClusters;
        Runs = streamRuns;
        StreamRuns = IsCompressed ? streamRuns : null;

        if (IsCompressed)
        {
            if (Flags.HasFlag(ContainerFlags.Compressed) && (Format == CompressionFormat.None || ChunkSize == 0))
            {
                throw new NotSupportedException($"Container {Id}: compressed without a format or chunk size.");
            }
        }
        else if (totalClusters != Size)
        {
            throw new NotSupportedException($"Container {Id} is not fully mapped.");
        }
    }

    public ulong Id { get; }

    public ContainerFlags Flags { get; }

    /// <summary>Clusters in the container (a power of two, the same for every container).</summary>
    public uint Size { get; }

    /// <summary>First real cluster of the (first) run.</summary>
    public ulong Start { get; }

    /// <summary>Clusters in all runs together.</summary>
    public uint RunClusters { get; }

    public CompressionFormat Format { get; }

    public uint ChunkSize { get; }

    /// <summary>The runs of a compressed or packed container's stored stream, in order; null for any other container.</summary>
    public (ulong Start, uint Clusters)[]? StreamRuns { get; }

    /// <summary>All runs, in order: for an ordinary container they map its clusters one after another.</summary>
    public (ulong Start, uint Clusters)[] Runs { get; }

    /// <summary>The real cluster of cluster <paramref name="offset" /> of an ordinary container: found in its runs in order.</summary>
    public ulong Locate(ulong offset)
    {
        if (Runs.Length == 1)
        {
            return Start + offset;
        }

        foreach ((ulong start, uint clusters) in Runs)
        {
            if (offset < clusters)
            {
                return start + offset;
            }

            offset -= clusters;
        }

        throw new ReFsFormatException($"Cluster offset beyond the runs of container {Id}.");
    }

    /// <summary>The container's data is stored as a compressed chunk stream (the run is the stream, not the data).</summary>
    public bool IsCompressed => Flags.HasFlag(ContainerFlags.Compressed) || Flags.HasFlag(ContainerFlags.Packed);
}
