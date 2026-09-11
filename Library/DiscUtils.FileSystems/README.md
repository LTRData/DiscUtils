# LTRData.DiscUtils.FileSystems

DiscUtils meta-package for filesystem readers, supported filesystem writers and virtual filesystem abstractions.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.FileSystems
```

## Included packages

Direct package dependencies are listed below; their own dependencies are restored transitively. Choose individual packages when you need a smaller set of formats.

| Package | Purpose |
| --- | --- |
| [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core) | Common .NET disk, filesystem, partition and volume abstractions for DiscUtils, including raw disks and format-discovery infrastructure. |
| [LTRData.DiscUtils.Btrfs](https://www.nuget.org/packages/LTRData.DiscUtils.Btrfs) | Managed .NET support for reading Btrfs filesystems, including file contents and filesystem metadata. |
| [LTRData.DiscUtils.ExFat](https://www.nuget.org/packages/LTRData.DiscUtils.ExFat) | Managed .NET implementation for reading, formatting and modifying exFAT filesystems. |
| [LTRData.DiscUtils.Ext](https://www.nuget.org/packages/LTRData.DiscUtils.Ext) | Managed .NET support for reading Ext2, Ext3 and Ext4 filesystems, including file contents and filesystem metadata. |
| [LTRData.DiscUtils.Fat](https://www.nuget.org/packages/LTRData.DiscUtils.Fat) | Managed .NET implementation for reading, formatting and modifying FAT12, FAT16 and FAT32 filesystems. |
| [LTRData.DiscUtils.HfsPlus](https://www.nuget.org/packages/LTRData.DiscUtils.HfsPlus) | Managed .NET support for reading HFS+ filesystems, including file contents and filesystem metadata. |
| [LTRData.DiscUtils.Ntfs](https://www.nuget.org/packages/LTRData.DiscUtils.Ntfs) | Managed .NET implementation for reading, formatting and modifying NTFS filesystems, including NTFS-specific file metadata. |
| [LTRData.DiscUtils.OpticalDisk](https://www.nuget.org/packages/LTRData.DiscUtils.OpticalDisk) | Managed .NET optical-disc image access and automatic detection of ISO 9660 and UDF filesystems. |
| [LTRData.DiscUtils.SquashFs](https://www.nuget.org/packages/LTRData.DiscUtils.SquashFs) | Managed .NET support for reading SquashFS filesystems and building new compressed SquashFS images. |
| [LTRData.DiscUtils.Swap](https://www.nuget.org/packages/LTRData.DiscUtils.Swap) | Managed .NET detection and header inspection for Linux swap areas through DiscUtils filesystem discovery. |
| [LTRData.DiscUtils.VirtualFileSystem](https://www.nuget.org/packages/LTRData.DiscUtils.VirtualFileSystem) | Composable .NET virtual filesystem trees, TAR and ZIP filesystem readers, and a TAR image builder through DiscUtils abstractions. |
| [LTRData.DiscUtils.Xfs](https://www.nuget.org/packages/LTRData.DiscUtils.Xfs) | Managed .NET support for reading XFS filesystems, including file contents and filesystem metadata. |

## Registration

```csharp
DiscUtils.FileSystems.SetupHelper.SetupFileSystems();
```

This helper explicitly registers providers from: `Core`, `Btrfs`, `Ext`, `Fat`, `ExFat`, `HfsPlus`, `Ntfs`, `OpticalDisk`, `SquashFs`, `Swap`, `Xfs`, `VirtualFileSystem`. It is safe to repeat and supports trimming and Native AOT registration. Referencing the package alone does not register providers.

Package dependencies and registration scope are distinct. Libraries without discovery providers, such as WIM and Registry, are used through their direct APIs. ISO/UDF detection is supplied by OpticalDisk.

ISO 9660 and UDF arrive transitively through OpticalDisk. This package also includes the limited Linux swap metadata reader. It does not include virtual disk container packages such as VHD or VHDX.

Dokan/FUSE integrations and command-line utilities are not included. Registration compatibility does not establish Native AOT support for every operation in every dependency.

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.Containers](https://www.nuget.org/packages/LTRData.DiscUtils.Containers)
- [LTRData.DiscUtils.Transports](https://www.nuget.org/packages/LTRData.DiscUtils.Transports)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
