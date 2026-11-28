using System.Security.Cryptography;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.DeduplicationRole.ChunkStore.Enums;
using DiscUtils.DeduplicationRole.Compression;
using DiscUtils.DeduplicationRole.Reparse;

namespace DiscUtils.DeduplicationRole.ChunkStore;

/// <summary>
///     The Data Deduplication chunk store of a volume: <c>System Volume Information\Dedup\ChunkStore\{GUID}.ddp</c>, with the stream maps in
///     <c>Stream\</c> and the chunks in <c>Data\</c> (and <c>Hotspot\</c>), each a set of container files <c>{id:x8}.{generation:x8}.ccc</c>.
///     Container layout (undocumented; from ntfs-3g-dedup-plugin and DedupInspector, checked against the lab image <c>w2025-dedup</c>):
///     <list type="bullet">
///         <item>Header "Cthr" at 0 and 0x1000; a redirection table "Rrtl" at 0x3000 (backup 0x4000): u32 count at +8, {u32 record id, u32 offset} items at +0x20.</item>
///         <item>Records "Ckhr" from 0x5000 (<see cref="DedupRecordHeader" />). CRC-64: the NVMe polynomial, reflected, initial value 0, result inverted.</item>
///         <item>A stream map's payload is "Smap" + 4 version bytes, then 64-byte entries (<see cref="DedupChunk" />).</item>
///     </list>
///     Not thread-safe; container files stay open until the store is disposed.
/// </summary>
internal sealed class DedupChunkStore(Guid id, IDedupSource source, bool verify) : IDisposable
{
    private const int FirstRecordOffset = 0x5000;

    private readonly Dictionary<DedupContainerName, Stream> _containers = [];
    private readonly Dictionary<string, Stream> _openFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Stream, DedupRedirectionTable?> _redirectionTables = [];
    private readonly Dictionary<string, IReadOnlyList<string>> _listings = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _root = $@"System Volume Information\Dedup\ChunkStore\{{{id.ToString().ToUpperInvariant()}}}.ddp";

    public void Dispose()
    {
        foreach (Stream stream in _openFiles.Values)
            stream.Dispose();

        _openFiles.Clear();
        _containers.Clear();
        _redirectionTables.Clear();
    }

    /// <summary>The chunks of one file range, in file order.</summary>
    public IReadOnlyList<DedupChunk> ReadStreamMap(DedupStreamReference range)
    {
        DedupContainerName container = new DedupContainerName(DedupContainerKind.Stream, range.ContainerId, range.Generation);
        DedupRecord record = ReadRecord(container, range.StreamMapId, range.Offset, DedupRecordType.StreamMap);
        return new DedupStreamMap(record.Payload, range).Chunks;
    }

    /// <summary>A chunk's data, decompressed and (when verifying) checked against its hash.</summary>
    public byte[] ReadChunk(DedupChunk chunk)
    {
        DedupContainerName container = new DedupContainerName(DedupContainerKind.Data, chunk.ContainerId, chunk.Generation);
        DedupRecord record = ReadRecord(container, chunk.Id, chunk.Offset, DedupRecordType.DataChunk);
        byte[] data = Decompress(chunk, record.Header.Compression, record.Payload);

        // The stream map entry and the chunk's own header both hold the first 32 bytes of the SHA-512 of the data.
        if (verify)
        {
#if NET5_0_OR_GREATER
            ReadOnlySpan<byte> hash = SHA512.HashData(data).AsSpan(0, 32);
#else
            using var sha = SHA512.Create();
            ReadOnlySpan<byte> hash = sha.ComputeHash(data).AsSpan(0, 32);
#endif
            if (!hash.SequenceEqual(chunk.Hash) || !hash.SequenceEqual(record.Header.ChunkHash))
                throw new DedupFormatException($"Data Deduplication chunk {chunk.Id} (container {chunk.ContainerId}) does not match its hash.");
        }

        return data;
    }

    /// <summary>The chunk's data from its payload. Only uncompressed and XPRESS chunks are on the lab image; LZNT1 and XPRESS Huffman are checked against Windows' own compressor.</summary>
    private static byte[] Decompress(DedupChunk chunk, DedupCompressionFormat format, byte[] payload)
    {
        if (format == DedupCompressionFormat.None)
            return payload.Length == chunk.Length
                ? payload
                : throw new DedupFormatException($"Uncompressed Data Deduplication chunk {chunk.Id} holds {payload.Length} bytes for {chunk.Length}.");

        byte[] data = new byte[chunk.Length];
        switch (format)
        {
            case DedupCompressionFormat.Lznt1:
                DedupDecompressors.Lznt1(payload, data);
                break;
            case DedupCompressionFormat.Xpress:
                DedupDecompressors.Xpress(payload, data);
                break;
            case DedupCompressionFormat.XpressHuffman:
                DedupDecompressors.XpressHuffman(payload, data);
                break;
            default:
                throw new NotSupportedException($"Data Deduplication chunk {chunk.Id} uses unknown compression format {(uint)format}.");
        }

        return data;
    }

    private DedupRecord ReadRecord(DedupContainerName name, uint recordId, uint offset, DedupRecordType type)
    {
        Stream container = Container(name);
        if (TryReadRecord(container, offset, recordId, type) is { } record)
            return record;

        // The record moved (the container was compacted): the redirection table maps its id to the new offset.
        if (Redirected(container, recordId) is { } moved && TryReadRecord(container, moved, recordId, type) is { } found)
            return found;

        throw new DedupFormatException($"Data Deduplication record {recordId} was not found in {name.Kind} container {name.FileName}.");
    }

    /// <summary>The record at <paramref name="offset" />; null when that is not the record with this id and type.</summary>
    private DedupRecord? TryReadRecord(Stream container, long offset, uint recordId, DedupRecordType type)
    {
        if (offset < FirstRecordOffset || offset + DedupRecordHeader.FixedLength > container.Length
            || !DedupRecordHeader.TryGetLength(ReadAt(container, offset, DedupRecordHeader.FixedLength), out int headerLength)
            || offset + headerLength > container.Length)
            return null;

        byte[] headerBytes = ReadAt(container, offset, headerLength);
        DedupRecordHeader header = new DedupRecordHeader(headerBytes);
        if (header.Id != recordId || header.Type != type)
            return null;

        if (header.PayloadLength > int.MaxValue || offset + headerLength + header.PayloadLength > container.Length)
            throw new DedupFormatException($"Data Deduplication record {recordId} at {offset} runs past its container.");

        byte[] payload = ReadAt(container, offset + headerLength, (int)header.PayloadLength);
        if (verify && (Crc64LittleEndian.Compute(Crc64Algorithm.Nvme, 0, headerBytes.AsSpan(0, headerBytes.Length - 8)) != header.HeaderCrc || Crc64LittleEndian.Compute(Crc64Algorithm.Nvme, 0, payload) != header.PayloadCrc))
            throw new DedupFormatException($"Data Deduplication record {recordId} at {offset} fails its CRC-64.");

        return new DedupRecord(header, payload);
    }

    /// <summary>The offset the redirection table (or its backup) gives the record; null when neither lists it.</summary>
    private uint? Redirected(Stream container, uint recordId)
    {
        if (!_redirectionTables.TryGetValue(container, out DedupRedirectionTable? table))
            _redirectionTables[container] = table = LoadRedirectionTable(container);

        return table?.Find(recordId);
    }

    private DedupRedirectionTable? LoadRedirectionTable(Stream container)
    {
        foreach (long tableOffset in DedupRedirectionTable.Offsets)
        {
            if (tableOffset + DedupRedirectionTable.HeaderLength > container.Length
                || !DedupRedirectionTable.TryGetItemsLength(ReadAt(container, tableOffset, DedupRedirectionTable.HeaderLength), out int itemsLength, verify)
                || tableOffset + DedupRedirectionTable.HeaderLength + itemsLength > container.Length)
                continue;

            return new DedupRedirectionTable(ReadAt(container, tableOffset + DedupRedirectionTable.HeaderLength, itemsLength));
        }

        return null;
    }

    private Stream Container(DedupContainerName name)
    {
        if (!_containers.TryGetValue(name, out Stream? stream))
            _containers[name] = stream = OpenContainer(name);

        return stream;
    }

    /// <summary>Opens the container file, looking in <c>Hotspot\</c> too for data chunks.</summary>
    private Stream OpenContainer(DedupContainerName name)
    {
        string[] folders = name.Kind == DedupContainerKind.Stream ? ["Stream"] : ["Data", "Hotspot"];
        foreach (string folder in folders)
            if (FindContainerFile(Listing(folder), name) is { } fileName)
            {
                string path = $@"{_root}\{folder}\{fileName}";
                if (!_openFiles.TryGetValue(path, out Stream? stream))
                    _openFiles[path] = stream = source.OpenFile(path);

                return stream;
            }

        throw new DedupFormatException($"Data Deduplication {name.Kind} container {name.FileName} is missing.");
    }

    /// <summary>
    ///     The file of the generation the reference names, else the newest generation of the container: garbage collection rewrites a container
    ///     under the next generation (not seen on the lab image).
    /// </summary>
    private static string? FindContainerFile(IReadOnlyList<string> fileNames, DedupContainerName wanted)
    {
        if (fileNames.Contains(wanted.FileName, StringComparer.OrdinalIgnoreCase))
            return wanted.FileName;

        DedupContainerName? newest = null;
        foreach (string fileName in fileNames)
            if (DedupContainerName.TryParse(wanted.Kind, fileName, out DedupContainerName found) && found.Id == wanted.Id
                && (newest is null || found.Generation > newest.Value.Generation))
                newest = found;

        return newest?.FileName;
    }

    /// <summary>The file names in a folder of the store; empty when the folder does not exist.</summary>
    private IReadOnlyList<string> Listing(string folder)
    {
        if (!_listings.TryGetValue(folder, out IReadOnlyList<string>? names))
            _listings[folder] = names = source.ListFiles($@"{_root}\{folder}") ?? [];

        return names;
    }

    private static byte[] ReadAt(Stream stream, long offset, int count)
    {
        byte[] buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer, 0, count);
        return buffer;
    }
}