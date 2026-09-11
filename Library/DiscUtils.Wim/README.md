# LTRData.DiscUtils.Wim

Managed .NET support for reading Windows Imaging Format (WIM) containers and accessing their image filesystems and metadata.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Wim
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read WIM images and files | Yes |
| Create WIM containers | No |
| Modify existing WIM images | No |
| Resource decompression | XPRESS and LZX |

## Usage notes

WimFile can contain multiple filesystem images; GetImage uses a zero-based index. These are file-based images, not virtual disk sector streams. This is not a general ESD/LZMS decoder. Use WimFile directly; this package has no generated format-registration entry point.

## Example

```csharp
using System;
using System.IO;
using DiscUtils.Wim;

using var stream = File.OpenRead("install.wim");
var wim = new WimFile(stream);
Console.WriteLine(wim.ImageCount);
using var fs = wim.GetImage(0);
foreach (var file in fs.Root.GetFiles())
    Console.WriteLine(file.FullName);
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
