# LTRData.DiscUtils.HfsPlus

Managed .NET support for reading HFS+ filesystems, including file contents and filesystem metadata.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.HfsPlus
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read files and directories | Yes |
| Create a filesystem | No |
| Modify an existing filesystem | No |

## Usage notes

Includes Unix metadata and file allocation extents. The package reads HFS+ volumes; it is not an APFS implementation.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.HfsPlus.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using System.IO;
using DiscUtils.HfsPlus;

// The stream starts at the filesystem, not at a whole disk's partition table.
using var stream = File.OpenRead("hfsplus.img");
using var fs = new HfsPlusFileSystem(stream);
foreach (var file in fs.Root.GetFiles())
    Console.WriteLine(file.FullName);
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
