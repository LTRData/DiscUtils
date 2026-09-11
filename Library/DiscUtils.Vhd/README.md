# LTRData.DiscUtils.Vhd

Managed .NET support for reading, creating and modifying fixed, dynamic and differencing Microsoft VHD virtual disk images.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Vhd
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read existing images | Yes |
| Create fixed / dynamic images | Yes |
| Create differencing images | Yes |
| Write virtual disk content | Yes, for writable images |
| Parent chains | Yes |

## Usage notes

Disk.Content exposes virtual sectors. A disk image and a filesystem are separate layers: add partitioning and filesystem packages to work with files inside the disk. Parent images must be accessible when opening differencing disks. This package does not install a Windows virtual-disk driver.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Vhd.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System.IO;
using DiscUtils.Vhd;
using DiscUtils.Streams;

using var image = File.Create("new.vhd");
using var disk = Disk.InitializeDynamic(image, Ownership.None, 64L * 1024 * 1024);
// The new disk is blank; partition and format disk.Content as needed.
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
