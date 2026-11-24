# LTRData.DiscUtils.ReFs

Managed .NET support for reading ReFS 3.x filesystems. **Experimental, read-only.**

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Capabilities

| Capability | Support |
| --- | --- |
| Detect ReFS boot sector, generic discovery | Yes |
| Read files and directories (lazy enumeration, upcase-table name matching) | Yes |
| Alternate data streams (`IFileSystemWithAltStreams`) | Yes |
| Windows metadata (`IWindowsFileSystem`): security descriptors, reparse points, hard-link counts, standard information, file ids | Yes, read-only; setters throw `NotSupportedException` |
| Extended attributes | Yes (`ReFsFileSystem.GetExtendedAttributes`) |
| Metadata page checksums (CRC32C, CRC-64, SHA-256, XXH64) and integrity-stream data checksums (including XXH64) | Yes, verified on read; a mismatch raises an error |
| Volume compression (LZ4, LZ4 HC, Zstandard) | Yes; Zstandard is unavailable on net46 |
| Windows Data Deduplication | Yes: optimized files read from the chunk store through `LTRData.DiscUtils.DeduplicationRole`, chunks checked against their hashes (`ReFsFileSystemOptions.ReadDeduplicatedFiles`, on by default) |
| EFS-encrypted files | Listed; reading their data throws `NotSupportedException` (no decryption) |
| Redo-log replay of a dirty volume | Experimental and opt-in (`ReFsFileSystemOptions.ReplayLog`), in memory only; logs up to Windows build 29683 |
| Structure validation (`ReFsFileSystem.Validate`) | Yes |
| Diagnostics: volume and entry details, streams and snapshots (`OpenStream`), per-file data verification, redo-log scan (`GetVolumeDetails`, `GetEntryDetails`, `VerifyData`, `ReadRedoLog`) | Yes, experimental: shapes may change |
| Create a filesystem / modify an existing one | No |
| ReFS 1.x (Server 2012 / 2012 R2) | No: refused on open |
| Free-space accounting, DOS short names | No: `SupportsUsedAvailableSpace` is `false`; short names are `null` |
| Symbolic links | Listed as ordinary entries with the `ReparsePoint` attribute; targets are not followed |
| Allocation extents, cluster map | Not exposed yet |
| Async I/O, thread safety | Synchronous reads; instances are not thread safe |

The target is ReFS 3.x. Behaviour is verified against Windows-created images of ReFS 3.1, 3.4, 3.5, 3.10 and 3.14 (native and upgraded),
with 4 KiB and 64 KiB clusters, including Windows Server 2025 and Insider build 29683 images (see Tests/LibraryTests/ReFs). The
full-content checks use 3.14 images.

## Registration

```csharp
DiscUtils.ReFs.Formats.Register();
```

Each registration call covers only its own assembly. Direct use of `ReFsFileSystem` needs no registration.

## Example

```csharp
using System.IO;
using DiscUtils.ReFs;

// The stream starts at the ReFS volume, not at a partitioned disk.
using var fs = new ReFsFileSystem(volumeStream);
foreach (var file in fs.Root.GetFiles())
    Console.WriteLine(file.FullName);

using var data = fs.OpenFile(@"folder\file.txt", FileMode.Open, FileAccess.Read);

// A volume that was not cleanly dismounted: apply the redo log in memory (experimental).
using var dirty = new ReFsFileSystem(volumeStream, new ReFsFileSystemOptions { ReplayLog = true });
```

## Provenance

The code is an independent implementation written for this project. It contains no source code from other projects and no Microsoft code.

Microsoft does not publish the ReFS on-disk format. The format knowledge comes from these sources:

- **Public documentation:** the ReFS documentation of the forefst project, credited in the source comments where it was used.
- **Microsoft's published specifications:** reparse points, file attributes and security descriptors ([MS-FSCC], [MS-DTYP]).
- **Published formats and standard algorithms:** LZ4 and Zstandard for volume compression, CRC32C, CRC-64, SHA-256 and XXH64 for
  checksums.
- **Observation:** volumes were created and changed by several Windows versions, and their structures were examined. The reader's output
  was compared with what Windows reports for the same volumes.
- **Interoperability analysis:** where these sources left gaps, the behaviour of Windows' ReFS driver was studied only as far as needed to
  read volumes it writes. No driver code, function names or addresses are included.

Data Deduplication support comes from `LTRData.DiscUtils.DeduplicationRole`; its README describes its own sources.
