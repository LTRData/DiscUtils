# LTRData.DiscUtils.Btrfs

Managed .NET support for reading Btrfs filesystems, including file contents and filesystem metadata.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Btrfs
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read files and directories | Yes |
| Create a filesystem | No |
| Modify an existing filesystem | No |

## Usage notes

Includes subvolume enumeration, Unix metadata and file allocation extents. Zlib and LZO extent decompression are supported; Zstandard support is excluded from the net46 build. Filesystem feature and multi-device layout support is not universal.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Btrfs.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using System.IO;
using DiscUtils.Btrfs;

// The stream starts at the filesystem, not at a whole disk's partition table.
using var stream = File.OpenRead("btrfs.img");
using var fs = new BtrfsFileSystem(stream);
foreach (var file in fs.Root.GetFiles())
    Console.WriteLine(file.FullName);
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
