using System;
using System.Collections.Generic;
using System.IO;
using DiscUtils;
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Vfs;

namespace RegistrationPlugin;

public class TestDiskFactory : VirtualDiskFactory
{
    public override string[] Variants => new[] { "test" };
    public override VirtualDiskTypeInfo GetDiskTypeInformation(string variant) => new() { Name = "Plugin" };
    public override DiskImageBuilder GetImageBuilder(string variant) => new TestBuilder();
    public override VirtualDisk CreateDisk(FileLocator locator, string variant, string path, VirtualDiskParameters parameters)
        => new DiscUtils.Raw.Disk(new MemoryStream(new byte[4096]), Ownership.Dispose);
    public override VirtualDisk OpenDisk(string path, FileAccess access, bool useAsync) => new DiscUtils.Raw.Disk(new MemoryStream(new byte[4096]), Ownership.Dispose);
    public override VirtualDisk OpenDisk(FileLocator locator, string path, FileAccess access)
        => new DiscUtils.Raw.Disk(new MemoryStream(new byte[4096]), Ownership.Dispose);
    public override VirtualDiskLayer? OpenDiskLayer(FileLocator locator, string path, FileAccess access) => null;
}

[VirtualDiskFactory("REFLECTION", ".reflectiondisk")]
public sealed class ReflectionDiskFactory : TestDiskFactory
{
    public static int Constructions;
    public ReflectionDiskFactory()
    {
        if (++Constructions > 1) throw new InvalidOperationException("Assembly registration re-entered its factory constructor.");
        DiscUtils.Setup.SetupHelper.RegisterAssembly(typeof(ReflectionDiskFactory).Assembly);
    }
}

public sealed class TestBuilder : DiskImageBuilder
{
    public override IEnumerable<DiskImageFileSpecification> Build(string baseName) => Array.Empty<DiskImageFileSpecification>();
}

public class TestFileSystemFactory : VfsFileSystemFactory
{
    private readonly string _name;
    public TestFileSystemFactory(string name) => _name = name;
    public override IEnumerable<DiscUtils.FileSystemInfo> Detect(Stream stream, VolumeInfo? volumeInfo)
    {
        stream.Position = 0;
        if (stream.Length >= 512 && stream.ReadByte() == 0x71 && stream.ReadByte() == 0x93)
            yield return new VfsFileSystemInfo(_name, "Test format", (_, _, _) => throw new NotSupportedException());
    }
}

[VfsFileSystemFactory]
public sealed class ReflectionFileSystemFactory : TestFileSystemFactory
{
    public ReflectionFileSystemFactory() : base("ReflectionFS") { }
}

public class TestTransport : VirtualDiskTransport
{
    public static int Created;
    public static int Disposed;
    public TestTransport() => Created++;
    public override bool IsRawDisk => true;
    public override void Connect(Uri uri, string? username, string? password) { }
    public override VirtualDisk OpenDisk(FileAccess access) => new DiscUtils.Raw.Disk(new MemoryStream(new byte[4096]), Ownership.Dispose);
    public override FileLocator GetFileLocator(bool useAsync) => throw new NotSupportedException();
    public override string GetFileName() => "test";
    public override string GetExtraInfo() => "";
    protected override void Dispose(bool disposing) { if (disposing) Disposed++; }
}

[VirtualDiskTransport("reflectiontransport")]
public sealed class ReflectionTransport : TestTransport { }

public class TestLogicalVolumeFactory : LogicalVolumeFactory
{
    private readonly bool _includeVolume;
    public TestLogicalVolumeFactory(bool includeVolume = false) => _includeVolume = includeVolume;
    public int Calls;
    public override bool HandlesPhysicalVolume(PhysicalVolumeInfo volume) => false;
    public override void MapDisks(IEnumerable<VirtualDisk> disks, Dictionary<string, LogicalVolumeInfo> result)
    {
        Calls++;
        if (!_includeVolume) return;
        var volume = new LogicalVolumeInfo(new Guid("559b7b73-6870-4820-a3ec-25a5c7984da7"), null,
            () => SparseStream.FromStream(new MemoryStream(new byte[4096]), Ownership.Dispose),
            4096, 0, LogicalVolumeStatus.Healthy, "ThirdPartyVolume");
        result.Add(volume.Identity, volume);
    }
}

[LogicalVolumeFactory]
public sealed class ReflectionLogicalVolumeFactory : TestLogicalVolumeFactory { }
