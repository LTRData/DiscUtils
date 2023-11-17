# Project Description

DiscUtils is a .NET library to read and write ISO files and Virtual Machine disk files (VHD, VDI, XVA, VMDK, etc). DiscUtils is developed in C# with no native code (or P/Invoke).

### This fork

This is https://github.com/LTRData/DiscUtils

This is a fork of https://github.com/DiscUtils/DiscUtils, a fork of https://github.com/quamotion/DiscUtils, which is a fork of https://discutils.codeplex.com/.

This fork has the primary goal of modernized and optimized code, better compatibility with non-Windows platforms and much better performance at the cost of dropping support for older .NET Framework versions and some breaking interface changes compared to upstream DiscUtils. For example, there is support for `async` and `Span<byte>` versions of read and write on `Stream` objects throughout all I/O paths and methods for enumerating files in directories return `IEnumerable` objects instead of building temporary lists and arrays.

All libraries target .NET Framework 4.6 and 4.8, .NET Standard 2.0 and 2.1 as well as .NET 6.0, 7.0 and 8.0.

### Implementation status

Implementations of the ISO, UDF, FAT and NTFS file systems are fairly stable. Lots of work have been carried out to solve issues with Ext2/3/4, SquashFs and Btrfs implementations, but there are probably a few bugs left. It is also possible to open TAR and ZIP archives as file systems. VHD, XVA, VMDK and VDI disk formats are implemented, as well as read/write Registry support. Support for pending updates in Registry log files is also implemented. The library also includes a simple iSCSI initiator, for accessing disks via iSCSI and an NFS client implementation.

### Wiki

See more up to date documentation at the [Wiki](https://github.com/DiscUtils/DiscUtils/wiki)

### Implementation in this repository

The DiscUtils library has been split into 25 independent projects, which can function without the others present. This reduces the "cost" of having DiscUtils immensely, as we're down from the 1 MB binary it used to be.

To work with this, four Meta packages have been created:

* DiscUtils.Complete: Everything, like before
* DiscUtils.Containers: such as VMDK, VHD, VHDX
* DiscUtils.FileSystems: such as NTFS, FAT, EXT
* DiscUtils.Transports: such as NFS

#### Note on detections

DiscUtils has a number of detection helpers. These provide services like "which filesystem is this stream?". For this to work, you must register your filesystem providers with the DiscUtils core. To do this, call:

    DiscUtils.Setup.RegisterAssembly(assembly);

Where `assembly` is the assembly you wish to register. Note that the metapackages have helpers:

```csharp
SetupHelper.SetupComplete(); // From DiscUtils.Complete
SetupHelper.SetupContainers(); // From DiscUtils.Containers
SetupHelper.SetupFileSystems(); // From DiscUtils.FileSystems
SetupHelper.SetupTransports(); // From DiscUtils.Transports
```

## How to use the Library

Here's a few really simple examples.

### How to create a new ISO:

```csharp
CDBuilder builder = new CDBuilder();
builder.UseJoliet = true;
builder.VolumeIdentifier = "A_SAMPLE_DISK";
builder.AddFile(@"Folder\Hello.txt", Encoding.ASCII.GetBytes("Hello World!"));
builder.Build(@"C:\temp\sample.iso");
``` 

You can add files as byte arrays (shown above), as files from the Windows filesystem, or as a Stream. By using a different form of Build, you can get a Stream to the ISO file, rather than writing it to the Windows filesystem.


### How to extract a file from an ISO:

```csharp
using (FileStream isoStream = File.Open(@"C:\temp\sample.iso"))
{
  CDReader cd = new CDReader(isoStream, true);
  Stream fileStream = cd.OpenFile(@"Folder\Hello.txt", FileMode.Open);
  // Use fileStream...
}
``` 

You can also browse through the directory hierarchy, starting at cd.Root.

### How to create a virtual hard disk:

```csharp
long diskSize = 30 * 1024 * 1024; //30MB
using (Stream vhdStream = File.Create(@"C:\TEMP\mydisk.vhd"))
{
    Disk disk = Disk.InitializeDynamic(vhdStream, diskSize);
    BiosPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsFat);
    using (FatFileSystem fs = FatFileSystem.FormatPartition(disk, 0, null))
    {
        fs.CreateDirectory(@"TestDir\CHILD");
        // do other things with the file system...
    }
}
``` 

As with ISOs, you can browse the file system, starting at fs.Root.


### How to create a virtual floppy disk:

```csharp
using (FileStream fs = File.Create(@"myfloppy.vfd"))
{
    using (FatFileSystem floppy = FatFileSystem.FormatFloppy(fs, FloppyDiskType.HighDensity, "MY FLOPPY  "))
    {
        using (Stream s = floppy.OpenFile("foo.txt", FileMode.Create))
        {
            // Use stream...
        }
    }
}
``` 

Again, start browsing the file system at floppy.Root.

