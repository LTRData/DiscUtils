# LTRData.DiscUtils.Registry

Managed .NET support for reading, creating and modifying Windows Registry hive files and applying pending registry transaction-log changes.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Registry
```

## Capabilities

| Capability | Support |
| --- | --- |
| Read offline Registry hives | Yes |
| Create hives | Yes |
| Modify keys and values | Yes |
| Apply pending log changes | Yes, for supported registry logs |

## Usage notes

This package edits hive files through streams; it does not access the live Windows Registry through Win32. RegistryHive constructors accept associated log streams, and path-based opening can locate logs. Recovery behavior depends on hive/log state and stream access. No format registration is required.

## Example

```csharp
using System.IO;
using DiscUtils.Registry;
using DiscUtils.Streams;

using var stream = File.Create("settings.hive");
using var hive = RegistryHive.Create(stream, Ownership.None);
var key = hive.Root.CreateSubKey(@"Software\Example");
key.SetValue("Greeting", "Hello from DiscUtils!");
```

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
