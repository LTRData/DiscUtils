# LTRData.DiscUtils.Vdi

Managed .NET support for reading, creating and modifying fixed and dynamically allocated VirtualBox VDI disk images.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Vdi
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read fixed / dynamic images | Yes |
| Create fixed / dynamic images | Yes |
| Write virtual disk content | Yes |
| Create differencing images | No |

## Usage notes

Differencing-disk creation APIs throw NotImplementedException. Use Disk.Content for virtual sectors, with separate filesystem packages to interpret partitions.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Vdi.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using System.IO;
using DiscUtils.Vdi;

using var disk = new Disk("existing.vdi", FileAccess.Read);
Console.WriteLine(disk.Capacity);
// disk.Content is the virtual disk's sector data, including any partition table.
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
