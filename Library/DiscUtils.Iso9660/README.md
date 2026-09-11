# LTRData.DiscUtils.Iso9660

Managed .NET support for reading ISO 9660 optical-disc filesystems and building ISO images with Joliet and boot-image support.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Iso9660
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read ISO filesystems | Yes, including Joliet and Rock Ridge views |
| Create ISO images | Yes, using CDBuilder |
| Modify existing ISO filesystems | No |

## Usage notes

CDReader reads existing images; CDBuilder assembles new images from files, byte arrays or streams. Building a new image is distinct from editing an existing one. Automatic ISO/UDF detection is provided by LTRData.DiscUtils.OpticalDisk, not by a Formats class in this package.

## Example

```csharp
using System.Text;
using DiscUtils.Iso9660;

var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "SAMPLE" };
builder.AddFile(@"Folder\hello.txt", Encoding.UTF8.GetBytes("Hello from DiscUtils!"));
builder.Build("sample.iso");
```

## Related packages

- [LTRData.DiscUtils.OpticalDisk](https://www.nuget.org/packages/LTRData.DiscUtils.OpticalDisk)
- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.FileSystems](https://www.nuget.org/packages/LTRData.DiscUtils.FileSystems)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
