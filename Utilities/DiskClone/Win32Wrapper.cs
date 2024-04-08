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
using Microsoft.Win32.SafeHandles;

namespace DiskClone;

internal static class Win32Wrapper
{
    public static SafeFileHandle OpenFileHandle(string path)
    {
        var handle = NativeMethods.CreateFileW(path, FileAccess.Read, FileShare.Read, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            throw new Win32Exception();
        }

        return handle;
    }

    public static unsafe NativeMethods.DiskGeometry GetDiskGeometry(SafeFileHandle handle)
    {
        var buffer = new NativeMethods.DiskGeometry();
        if (!NativeMethods.DeviceIoControl(handle, NativeMethods.EIOControlCode.DiskGetDriveGeometry, null, 0, &buffer, sizeof(NativeMethods.DiskGeometry), out _, IntPtr.Zero))
        {
            throw new Win32Exception();
        }

        return buffer;
    }

    public static unsafe NativeMethods.NtfsVolumeData GetNtfsVolumeData(SafeFileHandle volumeHandle)
    {
        var buffer = new NativeMethods.NtfsVolumeData();
        if (!NativeMethods.DeviceIoControl(volumeHandle, NativeMethods.EIOControlCode.FsctlGetNtfsVolumeData, null, 0, &buffer, sizeof(NativeMethods.NtfsVolumeData), out _, IntPtr.Zero))
        {
            throw new Win32Exception();
        }

        return buffer;
    }

    public static unsafe long GetDiskCapacity(SafeFileHandle diskHandle)
    {
        long capacity;
        if (!NativeMethods.DeviceIoControl(diskHandle, NativeMethods.EIOControlCode.DiskGetLengthInfo, null, 0, &capacity, sizeof(long), out _, IntPtr.Zero))
        {
            throw new Win32Exception();
        }

        return capacity;
    }
}
