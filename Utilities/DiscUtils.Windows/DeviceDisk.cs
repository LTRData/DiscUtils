using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using DiscUtils.Streams;
using Microsoft.Win32.SafeHandles;

namespace DiscUtils.Windows;

/// <summary>
/// A Windows raw device opened read-only as a <see cref="VirtualDisk"/>: a physical disk (<c>\\.\PhysicalDriveN</c>), a volume or partition
/// (<c>\\.\X:</c>, <c>\\?\Volume{guid}</c>, <c>\\?\GLOBALROOT\Device\...</c>) or a volume shadow copy. A whole disk is read with its partition
/// table; a volume has none and reads as one file system. Administrator rights are normally required. The handle lets other processes keep
/// reading and writing, so a mounted volume in use can change while it is read: for a consistent view use an offline disk, a dismounted
/// volume or a snapshot.
/// </summary>
public sealed class DeviceDisk : VirtualDisk
{
    private readonly SafeFileHandle _handle;
    private readonly NativeMethods.DiskGeometry? _geometry;
    private SparseStream? _content;

    /// <summary>Opens <paramref name="path"/>; it must be a device path (see <see cref="IsDevicePath"/>).</summary>
    /// <exception cref="UnauthorizedAccessException">The device needs administrator rights.</exception>
    /// <exception cref="IOException">The device cannot be opened or its size cannot be read.</exception>
    public DeviceDisk(string path)
    {
        if (!IsDevicePath(path))
        {
            throw new ArgumentException(@"Not a device path (expected \\.\PhysicalDriveN, \\.\X:, \\?\Volume{...} or another \\.\ or \\?\ path).", nameof(path));
        }

        if (Environment.OSVersion.Platform != PlatformID.Win32NT)
        {
            throw new PlatformNotSupportedException("Device paths are Windows only; open a device file such as /dev/sdb as a raw disk instead.");
        }

        Path = path.TrimEnd('\\');
        _handle = NativeMethods.CreateFileW(Path, FileAccess.Read, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, NativeMethods.FileFlagNoBuffering, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            var error = Marshal.GetLastWin32Error();
            var message = $"Cannot open {Path}: {new Win32Exception(error).Message}";
            throw error == 5 ? new UnauthorizedAccessException(message + " (administrator rights are needed for raw devices)") : new IOException(message);
        }

        try
        {
            // A volume handle reads only the part a file system claims unless extended access is allowed; a disk handle does not support this.
            TryAllowExtendedDasdIo();
            Capacity = ReadLength();
            _geometry = TryReadGeometry();
        }
        catch
        {
            _handle.Dispose();
            throw;
        }
    }

    /// <summary>The device path.</summary>
    public string Path { get; }

    /// <summary>The device's sector size; 512 when the device does not report it.</summary>
    public int SectorSizeOfDevice => _geometry?.BytesPerSector is > 0 and var bytes ? bytes : 512;

    /// <summary>
    /// True for a Windows device path: <c>\\.\</c> or <c>\\?\</c> followed by something other than a drive-letter path (<c>\\?\C:\file</c> is a
    /// file, <c>\\.\C:</c> a volume).
    /// </summary>
    public static bool IsDevicePath(string? path)
    {
        if (path is null || path.Length < 5 || !(path.StartsWith(@"\\.\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal)))
        {
            return false;
        }

        var rest = path.Substring(4).TrimEnd('\\');
        if (rest.Length >= 2 && rest[1] == ':')
        {
            return rest.Length == 2; // \\.\C: is the volume; \\?\C:\dir\file is a long file path
        }

        return !rest.StartsWith("UNC\\", StringComparison.OrdinalIgnoreCase);
    }

    public override bool CanWrite => false;

    public override long Capacity { get; }

    public override int BlockSize => SectorSizeOfDevice;

    public override Geometry? Geometry => DiscUtils.Geometry.FromCapacity(Capacity, SectorSizeOfDevice);

    public override Geometry BiosGeometry => _geometry is { } g && g.TracksPerCylinder > 0 && g.SectorsPerTrack > 0
        ? new Geometry((int)Math.Min(g.Cylinders, int.MaxValue), g.TracksPerCylinder, g.SectorsPerTrack, SectorSizeOfDevice)
        : DiscUtils.Geometry.MakeBiosSafe(Geometry, Capacity);

    public override VirtualDiskClass DiskClass => VirtualDiskClass.HardDisk;

    /// <summary>The device's bytes, read through a cache (raw devices are slow for many small reads).</summary>
    public override SparseStream Content => _content ??= new BlockCacheStream(new DeviceStream(_handle, Capacity, SectorSizeOfDevice, ownsHandle: false), Ownership.Dispose,
        new BlockCacheSettings { BlockSize = 64 * 1024, ReadCacheSize = 32 * 1024 * 1024, LargeReadSize = 1024 * 1024, OptimumReadSize = 1024 * 1024 });

    public override IEnumerable<VirtualDiskLayer> Layers => [];

    public override VirtualDiskTypeInfo DiskTypeInfo => new()
    {
        Name = "Device",
        Variant = string.Empty,
        CanBeHardDisk = true,
        DeterministicGeometry = false,
        PreservesBiosGeometry = false,
        CalcGeometry = c => DiscUtils.Geometry.FromCapacity(c)
    };

    public override VirtualDisk CreateDifferencingDisk(DiscFileSystem fileSystem, string path) => throw new NotSupportedException("A device cannot have a differencing disk.");

    public override VirtualDisk CreateDifferencingDisk(string path, bool useAsync = false) => throw new NotSupportedException("A device cannot have a differencing disk.");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content?.Dispose();
            _content = null;
            _handle.Dispose();
        }

        base.Dispose(disposing);
    }

    private unsafe long ReadLength()
    {
        long length;
        if (!NativeMethods.DeviceIoControl(_handle, NativeMethods.IoctlDiskGetLengthInfo, null, 0, &length, sizeof(long), out _, IntPtr.Zero))
        {
            throw new IOException($"Cannot read the size of {Path}: {new Win32Exception(Marshal.GetLastWin32Error()).Message}");
        }

        return length;
    }

    private unsafe NativeMethods.DiskGeometry? TryReadGeometry()
    {
        NativeMethods.DiskGeometry geometry;
        return NativeMethods.DeviceIoControl(_handle, NativeMethods.IoctlDiskGetDriveGeometry, null, 0, &geometry, sizeof(NativeMethods.DiskGeometry), out _, IntPtr.Zero)
            ? geometry
            : null;
    }

    private unsafe void TryAllowExtendedDasdIo()
    {
        NativeMethods.DeviceIoControl(_handle, NativeMethods.FsctlAllowExtendedDasdIo, null, 0, null, 0, out _, IntPtr.Zero);
    }
}
