# LTRData.DiscUtils.Iscsi

Managed .NET iSCSI initiator for target discovery, sessions and read/write access to remote block devices through DiscUtils.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Iscsi
```

## Capabilities

| Capability | Support |
| --- | --- |
| Discover targets and LUNs | Yes |
| Read remote block devices | Yes |
| Write remote block devices | Yes, subject to target permissions |

## Usage notes

Initiator and Session provide explicit connections and LUN access. The package also supplies an iscsi transport for generic disk opening. This is a client library, not an iSCSI server or an operating-system initiator driver. Registration is AOT-compatible; protocol-key reflection elsewhere in iSCSI is outside that guarantee.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Iscsi.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using DiscUtils.Iscsi;

var initiator = new Initiator();
foreach (var target in initiator.GetTargets("192.0.2.10"))
    Console.WriteLine(target);
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
