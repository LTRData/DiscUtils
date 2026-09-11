# LTRData.DiscUtils.Containers

DiscUtils meta-package for disk and image containers, with Linux volume mapping and related image readers.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils.Containers
```

## Included packages

Direct package dependencies are listed below; their own dependencies are restored transitively. Choose individual packages when you need a smaller set of formats.

| Package | Purpose |
| --- | --- |
| [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core) | Common .NET disk, filesystem, partition and volume abstractions for DiscUtils, including raw disks and format-discovery infrastructure. |
| [LTRData.DiscUtils.Dmg](https://www.nuget.org/packages/LTRData.DiscUtils.Dmg) | .NET support for reading Apple DMG/UDIF disk images and decompressing supported image data. |
| [LTRData.DiscUtils.Iso9660](https://www.nuget.org/packages/LTRData.DiscUtils.Iso9660) | Managed .NET support for reading ISO 9660 optical-disc filesystems and building ISO images with Joliet and boot-image support. |
| [LTRData.DiscUtils.Lvm](https://www.nuget.org/packages/LTRData.DiscUtils.Lvm) | Managed .NET discovery and stream mapping for supported Linux LVM logical volumes and Linux MD RAID 1 members. |
| [LTRData.DiscUtils.Vhd](https://www.nuget.org/packages/LTRData.DiscUtils.Vhd) | Managed .NET support for reading, creating and modifying fixed, dynamic and differencing Microsoft VHD virtual disk images. |
| [LTRData.DiscUtils.Vhdx](https://www.nuget.org/packages/LTRData.DiscUtils.Vhdx) | Managed .NET support for reading, creating and modifying fixed, dynamic and differencing Microsoft VHDX virtual disk images. |
| [LTRData.DiscUtils.Vmdk](https://www.nuget.org/packages/LTRData.DiscUtils.Vmdk) | Managed .NET support for reading, creating and modifying supported VMware VMDK layouts, including differencing disk chains. |
| [LTRData.DiscUtils.Vdi](https://www.nuget.org/packages/LTRData.DiscUtils.Vdi) | Managed .NET support for reading, creating and modifying fixed and dynamically allocated VirtualBox VDI disk images. |
| [LTRData.DiscUtils.Wim](https://www.nuget.org/packages/LTRData.DiscUtils.Wim) | Managed .NET support for reading Windows Imaging Format (WIM) containers and accessing their image filesystems and metadata. |
| [LTRData.DiscUtils.Xva](https://www.nuget.org/packages/LTRData.DiscUtils.Xva) | Managed .NET support for reading Xen Virtual Appliance (XVA) disks and creating new XVA appliances from disk streams. |
| [LTRData.DiscUtils.OpticalDiscSharing](https://www.nuget.org/packages/LTRData.DiscUtils.OpticalDiscSharing) | .NET client for discovering and reading optical media shared through Apple Optical Disc Sharing. |

## Registration

```csharp
DiscUtils.Containers.SetupHelper.SetupContainers();
```

This helper explicitly registers providers from: `Core`, `Dmg`, `Lvm`, `Vhd`, `Vhdx`, `Vmdk`, `Vdi`, `Xva`. It is safe to repeat and supports trimming and Native AOT registration. Referencing the package alone does not register providers.

Package dependencies and registration scope are distinct. Libraries without discovery providers, such as WIM and Registry, are used through their direct APIs. ISO/UDF detection is supplied by OpticalDisk.

OpticalDiscSharing is a dependency, but SetupContainers does not register its transport. ISO 9660 and WIM readers are included for direct use; automatic ISO/UDF detection requires adding and registering OpticalDisk. SDI is not included.

Dokan/FUSE integrations and command-line utilities are not included. Registration compatibility does not establish Native AOT support for every operation in every dependency.

## Related packages

- [LTRData.DiscUtils](https://www.nuget.org/packages/LTRData.DiscUtils)
- [LTRData.DiscUtils.FileSystems](https://www.nuget.org/packages/LTRData.DiscUtils.FileSystems)
- [LTRData.DiscUtils.Transports](https://www.nuget.org/packages/LTRData.DiscUtils.Transports)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
