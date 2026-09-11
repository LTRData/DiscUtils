# LTRData.DiscUtils.Lvm

Managed .NET discovery and stream mapping for supported Linux LVM logical volumes and Linux MD RAID 1 members.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Lvm
```

## Capabilities

| Capability | Support |
| --- | --- |
| Discover LVM physical / logical volumes | Yes |
| Read supported logical volumes | Yes: single-stripe (linear) segments |
| Discover Linux MD arrays | RAID 1 partition members |
| Create / edit LVM or MD metadata | No |

## Usage notes

LVM requires all referenced physical volumes and supports segments recorded as striped with exactly one stripe; multi-stripe, thin, snapshot and RAID segment layouts are not implemented. The MD provider currently discovers RAID 1 partition members. These are mapping APIs, not volume-management or array-repair tools. Mapped streams may forward writes to writable storage; open source disks read-only for inspection.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Lvm.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using System.IO;
using DiscUtils;

DiscUtils.Core.Formats.Register();
DiscUtils.Lvm.Formats.Register();
using var disk = VirtualDisk.OpenDisk("linux.img", FileAccess.Read);
var volumes = new VolumeManager(disk);
foreach (var volume in volumes.GetLogicalVolumes())
    Console.WriteLine(volume.Identity);
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
