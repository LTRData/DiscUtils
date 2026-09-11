# LTRData.DiscUtils.Nfs

Managed .NET NFS v3 client for remote file and directory operations and NFS-backed DiscUtils disk access.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Nfs
```

## Capabilities

| Capability | Support |
| --- | --- |
| List exports and read files | Yes |
| Create / modify / delete files and directories | Yes, subject to server permissions |
| NFS protocol version | v3 |

## Usage notes

NfsFileSystem exposes an export through the common filesystem API. Credentials, export policy and server capabilities govern access. The NFS transport can open disk-image files stored on an export; register their format providers separately. Not every Windows-style metadata operation maps to NFS.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Nfs.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using DiscUtils.Nfs;

foreach (var export in NfsFileSystem.GetExports("192.0.2.10"))
    Console.WriteLine(export);
using var fs = new NfsFileSystem("192.0.2.10", "/export/images");
foreach (var file in fs.Root.GetFiles())
    Console.WriteLine(file.FullName);
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
