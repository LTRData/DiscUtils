# LTRData.DiscUtils.BootConfig

Managed .NET support for reading, creating and modifying Windows Boot Configuration Data (BCD) stores in Registry hives.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.BootConfig
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read BCD objects and elements | Yes |
| Initialize a BCD store | Yes |
| Create / modify / remove BCD objects | Yes |

## Usage notes

Store wraps a DiscUtils.Registry.RegistryKey. It supports application, device and inherited settings objects. Initializing an empty store does not by itself create a bootable Windows configuration. No format registration is required.

## Example

```csharp
using System.IO;
using DiscUtils.BootConfig;
using DiscUtils.Registry;
using DiscUtils.Streams;

using var stream = File.Create("new-bcd.hive");
using var hive = RegistryHive.Create(stream, Ownership.None);
var store = Store.Initialize(hive.Root);
// Populate the objects and elements required by your boot configuration.
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
