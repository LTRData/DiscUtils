# LTRData.DiscUtils.Udf

Managed .NET reader and image builder for UDF optical-disc filesystems.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Udf
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read files and directories | Yes |
| Create a filesystem | UDF 2.01 with UdfBuilder |
| Modify an existing filesystem | No |

## Usage notes

UdfReader exposes files and extended attributes. Size, UsedSpace and AvailableSpace are not implemented. For automatic ISO/UDF detection, reference LTRData.DiscUtils.OpticalDisk and register its providers; this package has no generated Formats entry point.

UdfBuilder creates a read-only UDF 2.01 image with one physical partition and 2048-byte logical blocks. Files are limited to 234 short allocation descriptors (approximately 234 GiB per file); the partition is limited to approximately 8 TiB. The builder preserves names, directories and file contents, but does not import source filesystem attributes, extended attributes or permissions.

`Build()` and `BuildAsync(cancellationToken)` return a seekable virtual image as a `Stream`. Construction creates metadata in memory and opens no file payloads. Every image owns the streams returned by its source factories; each factory must return a new readable, seekable stream of the declared length on every call. Async image reads use the source stream's async methods. Separate builds have independent positions; a single image stream must not be read concurrently.

The source-path overload checks length and last-write time whenever it opens the file. These checks do not make a content snapshot: keep source files and retained byte arrays unchanged for the image's lifetime. Unexpected truncation fails, and reads never include source data beyond a declared extent. The overloads that build to an output file or stream materialize the entire image, including payloads and zero padding.

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

Create a virtual image from existing files:

```csharp
using System.IO;
using DiscUtils.Udf;

var builder = new UdfBuilder { VolumeIdentifier = "DISC" };
builder.AddDirectory("BDMV/EMPTY");
builder.AddFile("BDMV/index.bdmv", Path.Combine("source", "BDMV", "index.bdmv"));
builder.AddFile("readme.txt", System.Text.Encoding.UTF8.GetBytes("Virtual UDF image"));
using Stream image = builder.Build();
// Serve reads/seeks from image, or materialize it with builder.Build(output).
```

## Related packages

- [LTRData.DiscUtils.OpticalDisk](https://www.nuget.org/packages/LTRData.DiscUtils.OpticalDisk)
- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.FileSystems](https://www.nuget.org/packages/LTRData.DiscUtils.FileSystems)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
