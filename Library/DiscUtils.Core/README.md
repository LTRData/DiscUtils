# LTRData.DiscUtils.Core

Common .NET disk, filesystem, partition and volume abstractions for DiscUtils, including raw disks and format-discovery infrastructure.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Core
```

## Capabilities

| Capability | Support |
| --- | --- |
| Common disk / filesystem APIs | VirtualDisk, DiscFileSystem and related abstractions |
| Raw disk content | Read, create and write |
| BIOS/MBR and GPT partition tables | Read, initialize and modify |
| Discovery infrastructure | Disk, filesystem, transport and volume providers |

## Usage notes

Includes partition/volume discovery, Windows dynamic-volume mapping and file transport. Core does not contain the NTFS, FAT, ISO, VHD or other individual format implementations; reference those packages separately. Register Core for generic RAW disk and local-file transport access. Filesystem constructors generally expect a volume stream, not a whole partitioned disk.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Core.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using System.IO;
using DiscUtils;

DiscUtils.Core.Formats.Register();
using var disk = VirtualDisk.OpenDisk("disk.img", FileAccess.Read);
var volumes = new VolumeManager(disk);
foreach (var volume in volumes.GetLogicalVolumes())
    Console.WriteLine(volume.Identity);
```

## Related packages

- [LTRData.DiscUtils.Streams](https://www.nuget.org/packages/LTRData.DiscUtils.Streams)
- [LTRData.DiscUtils.Containers](https://www.nuget.org/packages/LTRData.DiscUtils.Containers)
- [LTRData.DiscUtils.FileSystems](https://www.nuget.org/packages/LTRData.DiscUtils.FileSystems)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
