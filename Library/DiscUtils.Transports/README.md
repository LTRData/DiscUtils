# LTRData.DiscUtils.Transports

DiscUtils meta-package for iSCSI and NFS access, local-file transport and optical-disc detection.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Transports
```

## Included packages

Direct package dependencies are listed below; their own dependencies are restored transitively. Choose individual packages when you need a smaller set of formats.

| Package | Purpose |
| --- | --- |
| [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core) | Common .NET disk, filesystem, partition and volume abstractions for DiscUtils, including raw disks and format-discovery infrastructure. |
| [LTRData.DiscUtils.Iscsi](https://www.nuget.org/packages/LTRData.DiscUtils.Iscsi) | Managed .NET iSCSI initiator for target discovery, sessions and read/write access to remote block devices through DiscUtils. |
| [LTRData.DiscUtils.Nfs](https://www.nuget.org/packages/LTRData.DiscUtils.Nfs) | Managed .NET NFS v3 client for remote file and directory operations and NFS-backed DiscUtils disk access. |
| [LTRData.DiscUtils.OpticalDisk](https://www.nuget.org/packages/LTRData.DiscUtils.OpticalDisk) | Managed .NET optical-disc image access and automatic detection of ISO 9660 and UDF filesystems. |

## Registration

```csharp
DiscUtils.Transports.SetupHelper.SetupTransports();
```

This helper explicitly registers providers from: `Core`, `Iscsi`, `Nfs`, `OpticalDisk`. It is safe to repeat and supports trimming and Native AOT registration. Referencing the package alone does not register providers.

Package dependencies and registration scope are distinct. Core supplies the local-file transport, Iscsi and Nfs supply their network transports, and OpticalDisk supplies optical-disc image access and ISO/UDF detection.

OpticalDisk brings ISO 9660 and UDF readers transitively. Apple Optical Disc Sharing is a separate package and is not included. Register the corresponding format providers separately when opening disk-image files over a transport.

Dokan/FUSE integrations and command-line utilities are not included. Registration compatibility does not establish Native AOT support for every operation in every dependency.

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Containers](https://www.nuget.org/packages/LTRData.DiscUtils.Containers)
- [LTRData.DiscUtils.FileSystems](https://www.nuget.org/packages/LTRData.DiscUtils.FileSystems)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/HEAD/docs/native-aot-format-registration.md)
