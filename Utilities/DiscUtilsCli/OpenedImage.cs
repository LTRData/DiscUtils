using DiscUtils;
using DiscUtils.Raw;
using DiscUtils.ReFs;
using DiscUtils.Windows;
using DiscUtilsCli.Commands;
using FileSystemInfo = DiscUtils.FileSystemInfo;

namespace DiscUtilsCli;

/// <summary>
///     A disk image opened read-only and one file system on it. Any disk format DiscUtils knows (VHDX, VHD, VMDK, VDI, raw images with a known
///     extension) is opened through <see cref="VirtualDisk.OpenDisk(string, FileAccess)" />; a file with another extension is opened as a raw
///     disk. The file system is the one on the volume chosen with <see cref="ImageOptions.Volume" />, or the first volume holding a file system
///     DiscUtils detects; a disk without a partition table is tried as one volume. ReFS is opened with its own options (checksums, replay).
/// </summary>
internal sealed class OpenedImage : IDisposable
{
    /// <summary>The <see cref="ImageOptions.Volume" /> value that opens the disk's whole content as one file system.</summary>
    public const int WholeDisk = -1;

    private readonly VirtualDisk _disk;

    private OpenedImage(VirtualDisk disk, DiscFileSystem fileSystem, string fileSystemName, string volume, int volumeIndex)
    {
        _disk = disk;
        FileSystem = fileSystem;
        FileSystemName = fileSystemName;
        Volume = volume;
        VolumeIndex = volumeIndex;
    }

    public DiscFileSystem FileSystem { get; }

    /// <summary>The detected file system's name (ReFS, NTFS, FAT...).</summary>
    public string FileSystemName { get; }

    /// <summary>The volume's identity, or "whole disk".</summary>
    public string Volume { get; }

    /// <summary>The index of the volume among the disk's logical volumes, or -1 for the whole disk.</summary>
    public int VolumeIndex { get; }

    public ReFsFileSystem? ReFs => FileSystem as ReFsFileSystem;

    public void Dispose()
    {
        FileSystem.Dispose();
        _disk.Dispose();
    }

    public static OpenedImage Open(string path, ImageOptions options)
    {
        var disk = OpenDisk(path);

        try
        {
            return OpenFileSystem(disk, options);
        }
        catch
        {
            disk.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     The volumes of an image, as <see cref="ImageOptions.Volume" /> numbers them: every logical volume with its place on the disk, its type and
    ///     the file system DiscUtils detects on it (null when none). A disk without any volume is listed as one entry for the whole disk (index -1).
    /// </summary>
    public static IReadOnlyList<VolumeChoice> ListVolumes(string path)
    {
        using var disk = OpenDisk(path);
        var volumes = new VolumeManager(disk).GetLogicalVolumes();
        var choices = new List<VolumeChoice>();

        for (var i = 0; i < volumes.Length; i++)
        {
            var volume = volumes[i];
            choices.Add(new VolumeChoice(i, volume.Identity, volume.PhysicalStartSector * disk.SectorSize, volume.Length,
                volume.TypeAsString ?? (volume.BiosType == 0 ? "whole disk (no partition table)" : $"0x{volume.BiosType:X2}"), Detect(() => FileSystemManager.DetectFileSystems(volume))));
        }

        if (choices.Count == 0)
        {
            choices.Add(new VolumeChoice(WholeDisk, "whole disk", 0, disk.Capacity, "no partition table", Detect(() => FileSystemManager.DetectFileSystems(disk.Content))));
        }

        return choices;
    }

    private static string? Detect(Func<IEnumerable<FileSystemInfo>> detect)
    {
        try
        {
            return detect().FirstOrDefault()?.Name;
        }
        catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
        {
            return null;
        }
    }

    private static VirtualDisk OpenDisk(string path)
    {
        // A Windows device (\\.\PhysicalDriveN, \\.\X:, \\?\Volume{...}); a path or URI DiscUtils knows (disk formats by extension,
        // transports); any other existing file is opened as a raw disk.
        if (DeviceDisk.IsDevicePath(path))
        {
            return new DeviceDisk(path);
        }

        return VirtualDisk.OpenDisk(path, FileAccess.Read)
               ?? (File.Exists(path) ? new Disk(path, FileAccess.Read) : throw new FileNotFoundException($"No such file: {path}", path));
    }

    private static OpenedImage OpenFileSystem(VirtualDisk disk, ImageOptions options)
    {
        var volumes = new VolumeManager(disk).GetLogicalVolumes();

        if (options.Volume is WholeDisk)
        {
            return OpenWholeDisk(disk, options) ?? throw new NotSupportedException("The disk's content holds no file system DiscUtils can read.");
        }

        if (options.Volume is { } index)
        {
            if (index < 0 || index >= volumes.Length)
            {
                throw new IOException($"The disk has {volumes.Length} volume(s); --volume takes 0 to {volumes.Length - 1}.");
            }

            var chosen = volumes[index];
            var detected = FileSystemManager.DetectFileSystems(chosen).FirstOrDefault()
                           ?? throw new NotSupportedException($"Volume {index} holds no file system DiscUtils can read.");

            return new OpenedImage(disk, OpenVolume(chosen, detected, options), detected.Name, chosen.Identity, index);
        }

        for (var i = 0; i < volumes.Length; i++)
        {
            if (FileSystemManager.DetectFileSystems(volumes[i]).FirstOrDefault() is { } detected)
            {
                return new OpenedImage(disk, OpenVolume(volumes[i], detected, options), detected.Name, volumes[i].Identity, i);
            }
        }

        // A volume image without a partition table.
        return OpenWholeDisk(disk, options) ?? throw new NotSupportedException("No file system DiscUtils can read was found on the image.");
    }

    private static OpenedImage? OpenWholeDisk(VirtualDisk disk, ImageOptions options)
    {
        var content = disk.Content;

        if (FileSystemManager.DetectFileSystems(content).FirstOrDefault() is not { } whole)
        {
            return null;
        }

        var fileSystem = whole.Name == "ReFS" ? new ReFsFileSystem(content, ReFsOptions(options)) : whole.Open(content);
        return new OpenedImage(disk, fileSystem, whole.Name, "whole disk", WholeDisk);
    }

    private static DiscFileSystem OpenVolume(VolumeInfo volume, FileSystemInfo detected, ImageOptions options) =>
        detected.Name == "ReFS" ? new ReFsFileSystem(volume.Open(), ReFsOptions(options)) : detected.Open(volume);

    private static ReFsFileSystemOptions ReFsOptions(ImageOptions options) =>
        new ReFsFileSystemOptions
        {
            VerifyChecksums = !options.NoVerify,
            VerifyDataChecksums = !options.NoVerify,
            ReplayLog = options.Replay || options.ReplayTransactions is not null,
            ReplayTransactions = options.ReplayTransactions
        };
}