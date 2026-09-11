# LTRData.DiscUtils.Streams

.NET sparse-stream, buffer, extent, caching and stream-building primitives used by DiscUtils storage implementations.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Streams
```

## Usage notes

Use this package directly when you need sparse memory streams, stream composition, allocation extents, buffers or stream builders without depending on Core or a filesystem implementation. Ownership controls whether wrappers dispose the streams they wrap. No format registration is needed.

## Example

```csharp
using System;
using DiscUtils.Streams;

using var stream = new SparseMemoryStream();
stream.SetLength(1024L * 1024 * 1024);
stream.Position = stream.Length - 1;
stream.WriteByte(42);
foreach (var extent in stream.Extents)
    Console.WriteLine($"{extent.Start}: {extent.Length}");
```

## Related packages

- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)
- [LTRData.DiscUtils.VirtualFileSystem](https://www.nuget.org/packages/LTRData.DiscUtils.VirtualFileSystem)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
