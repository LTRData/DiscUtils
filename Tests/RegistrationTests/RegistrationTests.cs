using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DiscUtils;
using DiscUtils.Partitions;
using DiscUtils.Streams;
using RegistrationPlugin;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace RegistrationTests;

public sealed class RegistrationTests
{
    public RegistrationTests() => DiscUtils.Complete.SetupHelper.SetupComplete();

    [Fact]
    public void BuiltInTypesAndExtensionsAreAvailableAfterExplicitSetup()
    {
        foreach (var type in new[] { "RAW", "VHD", "VHDX", "VMDK", "VDI", "DMG", "XVA", "Optical" })
            Assert.Contains(type, VirtualDiskManager.SupportedDiskTypes);
        foreach (var extension in new[] { "img", "vhd", "avhd", "vhdx", "avhdx", "vmdk", "vdi", "dmg", "xva", "iso", "bin" })
            Assert.Contains(extension, VirtualDiskManager.SupportedDiskFormats);
        Assert.IsType<DiscUtils.Vhd.DiskBuilder>(DiskImageBuilder.GetBuilder("vhd", "dynamic"));
        Assert.IsType<DiscUtils.Vhdx.DiskBuilder>(DiskImageBuilder.GetBuilder("VHDX", "dynamic"));
    }

    [Theory]
    [InlineData("VHD", ".VHD")]
    [InlineData("VHD", ".aVhD")]
    [InlineData("VHDX", ".VHDX")]
    [InlineData("VHDX", ".aVhDx")]
    [InlineData("VDI", ".VDI")]
    [InlineData("VMDK", ".VMDK")]
    public void ExtensionSelectsExpectedFactory(string type, string extension)
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "disk" + extension);
        try
        {
            // No format-specific type reference is needed for creation or reopening.
            const string variant = "dynamic";
            using (var disk = VirtualDisk.CreateDisk(type, variant, path, 8 * 1024 * 1024, null, null)!)
                disk.Content.WriteByte(0x5A);
            using var opened = VirtualDisk.OpenDisk(path, FileAccess.Read);
            Assert.NotNull(opened);
            Assert.Equal("DiscUtils." + type[0] + type.Substring(1).ToLowerInvariant() + ".Disk", opened!.GetType().FullName);
            Assert.Equal(0x5A, opened.Content.ReadByte());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FileSystemsAreDetectedAndOpenedAfterExplicitSetup()
    {
        using var stream = new MemoryStream();
        using (var fs = DiscUtils.Fat.FatFileSystem.FormatFloppy(stream, FloppyDiskType.HighDensity, "TEST"))
        using (var file = fs.OpenFile("test.txt", FileMode.Create, FileAccess.Write)) file.WriteByte(0x63);
        var detected = FileSystemManager.DetectFileSystems(stream);
        var info = Assert.Single(detected, f => f.Name == "FAT");
        using var reopened = info.Open(stream);
        using var content = reopened.OpenFile("test.txt", FileMode.Open, FileAccess.Read);
        Assert.Equal(0x63, content.ReadByte());
    }

    [Fact]
    public void OpticalFileSystemIsDetectedAndOpenedAfterExplicitSetup()
    {
        var builder = new DiscUtils.Iso9660.CDBuilder { UseJoliet = true };
        builder.AddFile("hello.txt", new byte[] { 0x42 });
        using var stream = builder.Build();
        var info = Assert.Single(FileSystemManager.DetectFileSystems(stream), f => f.Name == "ISO9660");
        using var fs = info.Open(stream);
        using var content = fs.OpenFile("hello.txt", FileMode.Open, FileAccess.Read);
        Assert.Equal(0x42, content.ReadByte());
    }

    [Fact]
    public void RepeatedSetupAndReflectionOfBuiltInsDoNotDuplicateDetectors()
    {
        using var stream = new MemoryStream();
        using (DiscUtils.Fat.FatFileSystem.FormatFloppy(stream, FloppyDiskType.HighDensity, "TEST")) { }
        var before = FileSystemManager.DetectFileSystems(stream).Select(f => f.Name).ToArray();
        Parallel.For(0, 8, _ => DiscUtils.Complete.SetupHelper.SetupComplete());
        DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(DiscUtils.Fat.FatFileSystem).Assembly);
        FileSystemManager.RegisterFileSystems(typeof(DiscUtils.Fat.FatFileSystem).Assembly);
        VirtualDiskManager.RegisterVirtualDiskTypes(typeof(DiscUtils.Vhd.Disk).Assembly);
        Assert.Equal(before, FileSystemManager.DetectFileSystems(stream).Select(f => f.Name));
    }

    [Fact]
    public void ExplicitDiskRegistrationIsExtensibleAndRejectsConflictsAtomically()
    {
        var factory = new TestDiskFactory();
        VirtualDiskManager.RegisterVirtualDiskFactory("EXPLICIT", new[] { ".explicitdisk" }, factory);
        Assert.Equal(new[] { "test" }, VirtualDisk.GetSupportedDiskVariants("explicit"));
        Assert.IsType<TestBuilder>(DiskImageBuilder.GetBuilder("EXPLICIT", "test"));
        Assert.Throws<ArgumentException>(() => VirtualDiskManager.RegisterVirtualDiskFactory("explicit", new[] { "unused" }, factory));
        Assert.DoesNotContain("unused", VirtualDiskManager.SupportedDiskFormats);
        Assert.Throws<ArgumentException>(() => VirtualDiskManager.RegisterVirtualDiskFactory("UNUSED", new[] { "unused", ".VHD" }, factory));
        Assert.DoesNotContain("UNUSED", VirtualDiskManager.SupportedDiskTypes);
        Assert.DoesNotContain("unused", VirtualDiskManager.SupportedDiskFormats);
        Assert.Throws<ArgumentException>(() => VirtualDiskManager.RegisterVirtualDiskFactory("REPEATED", new[] { ".a", "A" }, factory));
        Assert.DoesNotContain("REPEATED", VirtualDiskManager.SupportedDiskTypes);
    }

    [Fact]
    public void ExplicitFileSystemRegistrationPreservesInstanceDuplicatesAndOrder()
    {
        var first = new TestFileSystemFactory("FirstExplicitFS");
        FileSystemManager.RegisterFileSystems(first);
        FileSystemManager.RegisterFileSystems(new TestFileSystemFactory("SecondExplicitFS"));
        FileSystemManager.RegisterFileSystems(first);
        using var stream = MarkerStream();
        Assert.Equal(new[] { "FirstExplicitFS", "SecondExplicitFS", "FirstExplicitFS" },
            FileSystemManager.DetectFileSystems(stream).Select(f => f.Name).Where(n => n.EndsWith("ExplicitFS")));
    }

    [Fact]
    public void TransportsAreCreatedPerOpenAndDisposed()
    {
        VirtualDiskManager.RegisterVirtualDiskTransport("explicittransport", () => new TestTransport());
        Assert.Throws<ArgumentException>(() => VirtualDiskManager.RegisterVirtualDiskTransport("EXPLICITTRANSPORT", () => new TestTransport()));
        var created = TestTransport.Created;
        var disposed = TestTransport.Disposed;
        using (VirtualDisk.OpenDisk("explicittransport://localhost/disk", FileAccess.Read)) { }
        using (VirtualDisk.OpenDisk("explicittransport://localhost/disk", FileAccess.Read)) { }
        Assert.Equal(created + 2, TestTransport.Created);
        Assert.Equal(disposed + 2, TestTransport.Disposed);
    }

    [Fact]
    public void ExternalVolumeFactoriesUseOrdinaryPublicApis()
    {
        var volume = new TestLogicalVolumeFactory(includeVolume: true);
        VolumeManager.RegisterLogicalVolumeFactory(volume);
        using var stream = new MemoryStream(new byte[4096]);
        Assert.False(PartitionTable.IsPartitioned(stream));
        var mapped = Assert.Single(new VolumeManager(stream).GetLogicalVolumes(), v => v.TypeAsString == "ThirdPartyVolume");
        using var opened = mapped.Open();
        Assert.Equal(4096, opened.Length);
        Assert.True(volume.Calls > 0);
    }

    [Fact]
    public void AttributedPluginStillRegistersByReflectionWithoutGenerator()
    {
        var assembly = typeof(ReflectionDiskFactory).Assembly;
        DiscUtils.Setup.SetupHelper.RegisterAssembly(assembly);
        DiscUtils.Setup.SetupHelper.RegisterAssembly(assembly);
        // Reflection-first registration must also suppress a later handwritten entry point.
        DiscUtils.Setup.SetupHelper.RegisterAssembly(assembly,
            () => throw new InvalidOperationException("Explicit registration ran after reflection registration."));
        Assert.Equal(1, ReflectionDiskFactory.Constructions);
        Assert.Equal(new[] { "test" }, VirtualDisk.GetSupportedDiskVariants("REFLECTION"));
        Assert.Contains("reflectiondisk", VirtualDiskManager.SupportedDiskFormats);
        using var stream = MarkerStream();
        Assert.Single(FileSystemManager.DetectFileSystems(stream), f => f.Name == "ReflectionFS");
        using var disk = VirtualDisk.OpenDisk("reflectiontransport://localhost/disk", FileAccess.Read);
        Assert.NotNull(disk);
    }

    [Fact]
    public void PrivateLibraryGeneratedRegistrationFollowedByReflectionRunsOnce()
    {
        Assert.Equal(0, GeneratedRegistrationPlugin.PrivateFileSystemFactory.Constructions);
        GeneratedRegistrationPlugin.Formats.Register();
        GeneratedRegistrationPlugin.Formats.Register();
        DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(GeneratedRegistrationPlugin.PrivateFileSystemFactory).Assembly);
        Assert.Equal(1, GeneratedRegistrationPlugin.PrivateFileSystemFactory.Constructions);
        using var stream = MarkerStream();
        Assert.Single(FileSystemManager.DetectFileSystems(stream), f => f.Name == "GeneratedPrivateFS");
    }

    private static MemoryStream MarkerStream()
    {
        var buffer = new byte[65536];
        buffer[0] = 0x71;
        buffer[1] = 0x93;
        return new MemoryStream(buffer);
    }
}
