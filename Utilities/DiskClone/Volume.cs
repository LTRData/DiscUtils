//
// Copyright (c) 2008-2011, Kenneth Bell
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
//

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DiskClone;

internal sealed class Volume : IDisposable
{
    private SafeFileHandle _handle;
    private Stream _stream;

    public string Path { get; }

    public long Length { get; }

    public Volume(string path, long length)
    {
        Path = path.TrimEnd('\\');
        Length = length;

        if (!Path.StartsWith(@"\\"))
        {
            Path = $@"\\.\{Path}";
        }

        _handle = NativeMethods.CreateFileW(Path, FileAccess.Read, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception();
        }

        // Enable reading the full contents of the volume (not just the region bounded by the file system)
        if (!NativeMethods.DeviceIoControl(_handle, NativeMethods.EIOControlCode.FsctlAllowExtendedDasdIo, null, 0, out _, 0, out _, IntPtr.Zero))
        {
            throw new Win32Exception();
        }
    }

    public void Dispose()
    {
        if (_stream != null)
        {
            _stream.Dispose();
            _stream = null;
        }

        if (!_handle.IsClosed)
        {
            _handle.Dispose();
        }
    }

    public Stream Content
    {
        get
        {
            _stream ??= new VolumeStream(_handle);

            return _stream;
        }
    }

    public NativeMethods.DiskExtent[] GetDiskExtents()
    {
        var numExtents = 1;
        var bufferSize = 8 + Marshal.SizeOf<NativeMethods.DiskExtent>() * numExtents;
        Span<byte> buffer = stackalloc byte[bufferSize];

        if (!NativeMethods.DeviceIoControl(_handle, NativeMethods.EIOControlCode.VolumeGetDiskExtents, null, 0, out buffer[0], bufferSize, out _, IntPtr.Zero))
        {
            if (Marshal.GetLastWin32Error() != NativeMethods.ERROR_MORE_DATA)
            {
                throw new Win32Exception();
            }

            numExtents = MemoryMarshal.Read<int>(buffer);
            bufferSize = 8 + Marshal.SizeOf<NativeMethods.DiskExtent>() * numExtents;
            buffer = stackalloc byte[bufferSize];

            if (!NativeMethods.DeviceIoControl(_handle, NativeMethods.EIOControlCode.VolumeGetDiskExtents, null, 0, out buffer[0], bufferSize, out _, IntPtr.Zero))
            {
                throw new Win32Exception();
            }
        }

        return MemoryMarshal.Cast<byte, NativeMethods.DiskExtent>(buffer.Slice(8)).Slice(0, numExtents).ToArray();
    }
}
