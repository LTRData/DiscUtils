# LTRData.DiscUtils.OpticalDiscSharing

.NET client for discovering and reading optical media shared through Apple Optical Disc Sharing.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.OpticalDiscSharing
```

## Capabilities

| Capability | Support |
| --- | --- |
| Discover shared optical media | Yes, using DNS service discovery |
| Read shared disc content | Yes |
| Write shared disc content | No |

## Usage notes

Provides OpticalDiscServiceClient and Disc, plus an `ods` transport for remote optical media, with service discovery supplied by LTRData.DiscUtils.Net. A compatible sharing service and any required access approval must be available. This package is not included by the Transports meta-package; Containers references it but does not register its transport.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.OpticalDiscSharing.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using DiscUtils.OpticalDiscSharing;

using var client = new OpticalDiscServiceClient();
foreach (var service in client.LookupServices())
    Console.WriteLine(service.DisplayName);
```

## Related packages

- [LTRData.DiscUtils.Net](https://www.nuget.org/packages/LTRData.DiscUtils.Net)
- [LTRData.DiscUtils.OpticalDisk](https://www.nuget.org/packages/LTRData.DiscUtils.OpticalDisk)
- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
