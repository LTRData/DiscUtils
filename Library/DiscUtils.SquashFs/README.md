# LTRData.DiscUtils.SquashFs

Managed .NET support for reading SquashFS filesystems and building new compressed SquashFS images.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.SquashFs
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read existing filesystems | Yes |
| Build new images | Yes, using SquashFileSystemBuilder |
| Modify an existing filesystem | No |
| Built-in compression | Zlib; other codecs require application callbacks |

## Usage notes

The reader handles basic and extended inode forms, sparse files, hard links and symbolic links. Configure SquashFileSystemReaderOptions.GetDecompressor or SquashFileSystemBuilderOptions.GetCompressor for codecs other than zlib. Recognizing a compression identifier does not supply that codec. Use Build(Stream) or Build(string) for larger outputs; parameterless Build() buffers the result in memory.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.SquashFs.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System.Text;
using DiscUtils.SquashFs;

var builder = new SquashFileSystemBuilder();
builder.AddFile("hello.txt", Encoding.UTF8.GetBytes("Hello from DiscUtils!"));
builder.Build("sample.squashfs");
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
