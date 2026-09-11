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

## Usage notes

Supports alternate data streams, security descriptors, reparse points, hard links and allocation metadata through NTFS-specific APIs. Formatting is available through NtfsFileSystem.Format. Support for these APIs does not imply support for every Windows NTFS feature.

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
