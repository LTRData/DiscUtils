using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DiscUtils.Internal;
using DiscUtils.DeduplicationRole;
using DiscUtils.DeduplicationRole.ChunkStore;
using DiscUtils.DeduplicationRole.Reparse;
using DiscUtils.DeduplicationRole.Streams;
using DiscUtils.Streams;
using Xunit;

namespace LibraryTests.DeduplicationRole;

/// <summary>Data Deduplication: reparse data, chunk store records and stream maps. The chunk decoders are tested in <c>Compression</c>.</summary>
public class DedupTest
{
    // The reparse data of d\a.bin on the lab image w2025-dedup (Server 2025, ReFS 3.14), after the 8-byte buffer header.
    private const string ReparseHex =
        "0301040146655270A5B3905BFC000000000009000A0004005C0000000A000400580000000C001000600000000B0008007000000006000800780000000500040080000000" +
        "090004008400000000000000000000000D0074008800000000000000010000004BF0E17051E4904A9D19912A2BA408A3FC88CAAEE578DD01000010000000000000000000" +
        "0200000044645270A5838A6774000000000003000A000400280000000E0008002C0000000D00400034000000010000000100000000000100010000000000010000500000" +
        "0100000001000000080800008803000000000000992625F8F0BB5CB9F95CF2EDDEC56830000010000000000000000000000000002608ADE5";

    private static readonly Guid StoreId = new("70E1F04B-E451-4A90-9D19-912A2BA408A3");

    [Fact]
    public void TheReparseDataOfAnOptimizedFileNamesItsChunkStoreAndStreamMap()
    {
        var data = new DedupReparseData(FromHex(ReparseHex));
        Assert.Equal(StoreId, data.StoreId);
        Assert.Equal(0x100000UL, data.FileSize);
        Assert.Equal(Reference(1, 0x10000, 0x5000, 1, 0, 0x100000), Assert.Single(data.Ranges));
    }

    [Fact]
    public void DamagedReparseDataIsRefused()
    {
        var data = FromHex(ReparseHex);
        data[0x70] ^= 1;
        Assert.Throws<DedupFormatException>(() => new DedupReparseData(data));
    }

    [Fact]
    public void ADamagedRedirectionTableFallsBackToItsBackup()
    {
        var first = Encoding.ASCII.GetBytes("plain chunk data ");
        var second = Encoding.ASCII.GetBytes("second");
        var files = Files(first, second, second, streamMapAt: 0x5100);
        var stream = files[@"Stream\00010000.00000001.ccc"];

        // A valid copy of the table at 0x4000, then a primary at 0x3000 that fails its CRC-64.
        Array.Copy(stream, 0x3000, stream, 0x4000, 0x28);
        stream[0x3018] ^= 1;

        using var store = Store(files);
        Assert.Equal(2, store.ReadStreamMap(Reference(7, 0x10000, 0x5000, 1, 0, (ulong)(first.Length + second.Length))).Count());
    }

    [Fact]
    public void AFileReadsFromItsChunksAndFindsAMovedStreamMapThroughTheRedirectionTable()
    {
        var first = Encoding.ASCII.GetBytes("plain chunk data ");
        var second = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("abc", 15)).Substring(0, 43));
        byte[] compressed = [0xFF, 0xFF, 0xFF, 0x1F, (byte)'a', (byte)'b', (byte)'c', 0x17, 0x00, 0x0F, 0x05, 0x17, 0x00];
        var files = Files(first, second, compressed, streamMapAt: 0x5100);
        using var store = Store(files);

        // The reference names 0x5000; the record is at 0x5100 and the Rrtl table says so.
        var chunks = store.ReadStreamMap(Reference(7, 0x10000, 0x5000, 1, 0, (ulong)(first.Length + second.Length))).ToList();
        using var stream = new DedupFileStream(new MemoryStream(new byte[first.Length + second.Length]), store, chunks, first.Length + second.Length);
        using var read = new MemoryStream();
        stream.CopyTo(read);
        Assert.Equal(first.Concat(second).ToArray(), read.ToArray());
    }

    [Fact]
    public void AChunkThatDoesNotMatchItsHashIsRefused()
    {
        var first = Encoding.ASCII.GetBytes("plain chunk data ");
        var second = Encoding.ASCII.GetBytes("second");
        var files = Files(first, second, second, streamMapAt: 0x5000, damageSecondHash: true);
        using var store = Store(files);
        var chunks = store.ReadStreamMap(Reference(7, 0x10000, 0x5000, 1, 0, (ulong)(first.Length + second.Length))).ToList();
        Assert.Equal(first, store.ReadChunk(chunks[0]));
        Assert.Throws<DedupFormatException>(() => store.ReadChunk(chunks[1]));
    }

    private static byte[] FromHex(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        return bytes;
    }

    private static byte[] Sha512Prefix(byte[] data)
    {
        using var sha = SHA512.Create();
        return sha.ComputeHash(data).Take(32).ToArray();
    }

    /// <summary>A range entry of a DdRp block, parsed.</summary>
    private static DedupStreamReference Reference(uint streamMapId, uint containerId, uint offset, uint generation, ulong begin, ulong end)
    {
        var entry = new byte[DedupStreamReference.EntryLength];
        EndianUtilities.WriteBytesLittleEndian(streamMapId, entry, 0);
        EndianUtilities.WriteBytesLittleEndian(containerId, entry, 4);
        EndianUtilities.WriteBytesLittleEndian(offset, entry, 8);
        EndianUtilities.WriteBytesLittleEndian(generation, entry, 0xC);
        EndianUtilities.WriteBytesLittleEndian(end, entry, 0x30);
        EndianUtilities.WriteBytesLittleEndian(begin, entry, 0x38);
        return new DedupStreamReference(entry);
    }

    private static DedupChunkStore Store(Dictionary<string, byte[]> files)
    {
        var root = $@"System Volume Information\Dedup\ChunkStore\{{{StoreId.ToString().ToUpperInvariant()}}}.ddp\";
        return new DedupChunkStore(StoreId, new MemoryDedupSource(root, files), verify: true);
    }

    private sealed class MemoryDedupSource(string root, Dictionary<string, byte[]> files) : IDedupSource
    {
        public IReadOnlyList<string> ListFiles(string directory)
        {
            return files.Keys.Where(k => k.StartsWith(directory.Substring(root.Length) + @"\", StringComparison.Ordinal))
                .Select(k => k.Substring(k.IndexOf('\\') + 1)).ToList();
        }

        public Stream OpenFile(string path)
        {
            return new MemoryStream(files[path.Substring(root.Length)]);
        }
    }

    /// <summary>A stream container with one stream map (id 7) of two chunks, and a data container with them (the second XPRESS-compressed when it differs).</summary>
    private static Dictionary<string, byte[]> Files(byte[] first, byte[] second, byte[] secondStored, int streamMapAt, bool damageSecondHash = false)
    {
        var data = new byte[0x6000];
        var firstAt = 0x5000;
        var secondAt = WriteRecord(data, firstAt, 1, 1, DataHeader(first, 0), first);
        WriteRecord(data, secondAt, 2, 1, DataHeader(second, secondStored == second ? 0u : 2u, damageSecondHash), secondStored);

        var map = new byte[8 + 2 * 64];
        Encoding.ASCII.GetBytes("Smap").CopyTo(map, 0);
        MapEntry(map, 8, 1, firstAt, (ulong)first.Length, first, first.Length);
        MapEntry(map, 72, 2, secondAt, (ulong)(first.Length + second.Length), second, secondStored.Length);

        var stream = new byte[0x6000];
        WriteRecord(stream, streamMapAt, 7, 2, new byte[0x38], map);
        Encoding.ASCII.GetBytes("Rrtl").CopyTo(stream, 0x3000);
        EndianUtilities.WriteBytesLittleEndian(1u, stream, 0x3008);
        EndianUtilities.WriteBytesLittleEndian(8u, stream, 0x300C);
        EndianUtilities.WriteBytesLittleEndian(7u, stream, 0x3020);
        EndianUtilities.WriteBytesLittleEndian((uint)streamMapAt, stream, 0x3024);
        EndianUtilities.WriteBytesLittleEndian(Crc64LittleEndian.Compute(Crc64Algorithm.Nvme, 0, stream.AsSpan(0x3000, 0x18)), stream, 0x3018);
        return new Dictionary<string, byte[]> { [@"Stream\00010000.00000001.ccc"] = stream, [@"Data\00000001.00000001.ccc"] = data };
    }

    private static byte[] DataHeader(byte[] chunk, uint format, bool damageHash = false)
    {
        // Record bytes +0x20 to +0x58: the compression format at +0x20, the hash at +0x28, then the two CRC-64s.
        var extra = new byte[0x38];
        EndianUtilities.WriteBytesLittleEndian(format, extra, 0);
        Sha512Prefix(chunk).CopyTo(extra, 8);
        if (damageHash)
        {
            extra[8] ^= 1;
        }

        return extra;
    }

    private static void MapEntry(byte[] map, int at, uint id, int offset, ulong end, byte[] chunk, int stored)
    {
        EndianUtilities.WriteBytesLittleEndian(id, map, at);
        EndianUtilities.WriteBytesLittleEndian(1u, map, at + 4);
        EndianUtilities.WriteBytesLittleEndian((uint)offset, map, at + 8);
        EndianUtilities.WriteBytesLittleEndian(1u, map, at + 12);
        EndianUtilities.WriteBytesLittleEndian(end, map, at + 16);
        Sha512Prefix(chunk).CopyTo(map, at + 0x18);
        EndianUtilities.WriteBytesLittleEndian((uint)stored, map, at + 0x38);
    }

    /// <summary>
    /// A "Ckhr" record at <paramref name="at"/>: 0x20 fixed bytes, then <paramref name="extra"/> (which ends with the payload's and the header's CRC-64,
    /// filled in here), then the payload. Returns the next 8-aligned offset.
    /// </summary>
    private static int WriteRecord(byte[] container, int at, uint id, ushort type, byte[] extra, byte[] payload)
    {
        var headerLength = 0x20 + extra.Length;
        Encoding.ASCII.GetBytes("Ckhr").CopyTo(container, at);
        EndianUtilities.WriteBytesLittleEndian(id, container, at + 8);
        EndianUtilities.WriteBytesLittleEndian((uint)payload.Length, container, at + 0xC);
        EndianUtilities.WriteBytesLittleEndian(type, container, at + 0x10);
        EndianUtilities.WriteBytesLittleEndian((ushort)(headerLength - 0x30), container, at + 0x12);
        extra.CopyTo(container, at + 0x20);
        EndianUtilities.WriteBytesLittleEndian(Crc64LittleEndian.Compute(Crc64Algorithm.Nvme, 0, payload), container, at + headerLength - 16);
        EndianUtilities.WriteBytesLittleEndian(Crc64LittleEndian.Compute(Crc64Algorithm.Nvme, 0, container.AsSpan(at, headerLength - 8)), container, at + headerLength - 8);
        payload.CopyTo(container, at + headerLength);
        return (at + headerLength + payload.Length + 7) & ~7;
    }
}
