# LTRData.DiscUtils.MountFuse

FUSE integration for exposing DiscUtils filesystem implementations on platforms supported by LTRData.FuseDotNet.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.MountFuse
```

## Requirements and behavior

- A FUSE environment supported by [LTRData.FuseDotNet](https://www.nuget.org/packages/LTRData.FuseDotNet), including its native FUSE dependency and mount permissions.
- An opened DiscUtils `IFileSystem` and its format package. Framework compatibility alone does not establish native FUSE support on a particular operating system.
- File writes require a writable filesystem and backing stream, supported adapter operations and suitable mount permissions.

## Adapter example

Given an already opened `IFileSystem fileSystem`, create the operations object to pass to a FuseDotNet mount host:

```csharp
using DiscUtils.MountFuse;

using var operations = new FuseDiscUtils(fileSystem, FuseDiscUtilsOptions.None);
// Pass operations to your FuseDotNet mount host and keep it alive until unmounted.
```

Constructing the adapter does not mount a filesystem. Configure the mount point and lifetime through FuseDotNet and keep the underlying filesystem/streams alive until unmounted. `AccessCheck` currently throws `NotImplementedException`; several callbacks, including link creation, are not implemented. Filesystem capabilities are an upper bound on what the mounted view can expose.

No adapter format registration is needed. Register storage providers separately only if using DiscUtils automatic detection. This package omits the net46 target used by many other DiscUtils projects.

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
