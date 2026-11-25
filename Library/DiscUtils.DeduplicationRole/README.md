# LTRData.DiscUtils.DeduplicationRole

Reads files optimized by the **Windows Server Data Deduplication** role from their chunk store, on any DiscUtils file system that exposes
reparse points (NTFS, ReFS). **Read-only.**

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## What Data Deduplication does to a file

When the role optimizes a file, Windows moves its data into a chunk store under `System Volume Information\Dedup\ChunkStore\{GUID}.ddp` and
leaves a sparse stub with a reparse point (tag `IO_REPARSE_TAG_DEDUP`, `0x80000013`). Read without the dedup filter, such a file is all zeros,
apart from ranges written after the optimization. This library reads the file the way Windows does with the filter loaded.

## Capabilities

| Capability | Support |
| --- | --- |
| Reparse data (property blocks `FeRp`/`DdRp`, the short form) | Yes, checked against their CRC-32 |
| Stream maps, data chunks, the redirection table of moved stream maps | Yes |
| Chunk compression: none, LZNT1, XPRESS, XPRESS Huffman | Yes, with the DiscUtils decoders |
| Data written after the optimization (the stub's own ranges) | Yes: taken from the stub's `SparseStream.Extents` |
| Verification of chunk hashes (SHA-512) and record checksums (CRC-64) | Yes, can be turned off |
| Files with a recall bitmap (partly recalled) | No: `NotSupportedException` |
| Optimizing files, writing to the chunk store, garbage collection | No |

## Usage

**NTFS and ReFS read optimized files by themselves**: `NtfsFileSystem` and `ReFsFileSystem` use this library when a deduplicated file is
opened for reading (`NtfsOptions.ReadDeduplicatedFiles` and `ReFsFileSystemOptions.ReadDeduplicatedFiles`, both on by default). With the
option off they return the stub, holding only the ranges written since the optimization.

For other file systems with reparse points (`IWindowsFileSystem`), or to read deduplicated files explicitly:

```csharp
using DiscUtils.DeduplicationRole;

// fs: an NtfsFileSystem (or another DiscFileSystem that implements IWindowsFileSystem).
using var reader = new DedupFileSystemReader(fs);

if (reader.IsDeduplicated(@"Shares\big.iso"))
    Console.WriteLine("optimized by Data Deduplication");

// The content as Windows reads it; a file that is not optimized opens as stored.
using var data = reader.OpenRead(@"Shares\big.iso");
```

Keep one `DedupFileSystemReader` per volume: it keeps the chunk store's container files open until it is disposed. It is not thread-safe.

For a chunk store that is not reached through a `DiscFileSystem`, implement `IDedupSource` (list a folder, open a file) and call
`DedupReader.Open(reparseData, stub)` with the file's reparse data (after the 8-byte reparse header) and its stub stream.

Errors: damaged reparse data, containers or chunks throw `DedupFormatException` (an `IOException`); unsupported layouts throw
`NotSupportedException`; a missing container throws `FileNotFoundException`.

## Tested

- Synthetic containers, stream maps and the redirection table, and damaged reparse data and chunks (`Tests/LibraryTests/DeduplicationRole`).
- The chunk decoders against Windows' own compressor (`Tests/LibraryTests/Compression`).
- A Windows Server 2025 ReFS volume with deduplicated files (lab image `w2025-dedup`; needs `REFS_FIXTURE_ROOT`): every optimized file read on
  the ReFS volume, and on an NTFS volume built from it (the chunk store, the sparse stubs and the reparse points copied), matches the content
  Windows reports, both through `DedupFileSystemReader` and through `NtfsFileSystem.OpenFile`.
- Not yet tested on an NTFS volume deduplicated by Windows itself.

## Provenance

The code is an independent implementation written for this project; it contains no source code from other projects. It was split out of the
ReFS reader (`DiscUtils.ReFs`).

The format knowledge comes from these sources:

- **Microsoft's published specifications:** the reparse tag `IO_REPARSE_TAG_DEDUP` and the reparse point buffer ([MS-FSCC]), and the LZNT1,
  XPRESS and XPRESS Huffman chunk compression ([MS-XCA], through the DiscUtils decoders).
- **Standard algorithms:** CRC-32, CRC-64 (NVMe polynomial) and SHA-512.
- **Interoperability facts:** Microsoft does not document the chunk store layout (containers, stream maps, the redirection table). Its
  record layouts were described from the publicly available readers ntfs-3g-dedup-plugin and DedupInspector, which are credited here as
  references.
- **Observation:** every layout detail the reader relies on was checked against volumes deduplicated by Windows Server 2025. The reader's
  output was compared with the file content Windows returns.
