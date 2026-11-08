# LTRData.DiscUtils.Streams

.NET sparse-stream, buffer, extent, caching and stream-building primitives used by DiscUtils storage implementations.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Streams
```

## Usage notes

Use this package directly when you need sparse memory streams, stream composition, allocation extents, buffers or stream builders without depending on Core or a filesystem implementation. Ownership controls whether wrappers dispose the streams they wrap. No format registration is needed.

### Storage flush requests

`SparseStream.Flush(bool flushToDisk)` is a virtual overload for requesting a stronger storage flush. By default it calls `Flush()`. Built-in forwarding wrappers pass this request through nested sparse streams; `SparseStream.FromStream` uses `FileStream.Flush(bool)` when wrapping a `FileStream` and ordinary `Stream.Flush()` for unknown streams. Custom image or remote-storage streams can override this overload to implement an appropriate durability barrier.

A successful call only establishes what the underlying runtime and storage actually guarantee; it cannot by itself promise power-loss durability or atomic writes. NTFS journal recovery uses `Flush(true)` for its write-ordering barriers. See [NTFS replay durability](../../docs/ntfs-journal-replay-durability.md).

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

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
