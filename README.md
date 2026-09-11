# DiscUtils

DiscUtils is a modular .NET library for working with disk images, virtual disks, filesystems, storage protocols and related storage formats.

It provides APIs for opening, inspecting, creating and, where supported, modifying storage images without mounting them through the operating system. Applications can work with complete disks, partitions, filesystems, files and directories using .NET streams and common DiscUtils abstractions.

The core storage implementations are written in C#. Particular codec dependencies can have additional runtime requirements. The repository also contains optional platform integrations and utilities that use operating-system-specific facilities, including Dokan, FUSE and Windows VSS.

## This fork

[LTRData.DiscUtils](https://github.com/LTRData/DiscUtils) is an actively maintained fork of [DiscUtils/DiscUtils](https://github.com/DiscUtils/DiscUtils), descended through [quamotion/DiscUtils](https://github.com/quamotion/DiscUtils) from the original CodePlex project.

This fork focuses on modernizing and optimizing the codebase, improving cross-platform compatibility and making use of current .NET APIs. This includes `Span<T>` and memory-oriented APIs, asynchronous operations in many I/O paths, allocation reductions and lazy enumeration APIs. Some APIs intentionally differ from older DiscUtils releases; see the [migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils).

Most library projects currently target .NET Framework 4.6 and 4.8, .NET Standard 2.0 and 2.1, and .NET 8, 9 and 10. Targets are maintained in [Library/Directory.Build.props](Library/Directory.Build.props); integrations and utilities can use a subset. A compatible target framework does not remove platform requirements of native dependencies or utilities.

## Packages

DiscUtils is organized into focused packages so applications can reference the storage formats and functionality they need. Packages share common infrastructure and restore their dependencies transitively. NuGet package names use `LTRData.DiscUtils`; C# namespaces remain `DiscUtils`.

| Package | Purpose |
| --- | --- |
| [LTRData.DiscUtils](Library/DiscUtils/README.md) | Broad library meta-package covering disk images, filesystems, protocols and related storage formats |
| [LTRData.DiscUtils.Containers](Library/DiscUtils.Containers/README.md) | Disk/image container packages and Linux volume mapping |
| [LTRData.DiscUtils.FileSystems](Library/DiscUtils.FileSystems/README.md) | Filesystem implementations, including ISO/UDF through OpticalDisk |
| [LTRData.DiscUtils.Transports](Library/DiscUtils.Transports/README.md) | iSCSI, NFS, local-file transport and optical-disc detection |
| [LTRData.DiscUtils.Core](Library/DiscUtils.Core/README.md) | Common disk/filesystem abstractions, RAW disks, partitioning, volumes and discovery infrastructure |
| [LTRData.DiscUtils.Streams](Library/DiscUtils.Streams/README.md) | Sparse streams, buffers, allocation extents and stream composition |
| [LTRData.DiscUtils.VirtualFileSystem](Library/DiscUtils.VirtualFileSystem/README.md) | Composable filesystem trees, TAR/ZIP readers and a TAR builder |
| [LTRData.DiscUtils.Net](Library/DiscUtils.Net/README.md) | DNS, multicast DNS and service discovery |

Each package directory has its own README with capabilities, usage notes and installation instructions. The meta-package READMEs list their exact direct dependencies and registration scope. Individual format packages are linked in the tables below and available on [NuGet](https://www.nuget.org/packages?q=LTRData.DiscUtils).

Platform integrations are separate packages and are not included by the main library meta-package. Utilities are applications in this repository, not part of those NuGet packages.

## Capability matrix

These tables describe implemented API capabilities, not maturity ratings or support for every feature of a format.

- **Read**: open an existing instance and access the indicated content or metadata.
- **Create**: build a new image, filesystem or structured file.
- **Modify**: change an existing instance through format-aware APIs. For virtual disks this includes writing virtual sector content; it does not imply arbitrary metadata editing, resizing or chain merging.

Creation and modification are distinct: ISO, SquashFS and XVA can be built as new images while existing images remain read-only. Writable operations also require appropriate backing-stream access. Raw byte writes forwarded by a wrapper do not establish structured format-editing support.

### Filesystems

Filesystem constructors normally expect a stream beginning at the filesystem, such as a partition's content stream, rather than a whole partitioned disk image.

| Filesystem | Read | Create | Modify | Notes |
| --- | :---: | :---: | :---: | --- |
| [Btrfs](Library/DiscUtils.Btrfs/README.md) | Yes | No | No | Subvolumes, Unix metadata and allocation extents; Zstandard decompression is unavailable on net46 |
| [exFAT](Library/DiscUtils.ExFat/README.md) | Yes | Yes | Yes | Formatting and file/directory operations |
| [Ext2 / Ext3 / Ext4](Library/DiscUtils.Ext/README.md) | Yes | No | No | Unix metadata and file-to-cluster/extent mapping |
| [FAT12 / FAT16 / FAT32](Library/DiscUtils.Fat/README.md) | Yes | Yes | Yes | Includes floppy and partition formatting |
| [HFS+](Library/DiscUtils.HfsPlus/README.md) | Yes | No | No | Unix metadata and allocation extents; not APFS |
| [ISO 9660 / Joliet](Library/DiscUtils.Iso9660/README.md) | Yes | Yes | No | CDReader also supports Rock Ridge; CDBuilder builds new images, including bootable-image scenarios |
| [NTFS](Library/DiscUtils.Ntfs/README.md) | Yes | Yes | Yes | Includes alternate data streams, security descriptors, reparse points and allocation metadata |
| [SquashFS](Library/DiscUtils.SquashFs/README.md) | Yes | Yes | No | Separate reader and builder; zlib included, other codecs require callbacks |
| [UDF](Library/DiscUtils.Udf/README.md) | Yes | No | No | Size/used/free-space properties are not implemented |
| [XFS](Library/DiscUtils.Xfs/README.md) | Yes | No | No | Unix metadata and allocation extents |

### Archive filesystem views

These adapters expose archive entries through the common filesystem API.

| Format | Read | Create | Modify existing | Notes |
| --- | :---: | :---: | :---: | --- |
| [TAR](Library/DiscUtils.VirtualFileSystem/README.md) | Yes | Yes | No | TarFileSystem reads archives; TarFileSystemBuilder creates new ones; the filesystem view skips symbolic links |
| [ZIP](Library/DiscUtils.VirtualFileSystem/README.md) | Yes | No | No | ZipFileSystem provides a read-only view; opened file content is decompressed into memory |

### Disk and image formats

| Format | Read | Create | Modify | Notes |
| --- | :---: | :---: | :---: | --- |
| [RAW](Library/DiscUtils.Core/README.md) | Yes | Yes | Yes | Unstructured virtual sector data; filesystem support is separate |
| [DMG / UDIF](Library/DiscUtils.Dmg/README.md) | Yes | No | No | Read-only UDIF content, including supported compressed data; decoder dependencies vary by target framework |
| [SDI](Library/DiscUtils.Sdi/README.md) | Yes | No | No | Section/blob inspection; section streams may forward raw writes, but no structured image writer |
| [VDI](Library/DiscUtils.Vdi/README.md) | Yes | Yes | Yes | Fixed and dynamic images; differencing creation is not implemented |
| [VHD](Library/DiscUtils.Vhd/README.md) | Yes | Yes | Yes | Fixed, dynamic and differencing disks; parent chains |
| [VHDX](Library/DiscUtils.Vhdx/README.md) | Yes | Yes | Yes | Fixed, dynamic and differencing disks; parent chains |
| [VMDK](Library/DiscUtils.Vmdk/README.md) | Yes | Yes | Yes | Supported layouts and differencing disks; compressed hosted-sparse extents are read-only, single-stream opening has restrictions |
| [WIM](Library/DiscUtils.Wim/README.md) | Yes | No | No | Container with image filesystems, rather than virtual sectors; XPRESS/LZX resources |
| [XVA](Library/DiscUtils.Xva/README.md) | Yes | Yes | No | Existing disks are read-only; VirtualMachineBuilder creates appliances from disk streams |

[OpticalDisk](Library/DiscUtils.OpticalDisk/README.md) also exposes optical-disc sector data as a read-only virtual disk and supplies automatic ISO/UDF filesystem detection. It does not burn physical media.

### Volume and partition support

For this table, Modify means partition/volume **metadata** management. Mapped content streams can forward writes when their backing storage is writable; use read-only source streams for inspection.

| Format / component | Read | Create | Modify | Notes |
| --- | :---: | :---: | :---: | --- |
| [BIOS / MBR partition tables](Library/DiscUtils.Core/README.md) | Yes | Yes | Yes | Discovery, initialization, partition creation and deletion |
| [GPT partition tables](Library/DiscUtils.Core/README.md) | Yes | Yes | Yes | Discovery, initialization, partition creation and deletion |
| [Linux LVM](Library/DiscUtils.Lvm/README.md) | Yes | No | No | Single-stripe (linear) segments; all referenced physical volumes must be available |
| [Linux MD RAID](Library/DiscUtils.Lvm/README.md) | Yes | No | No | Current discovery is limited to RAID 1 partition members; not an array-management tool |
| [Linux swap](Library/DiscUtils.Swap/README.md) | Header | No | No | Signature and header metadata inspection, not an ordinary file/directory filesystem |

Core also includes Windows dynamic-volume discovery and stream mapping for supported layouts.

### Network storage and filesystems

Write access depends on the server, export permissions, credentials and requested access.

| Protocol | Read | Write | Notes |
| --- | :---: | :---: | --- |
| [iSCSI](Library/DiscUtils.Iscsi/README.md) | Yes | Yes | Target/LUN discovery and remote block access through virtual disks |
| [NFS](Library/DiscUtils.Nfs/README.md) | Yes | Yes | NFS v3 client with file/directory operations and transport support |
| [Apple Optical Disc Sharing](Library/DiscUtils.OpticalDiscSharing/README.md) | Yes | No | Remote shared optical media and service discovery |

### Windows structured data

These APIs operate on stored data and do not require mounting the hive in the live Windows Registry.

| Format | Read | Create | Modify | Notes |
| --- | :---: | :---: | :---: | --- |
| [Registry hives](Library/DiscUtils.Registry/README.md) | Yes | Yes | Yes | Keys/values and pending changes from supported Registry transaction logs |
| [Boot Configuration Data](Library/DiscUtils.BootConfig/README.md) | Yes | Yes | Yes | BCD objects/elements using the Registry hive implementation |

## Integrations

Optional mounting adapters live under [Integrations](Integrations). They require external platform facilities in addition to the NuGet package.

| Package | Platform | Purpose |
| --- | --- | --- |
| [LTRData.DiscUtils.MountDokan](Integrations/DiscUtils.MountDokan/README.md) | Windows with Dokan | Exposes DiscUtils IFileSystem implementations through the Dokan driver |
| [LTRData.DiscUtils.MountFuse](Integrations/DiscUtils.MountFuse/README.md) | FUSE environments supported by LTRData.FuseDotNet | Exposes DiscUtils IFileSystem implementations through FUSE |

The mounted view is constrained by the underlying filesystem, backing-stream access, mount settings and implemented adapter callbacks. An adapter does not make a read-only filesystem writable. See each package README for prerequisites and limitations.

## Utilities

[Utilities](Utilities) contains applications for image creation, conversion, extraction, inspection and diagnostics. They may have additional platform requirements beyond the libraries.

| Utility | Purpose | Requirements / scope |
| --- | --- | --- |
| [DiskClone](Utilities/DiskClone) | Clone a live physical Windows disk or selected NTFS volumes into a virtual disk image | Windows, administrator privileges and VSS; NTFS source volumes on conventional BIOS/MBR-partitioned disks |
| [VirtualDiskConvert](Utilities/VirtualDiskConvert) | Convert virtual disks between supported input/output formats | Output is limited to formats/layouts with builders |
| [DiskDump](Utilities/DiskDump) | Inspect disks, partitioning and detected filesystems | Depends on the selected format and transport |
| [FileExtract](Utilities/FileExtract) | Extract files from supported disk/filesystem images | Depends on the selected format and transport |
| [ISOCreate](Utilities/ISOCreate) | Build an ISO image from source files | Uses the ISO 9660 builder |

The source tree contains further tools, including BCD, NTFS, VHD and VHDX diagnostics. Consult each utility's help for arguments and restrictions.

## Format registration and Native AOT

Referencing a format package does not register its providers with automatic discovery. Register the providers your application needs during startup:

```csharp
DiscUtils.Core.Formats.Register(); // RAW disks, local-file transport and Core providers
DiscUtils.Vhd.Formats.Register();
DiscUtils.Fat.Formats.Register();
```

Each call covers only its own assembly, not its dependencies. Direct use of concrete APIs, such as constructing CDReader or initializing a VHD, does not require automatic discovery. ISO/UDF detection is registered through `DiscUtils.OpticalDisk.Formats.Register()`; Iso9660 and Udf do not have their own generated entry points.

Meta-packages provide composition helpers; choose the one appropriate to the packages you reference:

```csharp
DiscUtils.Complete.SetupHelper.SetupComplete();
DiscUtils.Containers.SetupHelper.SetupContainers();
DiscUtils.FileSystems.SetupHelper.SetupFileSystems();
DiscUtils.Transports.SetupHelper.SetupTransports();
```

These explicit registration calls are repeatable and compatible with trimming and Native AOT. That guarantee applies to registration, not every operation in every library or integration. No application-side generator or module initializer is required.

Reflection-based registration remains available for dynamic plugin discovery on suitable runtimes:

```csharp
DiscUtils.Setup.SetupHelper.RegisterAssembly(assembly);
```

See [format registration and Native AOT](docs/native-aot-format-registration.md) for registration scope, third-party providers and remaining limitations.

## Examples

Examples use modern C# syntax and local image files. Reference the packages listed for each example; their dependencies supply the shared abstractions. Keep backing streams alive while their filesystem or disk objects are in use.

### Create an ISO

Package: [LTRData.DiscUtils.Iso9660](Library/DiscUtils.Iso9660/README.md).

```csharp
using System.Text;
using DiscUtils.Iso9660;

var builder = new CDBuilder { UseJoliet = true, VolumeIdentifier = "A_SAMPLE_DISK" };
builder.AddFile(@"Folder\Hello.txt", Encoding.UTF8.GetBytes("Hello World!"));
builder.Build("sample.iso");
```

Files can come from byte arrays, local paths or streams. Build overloads can also return or write a stream.

### Extract a file from an ISO

Package: [LTRData.DiscUtils.Iso9660](Library/DiscUtils.Iso9660/README.md).

```csharp
using System.IO;
using DiscUtils.Iso9660;

using var image = File.OpenRead("sample.iso");
using var cd = new CDReader(image, true);
using var file = cd.OpenFile(@"Folder\Hello.txt", FileMode.Open, FileAccess.Read);
using var output = File.Create("Hello.txt");
file.CopyTo(output);
```

Browse the hierarchy through `cd.Root`.

### Create and format a VHD

Packages: [LTRData.DiscUtils.Vhd](Library/DiscUtils.Vhd/README.md) and [LTRData.DiscUtils.Fat](Library/DiscUtils.Fat/README.md).

```csharp
using System.IO;
using DiscUtils;
using DiscUtils.Fat;
using DiscUtils.Partitions;
using DiscUtils.Streams;

using var image = File.Create("mydisk.vhd");
using var disk = DiscUtils.Vhd.Disk.InitializeDynamic(
    image, Ownership.None, 64L * 1024 * 1024);
BiosPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsFat);
using var fs = FatFileSystem.FormatPartition(disk, 0, null);
fs.CreateDirectory(@"TestDir\CHILD");
```

The VHD contains a partition table and a FAT filesystem; browse its files through `fs.Root`.

### Create a virtual floppy

Package: [LTRData.DiscUtils.Fat](Library/DiscUtils.Fat/README.md).

```csharp
using System.IO;
using DiscUtils;
using DiscUtils.Fat;

using var image = File.Create("myfloppy.vfd");
using var floppy = FatFileSystem.FormatFloppy(
    image, FloppyDiskType.HighDensity, "MY FLOPPY  ");
using var file = floppy.OpenFile("hello.txt", FileMode.Create, FileAccess.Write);
using var writer = new StreamWriter(file);
writer.WriteLine("Hello World!");
```

## Repository layout and documentation

| Directory | Contents |
| --- | --- |
| [Library](Library) | Reusable storage implementations, infrastructure and meta-packages |
| [Integrations](Integrations) | Optional adapters for external/platform facilities |
| [Utilities](Utilities) | Applications built using DiscUtils |
| [SourceGenerators](SourceGenerators) | Build-time provider-registration generator; not a separately published NuGet package |
| [Tests](Tests) | Library, registration, source-generator and Native AOT test projects |
| [docs](docs) | Focused development and architecture documentation |

See the [wiki](https://github.com/LTRData/DiscUtils/wiki), [migration guide](https://github.com/LTRData/DiscUtils/wiki/Migration-from-DiscUtils-to-LTRData.DiscUtils) and package READMEs for further details. When changing a package's capabilities or dependencies, update its local README and the relevant table here. Local package READMEs are checked in and packed as `README.md`; the repository overview is not copied into every package.

To check NuGet documentation after a Release build or pack, run:

```powershell
./tools/Verify-PackageReadmes.ps1
```

The check verifies that every library/integration package declares and contains its local README and matches its project description. Pass `-PackageDirectory` if packages are written outside the repository. The same check runs in CI after the Release build.
