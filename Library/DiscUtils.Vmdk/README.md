# LTRData.DiscUtils.Vmdk

Managed .NET support for reading, creating and modifying supported VMware VMDK layouts, including differencing disk chains.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Vmdk
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read existing images | Yes, for supported layouts |
| Create new images | Yes, for supported DiskCreateType values |
| Write virtual disk content | Yes, for writable layouts |
| Differencing disks and parent chains | Yes |

## Usage notes

Support varies by extent/layout type. Compressed hosted-sparse extents are read-only. Opening from a single Stream is more restricted than path-based opening, which can resolve descriptor files, external extents and parents. Creation support is not implied for every DiskCreateType enum value.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Vmdk.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using System.IO;
using DiscUtils.Vmdk;

using var disk = new Disk("existing.vmdk", FileAccess.Read);
Console.WriteLine(disk.Capacity);
// disk.Content is the virtual disk's sector data, including any partition table.
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
