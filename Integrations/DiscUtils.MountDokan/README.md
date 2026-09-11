# LTRData.DiscUtils.MountDokan

Windows integration for exposing DiscUtils filesystem implementations through the Dokan filesystem driver.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.MountDokan
```

## Requirements and behavior

- Windows with a compatible Dokan filesystem driver installed; the package references DokanNet 2.3.0.3.
- An opened DiscUtils `IFileSystem` and its format package. This adapter does not open or detect disk images itself.
- Read/write access is constrained by the underlying filesystem, its backing stream, Dokan settings and adapter support. `ForceReadOnly` can further restrict access.

## Adapter example

Given an already opened `IFileSystem fileSystem`, create the operations object to pass to a DokanNet mount host:

```csharp
using DiscUtils.MountDokan;

using var operations = new DokanDiscUtils(
    fileSystem,
    DokanDiscUtilsOptions.ForceReadOnly | DokanDiscUtilsOptions.LeaveFSOpen);
// Pass operations to your DokanNet mount host and keep it alive until unmounted.
```

Constructing the adapter does not mount a drive. Configure the mount point and mount lifetime through DokanNet. `LeaveFSOpen` keeps disposal of the filesystem with the caller. Range locking callbacks are not implemented; this adapter does not add capabilities missing from the mounted filesystem.

No adapter format registration is needed. Register storage providers separately only if using DiscUtils automatic detection.

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
