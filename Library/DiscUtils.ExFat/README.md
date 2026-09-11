# LTRData.DiscUtils.ExFat

Managed .NET implementation for reading, formatting and modifying exFAT filesystems.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.ExFat
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read files and directories | Yes |
| Create / format a filesystem | Yes |
| Modify files and directories | Yes, with a writable backing stream |

## Usage notes

Use ExFatFileSystem.Format with a PhysicalVolumeInfo to format a volume. Existing filesystems expose file/directory creation, deletion, copying, moving and timestamp operations. Filesystem streams must start at the volume offset.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.ExFat.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using System.IO;
using DiscUtils.ExFat;

// The stream starts at the filesystem, not at a whole disk's partition table.
using var stream = File.OpenRead("exfat.img");
using var fs = new ExFatFileSystem(stream);
foreach (var file in fs.Root.GetFiles())
    Console.WriteLine(file.FullName);
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
