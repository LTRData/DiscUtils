# LTRData.DiscUtils.Sdi

Managed .NET reader for Microsoft Simple Deployment Image (SDI) headers, section metadata and section content streams.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Sdi
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read headers and sections | Yes |
| Create structured SDI images | No |
| Edit section layout / metadata | No |

## Usage notes

SdiFile.Sections describes image blobs and OpenSection opens their byte ranges. Section streams can forward writes if the input stream is writable; this is raw byte access, not a structured SDI writer. Open the input read-only for inspection. No format registration is required.

## Example

```csharp
using System;
using System.IO;
using DiscUtils.Sdi;

using var stream = File.OpenRead("boot.sdi");
using var image = new SdiFile(stream);
foreach (var section in image.Sections)
    Console.WriteLine($"{section.Index}: {section.SectionType}, {section.Length} bytes");
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
