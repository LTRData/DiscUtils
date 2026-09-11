# LTRData.DiscUtils

Broad DiscUtils meta-package for managed .NET disk images, filesystems, storage protocols and related storage formats.

Part of the [LTRData DiscUtils fork](https://github.com/LTRData/DiscUtils). Package IDs use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

## Installation

```sh
dotnet add package LTRData.DiscUtils
```

## Included packages

Direct package dependencies are listed below; their own dependencies are restored transitively. Choose individual packages when you need a smaller set of formats.

| Package | Purpose |
| --- | --- |
| [LTRData.DiscUtils.Btrfs](https://www.nuget.org/packages/LTRData.DiscUtils.Btrfs) | Managed .NET support for reading Btrfs filesystems, including file contents and filesystem metadata. |
| [LTRData.DiscUtils.Core](https://www.nuget.org/packages/LTRData.DiscUtils.Core) | Common .NET disk, filesystem, partition and volume abstractions for DiscUtils, including raw disks and format-discovery infrastructure. |
| [LTRData.DiscUtils.BootConfig](https://www.nuget.org/packages/LTRData.DiscUtils.BootConfig) | Managed .NET support for reading, creating and modifying Windows Boot Configuration Data (BCD) stores in Registry hives. |
| [LTRData.DiscUtils.Dmg](https://www.nuget.org/packages/LTRData.DiscUtils.Dmg) | .NET support for reading Apple DMG/UDIF disk images and decompressing supported image data. |
| [LTRData.DiscUtils.ExFat](https://www.nuget.org/packages/LTRData.DiscUtils.ExFat) | Managed .NET implementation for reading, formatting and modifying exFAT filesystems. |
| [LTRData.DiscUtils.Ext](https://www.nuget.org/packages/LTRData.DiscUtils.Ext) | Managed .NET support for reading Ext2, Ext3 and Ext4 filesystems, including file contents and filesystem metadata. |
| [LTRData.DiscUtils.Fat](https://www.nuget.org/packages/LTRData.DiscUtils.Fat) | Managed .NET implementation for reading, formatting and modifying FAT12, FAT16 and FAT32 filesystems. |
| [LTRData.DiscUtils.HfsPlus](https://www.nuget.org/packages/LTRData.DiscUtils.HfsPlus) | Managed .NET support for reading HFS+ filesystems, including file contents and filesystem metadata. |
| [LTRData.DiscUtils.Iscsi](https://www.nuget.org/packages/LTRData.DiscUtils.Iscsi) | Managed .NET iSCSI initiator for target discovery, sessions and read/write access to remote block devices through DiscUtils. |
| [LTRData.DiscUtils.Iso9660](https://www.nuget.org/packages/LTRData.DiscUtils.Iso9660) | Managed .NET support for reading ISO 9660 optical-disc filesystems and building ISO images with Joliet and boot-image support. |
| [LTRData.DiscUtils.Lvm](https://www.nuget.org/packages/LTRData.DiscUtils.Lvm) | Managed .NET discovery and stream mapping for supported Linux LVM logical volumes and Linux MD RAID 1 members. |
| [LTRData.DiscUtils.Nfs](https://www.nuget.org/packages/LTRData.DiscUtils.Nfs) | Managed .NET NFS v3 client for remote file and directory operations and NFS-backed DiscUtils disk access. |
| [LTRData.DiscUtils.Ntfs](https://www.nuget.org/packages/LTRData.DiscUtils.Ntfs) | Managed .NET implementation for reading, formatting and modifying NTFS filesystems, including NTFS-specific file metadata. |
| [LTRData.DiscUtils.OpticalDisk](https://www.nuget.org/packages/LTRData.DiscUtils.OpticalDisk) | Managed .NET optical-disc image access and automatic detection of ISO 9660 and UDF filesystems. |
| [LTRData.DiscUtils.Registry](https://www.nuget.org/packages/LTRData.DiscUtils.Registry) | Managed .NET support for reading, creating and modifying Windows Registry hive files and applying pending registry transaction-log changes. |
| [LTRData.DiscUtils.Sdi](https://www.nuget.org/packages/LTRData.DiscUtils.Sdi) | Managed .NET reader for Microsoft Simple Deployment Image (SDI) headers, section metadata and section content streams. |
| [LTRData.DiscUtils.SquashFs](https://www.nuget.org/packages/LTRData.DiscUtils.SquashFs) | Managed .NET support for reading SquashFS filesystems and building new compressed SquashFS images. |
| [LTRData.DiscUtils.Swap](https://www.nuget.org/packages/LTRData.DiscUtils.Swap) | Managed .NET detection and header inspection for Linux swap areas through DiscUtils filesystem discovery. |
| [LTRData.DiscUtils.Udf](https://www.nuget.org/packages/LTRData.DiscUtils.Udf) | Managed .NET reader for UDF optical-disc filesystems and their files, directories and extended attributes. |
| [LTRData.DiscUtils.Vdi](https://www.nuget.org/packages/LTRData.DiscUtils.Vdi) | Managed .NET support for reading, creating and modifying fixed and dynamically allocated VirtualBox VDI disk images. |
| [LTRData.DiscUtils.Vhd](https://www.nuget.org/packages/LTRData.DiscUtils.Vhd) | Managed .NET support for reading, creating and modifying fixed, dynamic and differencing Microsoft VHD virtual disk images. |
| [LTRData.DiscUtils.Vhdx](https://www.nuget.org/packages/LTRData.DiscUtils.Vhdx) | Managed .NET support for reading, creating and modifying fixed, dynamic and differencing Microsoft VHDX virtual disk images. |
| [LTRData.DiscUtils.VirtualFileSystem](https://www.nuget.org/packages/LTRData.DiscUtils.VirtualFileSystem) | Composable .NET virtual filesystem trees, TAR and ZIP filesystem readers, and a TAR image builder through DiscUtils abstractions. |
| [LTRData.DiscUtils.Vmdk](https://www.nuget.org/packages/LTRData.DiscUtils.Vmdk) | Managed .NET support for reading, creating and modifying supported VMware VMDK layouts, including differencing disk chains. |
| [LTRData.DiscUtils.Wim](https://www.nuget.org/packages/LTRData.DiscUtils.Wim) | Managed .NET support for reading Windows Imaging Format (WIM) containers and accessing their image filesystems and metadata. |
| [LTRData.DiscUtils.Xfs](https://www.nuget.org/packages/LTRData.DiscUtils.Xfs) | Managed .NET support for reading XFS filesystems, including file contents and filesystem metadata. |
| [LTRData.DiscUtils.Xva](https://www.nuget.org/packages/LTRData.DiscUtils.Xva) | Managed .NET support for reading Xen Virtual Appliance (XVA) disks and creating new XVA appliances from disk streams. |
| [LTRData.DiscUtils.Net](https://www.nuget.org/packages/LTRData.DiscUtils.Net) | .NET DNS, multicast DNS and DNS service-discovery helpers used by DiscUtils network integrations. |
| [LTRData.DiscUtils.OpticalDiscSharing](https://www.nuget.org/packages/LTRData.DiscUtils.OpticalDiscSharing) | .NET client for discovering and reading optical media shared through Apple Optical Disc Sharing. |

## Registration

```csharp
DiscUtils.Complete.SetupHelper.SetupComplete();
```

This helper explicitly registers providers from: `Core`, `Dmg`, `Btrfs`, `Ext`, `Fat`, `ExFat`, `HfsPlus`, `Iscsi`, `Nfs`, `Ntfs`, `OpticalDiscSharing`, `OpticalDisk`, `SquashFs`, `Swap`, `Vdi`, `Vhd`, `Vhdx`, `Vmdk`, `VirtualFileSystem`, `Xfs`, `Xva`, `Lvm`. It is safe to repeat and supports trimming and Native AOT registration. Referencing the package alone does not register providers.

Package dependencies and registration scope are distinct. Libraries without discovery providers, such as WIM and Registry, are used through their direct APIs. ISO/UDF detection is supplied by OpticalDisk.

Dokan/FUSE integrations and command-line utilities are not included. Registration compatibility does not establish Native AOT support for every operation in every dependency.

## Related packages

- [LTRData.DiscUtils.Containers](https://www.nuget.org/packages/LTRData.DiscUtils.Containers)
- [LTRData.DiscUtils.FileSystems](https://www.nuget.org/packages/LTRData.DiscUtils.FileSystems)
- [LTRData.DiscUtils.Transports](https://www.nuget.org/packages/LTRData.DiscUtils.Transports)

## Documentation

[Repository and capability matrix](https://github.com/LTRData/DiscUtils) · [Wiki](https://github.com/LTRData/DiscUtils/wiki) · [Migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) · [Format registration and Native AOT](https://github.com/LTRData/DiscUtils/blob/LTRData.DiscUtils-initial/docs/native-aot-format-registration.md)
