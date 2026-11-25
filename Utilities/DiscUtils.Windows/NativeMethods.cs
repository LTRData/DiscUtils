using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiscUtils.Windows;

internal static partial class NativeMethods
{
    internal const int FileFlagNoBuffering = 0x20000000;
    internal const int ErrorInvalidFunction = 1;
    internal const int ErrorNotSupported = 50;

    // IOCTL codes: (device type << 16) | (access << 14) | (function << 2) | method.
    internal const uint IoctlDiskGetDriveGeometry = (0x7u << 16) | (0x0000u << 2);
    internal const uint IoctlDiskGetLengthInfo = (0x7u << 16) | (0x1u << 14) | (0x0017u << 2);
    internal const uint FsctlAllowExtendedDasdIo = (0x9u << 16) | (32u << 2) | 3u;

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct DiskGeometry
    {
        public readonly long Cylinders;
        public readonly int MediaType;
        public readonly int TracksPerCylinder;
        public readonly int SectorsPerTrack;
        public readonly int BytesPerSector;
    }

#if NET7_0_OR_GREATER

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeFileHandle CreateFileW(string fileName, FileAccess fileAccess, FileShare fileShare, IntPtr securityAttributes,
        FileMode creationDisposition, int flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool DeviceIoControl(SafeFileHandle device, uint controlCode, void* inBuffer, int inBufferSize, void* outBuffer,
        int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetFilePointerEx(SafeFileHandle handle, long position, out long newPosition, int moveMethod);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReadFile(SafeFileHandle handle, IntPtr buffer, int count, out int read, IntPtr overlapped);

#else

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    internal static extern SafeFileHandle CreateFileW(string fileName, [MarshalAs(UnmanagedType.U4)] FileAccess fileAccess,
        [MarshalAs(UnmanagedType.U4)] FileShare fileShare, IntPtr securityAttributes, [MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
        int flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe extern bool DeviceIoControl(SafeFileHandle device, uint controlCode, void* inBuffer, int inBufferSize, void* outBuffer,
        int outBufferSize, out int bytesReturned, IntPtr overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetFilePointerEx(SafeFileHandle handle, long position, out long newPosition, int moveMethod);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, int count, out int read, IntPtr overlapped);

#endif
}