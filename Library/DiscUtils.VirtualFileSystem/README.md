# LTRData.DiscUtils.VirtualFileSystem

Composable .NET virtual filesystem trees, TAR and ZIP filesystem readers, and a TAR image builder through DiscUtils abstractions.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.VirtualFileSystem
```

## Capabilities

| Capability | Support |
| --- | --- |
| Build a virtual directory tree | Yes |
| Supply file content | Streams or file-open delegates |
| Expose common filesystem APIs | Yes |
| Read existing TAR / ZIP archives | Yes, as read-only filesystem views |
| Create TAR archives | Yes, using TarFileSystemBuilder |
| Create ZIP archives / modify existing archives | No through these filesystem wrappers |
| Write behavior | Depends on options, callbacks and implemented operations |

## Usage notes

VirtualFileSystem, its directory/file entries and VirtualFileSystemOptions let applications compose a filesystem view. TarFileSystem and ZipFileSystem provide read-only archive views; TarFileSystemBuilder builds new TAR images. The TAR view skips symbolic links. ZIP entries are decompressed into memory when opened, including large entries backed by sparse memory buffers. CopyFile is not implemented; some metadata APIs also have limitations. Freeze can make an assembled tree read-only. Registration enables automatic TAR/ZIP filesystem detection; no registration is needed to construct a tree or use the readers/builder directly.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.VirtualFileSystem.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using DiscUtils.VirtualFileSystem;

using var fs = new VirtualFileSystem(new VirtualFileSystemOptions { CanWrite = true });
fs.AddDirectory("Reports");
fs.Freeze();
Console.WriteLine(fs.DirectoryExists("Reports"));
```

### Open a TAR filesystem view

```csharp
using System;
using System.IO;
using DiscUtils.VirtualFileSystem;

using var archive = File.OpenRead("files.tar");
using var fs = new TarFileSystem(archive, ownsStream: false);
foreach (var file in fs.Root.GetFiles())
    Console.WriteLine(file.FullName);
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
