using System;
using System.IO;
using System.Linq;
using DiscUtils;
using DiscUtils.Internal;
using DiscUtils.Partitions;
using DiscUtils.Streams;

// Merely referencing/loading format libraries must not register their formats.
GC.KeepAlive(typeof(DiscUtils.Vhd.Disk));
if (VirtualDiskManager.SupportedDiskTypes.Contains("VHD")) throw new Exception("VHD registered before explicit setup.");

// Run each path in a fresh process so one cannot hide missing registration in the other.
var useContainers = args.Length == 1 && args[0] == "--containers";
if (args.Length != 0 && !useContainers) throw new ArgumentException("Usage: NativeAotSmoke [--containers]");
if (useContainers)
{
    DiscUtils.Containers.SetupHelper.SetupContainers();
    DiscUtils.Containers.SetupHelper.SetupContainers();
    foreach (var type in new[] { "RAW", "DMG", "VHD", "VHDX", "VMDK", "VDI", "XVA" })
        if (!VirtualDiskManager.SupportedDiskTypes.Contains(type)) throw new Exception("Missing container: " + type);
    if (VirtualDiskManager.SupportedDiskTypes.Contains("Optical")) throw new Exception("An implementation reference registered extra providers.");
}
else
{
    DiscUtils.Vhd.Formats.Register();
    DiscUtils.Vhd.Formats.Register();
    if (!VirtualDiskManager.SupportedDiskTypes.SequenceEqual(new[] { "VHD" }))
        throw new Exception("VHD registration included providers from another assembly.");
    // File-based generic disk APIs need Core's file transport; VHD does not register it implicitly.
    DiscUtils.Core.Formats.Register();
    DiscUtils.Lvm.Formats.Register();
}
DiscUtils.Fat.Formats.Register();
if (!VirtualDiskManager.SupportedDiskTypes.Contains("VHD") ||
    !VirtualDiskManager.SupportedDiskFormats.Contains("avhd")) throw new Exception("Missing VHD registration.");

var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".AVHD");
try
{
    using (var disk = VirtualDisk.CreateDisk("VHD", "dynamic", path, 8 * 1024 * 1024, null, null)!)
    {
        BiosPartitionTable.Initialize(disk, WellKnownPartitionType.WindowsFat);
    }
    using var opened = VirtualDisk.OpenDisk(path, FileAccess.Read);
    if (opened == null || opened.Capacity != 8 * 1024 * 1024) throw new Exception("VHD open failed.");
    if (!PartitionTable.IsPartitioned(opened)) throw new Exception("Partition discovery failed.");
    if (new VolumeManager(opened).GetLogicalVolumes().Length != 1) throw new Exception("Volume discovery failed.");

    var builder = DiskImageBuilder.GetBuilder("vhd", "dynamic");
    using var content = new SparseMemoryStream();
    content.SetLength(1024 * 1024);
    builder.Content = content;
    if (!builder.Build("image").Any()) throw new Exception("Builder lookup failed.");

    // Minimal, empty FAT12 floppy; detection and opening go through the shared registry.
    var bytes = new byte[1440 * 1024];
    bytes[0] = 0xEB; bytes[1] = 0x3C; bytes[2] = 0x90;
    bytes[11] = 0; bytes[12] = 2; bytes[13] = 1; bytes[14] = 1; bytes[16] = 2;
    bytes[17] = 224; bytes[19] = 0x40; bytes[20] = 0x0B; bytes[21] = 0xF0;
    bytes[22] = 9; bytes[24] = 18; bytes[26] = 2; bytes[38] = 0x29;
    System.Text.Encoding.ASCII.GetBytes("FAT12   ").CopyTo(bytes, 54);
    bytes[510] = 0x55; bytes[511] = 0xAA;
    foreach (var offset in new[] { 512, 5120 })
    { bytes[offset] = 0xF0; bytes[offset + 1] = 0xFF; bytes[offset + 2] = 0xFF; }
    using var fat = new MemoryStream(bytes);
    var detected = FileSystemManager.DetectFileSystems(fat).Single(f => f.Name == "FAT");
    using var fs = detected.Open(fat);
    if (fs.GetFiles(@"\").Any()) throw new Exception("FAT open failed.");

    // Containers references OpticalDiscSharing for packaging, but setup must leave its scheme available.
    var scheme = useContainers ? "ods" : "smoke";
    VirtualDiskManager.RegisterVirtualDiskTransport(scheme, static () => new SmokeTransport());
    using var external = VirtualDisk.OpenDisk(scheme + "://localhost/disk", FileAccess.Read);
    if (external == null || external.Capacity != 4096) throw new Exception("Explicit transport registration failed.");
    Console.WriteLine("PASS: " + (useContainers ? "SetupContainers" : "direct Formats.Register") +
        "; VHD, FAT, partition/volume, builder and third-party transport registration.");
}
finally { File.Delete(path); }

sealed class SmokeTransport : VirtualDiskTransport
{
    public override bool IsRawDisk => true;
    public override void Connect(Uri uri, string? username, string? password) { }
    public override VirtualDisk OpenDisk(FileAccess access) => new DiscUtils.Raw.Disk(new MemoryStream(new byte[4096]), Ownership.Dispose);
    public override FileLocator GetFileLocator(bool useAsync) => throw new NotSupportedException();
    public override string GetFileName() => "disk";
    public override string GetExtraInfo() => "";
}
