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

using DiscUtils.Windows;
using Microsoft.Win32.SafeHandles;

namespace DiskClone;

/// <summary>An NTFS volume's bytes, as long as its file system reports; the handle stays owned by <see cref="Volume"/>.</summary>
internal sealed class VolumeStream : DeviceStream
{
    public VolumeStream(SafeFileHandle handle)
        : this(handle, Win32Wrapper.GetNtfsVolumeData(handle))
    {
    }

    private VolumeStream(SafeFileHandle handle, NativeMethods.NtfsVolumeData volume)
        : base(handle, volume.TotalClusters * volume.BytesPerCluster, (int)volume.BytesPerSector, ownsHandle: false)
    {
    }
}
