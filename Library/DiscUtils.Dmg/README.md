# LTRData.DiscUtils.Dmg

.NET support for reading Apple DMG/UDIF disk images and decompressing supported image data.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Dmg
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read UDIF image data | Yes, including supported compression modes |
| Create UDIF images | No |
| Modify UDIF content | No |

## Usage notes

The UDIF content buffer is read-only even if an outer object reports a writable backing stream. Compression support includes zlib, bzip2, ADC and LZFSE. LZFSE uses LzfseSharp on modern .NET targets and lzfse-net on .NET Framework/.NET Standard targets; deployment requirements therefore depend on the selected target and decoder dependency.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Dmg.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using System.IO;
using DiscUtils.Dmg;
using DiscUtils.Streams;

using var image = File.OpenRead("existing.dmg");
using var disk = new Disk(image, Ownership.None);
Console.WriteLine(disk.Capacity);
// disk.Content exposes decompressed disk data; inspect its partitions separately.
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
