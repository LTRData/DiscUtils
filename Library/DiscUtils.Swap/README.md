# LTRData.DiscUtils.Swap

Managed .NET detection and header inspection for Linux swap areas through DiscUtils filesystem discovery.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Swap
```

## Capabilities

| Capability | Support |
| --- | --- |
| Detect swap signatures | Yes |
| Read swap header / volume label | Yes |
| Format or modify swap areas | No |
| Browse ordinary files / directories | Not applicable |

## Usage notes

SwapFileSystem recognizes SWAP-SPACE and SWAPSPACE2 signatures using a 4096-byte header page. It is a limited metadata view, not a normal directory filesystem or a swap-content recovery tool.

## Registration

For automatic discovery, register the providers implemented by this package:

```csharp
DiscUtils.Swap.Formats.Register();
```

Direct use of the concrete APIs in this package does not require discovery registration. Each registration call covers only its own assembly. Register `DiscUtils.Core.Formats.Register()` as well when using generic disk opening through the local-file transport, and register other disk/filesystem providers as needed. These registration calls support trimming and Native AOT; this is not a guarantee for every operation in the package.

## Example

```csharp
using System;
using System.IO;
using DiscUtils.Swap;

using var stream = File.OpenRead("swap.img");
Console.WriteLine(SwapFileSystem.Detect(stream));
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
