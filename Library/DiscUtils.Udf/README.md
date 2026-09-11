# LTRData.DiscUtils.Udf

Managed .NET reader for UDF optical-disc filesystems and their files, directories and extended attributes.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Udf
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read files and directories | Yes |
| Create a filesystem | No |
| Modify an existing filesystem | No |

## Usage notes

UdfReader exposes files and extended attributes. Size, UsedSpace and AvailableSpace are not implemented. For automatic ISO/UDF detection, reference LTRData.DiscUtils.OpticalDisk and register its providers; this package has no generated Formats entry point.

## Example

```csharp
using System;
using System.IO;
using DiscUtils.Udf;

// The stream starts at the filesystem, not at a whole disk's partition table.
using var stream = File.OpenRead("disc.udf");
using var fs = new UdfReader(stream);
foreach (var file in fs.Root.GetFiles())
    Console.WriteLine(file.FullName);
```

## Related packages

- [LTRData.DiscUtils.OpticalDisk](https://www.nuget.org/packages/LTRData.DiscUtils.OpticalDisk)
- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.FileSystems](https://www.nuget.org/packages/LTRData.DiscUtils.FileSystems)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
