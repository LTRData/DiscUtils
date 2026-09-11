# LTRData.DiscUtils.OpticalDisk

Managed .NET optical-disc image access and automatic detection of ISO 9660 and UDF filesystems.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.OpticalDisk
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read optical disc sector data | Yes |
| Detect ISO 9660 and UDF filesystems | Yes |
| Write optical disc sector data | No |
| Create ISO images | Via the included Iso9660 package and CDBuilder |

## Usage notes

This package combines the ISO 9660 and UDF readers with a read-only Disc virtual-disk wrapper and detection providers. Its Formats.Register() is the registration entry point for automatic ISO/UDF detection. It does not burn physical media.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.OpticalDisk.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using System.IO;
using DiscUtils;

DiscUtils.OpticalDisk.Formats.Register();
using var image = File.OpenRead("sample.iso");
foreach (var format in FileSystemManager.DetectFileSystems(image))
    Console.WriteLine(format.Name);
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
