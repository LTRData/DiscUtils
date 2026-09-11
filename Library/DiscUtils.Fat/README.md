# LTRData.DiscUtils.Fat

Managed .NET implementation for reading, formatting and modifying FAT12, FAT16 and FAT32 filesystems.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Fat
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read files and directories | Yes |
| Create / format a filesystem | Yes |
| Modify files and directories | Yes, with a writable backing stream |

## Usage notes

Includes floppy and partition formatting. Select FormatPartition for a partition or FormatFloppy for a floppy image. FAT variant, capacity and geometry constraints apply.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Fat.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System.IO;
using DiscUtils;
using DiscUtils.Fat;

using var image = File.Create("floppy.vfd");
using var fs = FatFileSystem.FormatFloppy(image, FloppyDiskType.HighDensity, "MY FLOPPY  ");
using var file = fs.OpenFile("hello.txt", FileMode.Create, FileAccess.Write);
using var writer = new StreamWriter(file);
writer.WriteLine("Hello from DiscUtils!");
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
