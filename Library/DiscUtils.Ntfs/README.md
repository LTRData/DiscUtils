# LTRData.DiscUtils.Ntfs

Managed .NET implementation for reading, formatting and modifying NTFS filesystems, including NTFS-specific file metadata.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Ntfs
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read files and directories | Yes |
| Create / format a filesystem | Yes |
| Modify files and directories | Yes, with a writable backing stream |
| NTFS `$LogFile` journal analysis and recovery | Yes, for supported records and safe recovery states |

## Usage notes

Supports alternate data streams, security descriptors, reparse points, hard links and allocation metadata through NTFS-specific APIs. Formatting is available through NtfsFileSystem.Format. Support for these APIs does not imply support for every Windows NTFS feature.

### NTFS journal recovery

DiscUtils can inspect the NTFS `$LogFile` journal after an unclean shutdown and replay supported metadata changes. `NtfsJournal.Analyze(volumeStream)` reports the recovery state without writing; `NtfsFileSystem.JournalInfo` describes what happened when opening a filesystem.

- **Default writable open:** `new NtfsFileSystem(stream)` refuses a journal requiring recovery or reset, or one it cannot safely interpret. It does not automatically change the volume.
- **Default read-only open:** supported journal changes are replayed in memory, leaving the source unchanged. If recovery is unsupported, the filesystem opens without replay and `JournalInfo.Status` reports `Unsupported`; callers must not assume its metadata is current.
- **Explicit recovery:** `new NtfsFileSystem(stream, NtfsJournalMode.Replay)` or `NtfsJournal.Replay(stream)` performs supported persistent recovery on a writable stream. `NtfsJournalMode.Refuse` rejects volumes requiring recovery; `Ignore` bypasses journal handling and can expose stale metadata.

**Limitations:** Persistent replay is not atomic and is refused when metadata undo is required; such undo is supported only in memory. The hibernation check covers local evidence, not suspended Windows installations holding the volume from elsewhere. Before persistent replay, ensure the volume is offline and exclusively owned. The `SparseStream.Flush(true)` durability barriers depend on the runtime, all stream wrappers and the underlying storage honoring them.

See [recovery and undo qualification](../../docs/ntfs-journal-recovery-qualification.md), [hibernation safeguards](../../docs/ntfs-journal-hibernation.md) and [replay durability](../../docs/ntfs-journal-replay-durability.md).

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Ntfs.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System.IO;
using DiscUtils.Ntfs;

// An existing NTFS volume image, starting at the filesystem boot sector.
using var image = File.Open("ntfs.img", FileMode.Open, FileAccess.ReadWrite);
// A writable open requires a clean journal by default; recovery needs explicit opt-in.
using var fs = new NtfsFileSystem(image);
fs.CreateDirectory("Reports");
using var file = fs.OpenFile(@"Reports\hello.txt", FileMode.Create, FileAccess.Write);
using var writer = new StreamWriter(file);
writer.WriteLine("Hello from DiscUtils!");
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
