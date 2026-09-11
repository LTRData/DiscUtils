# LTRData.DiscUtils.Xva

Managed .NET support for reading Xen Virtual Appliance (XVA) disks and creating new XVA appliances from disk streams.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Xva
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read existing appliance disks | Yes |
| Build new appliances | Yes, using VirtualMachineBuilder |
| Modify existing appliance disks | No |

## Usage notes

VirtualMachine exposes the disks inside an existing XVA. VirtualMachineBuilder creates minimal appliances from one or more streams. Builder support does not make existing XVA disk content writable.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Xva.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System.IO;
using DiscUtils.Xva;
using DiscUtils.Streams;

using var disk = File.OpenRead("disk.raw");
using var builder = new VirtualMachineBuilder();
builder.AddDisk("System disk", disk, Ownership.None);
builder.Build("appliance.xva");
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
