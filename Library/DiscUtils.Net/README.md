# LTRData.DiscUtils.Net

.NET DNS, multicast DNS and DNS service-discovery helpers used by DiscUtils network integrations.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Net
```

## Usage notes

Provides DNS record types, unicast and multicast clients, and ServiceDiscoveryClient for DNS-SD. Used by OpticalDiscSharing; it is not the iSCSI or NFS implementation. No DiscUtils format registration is needed.

## Example

```csharp
using System;
using DiscUtils.Net.Dns;

using var discovery = new ServiceDiscoveryClient();
foreach (var service in discovery.LookupServiceTypes())
    Console.WriteLine(service);
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
