//
// Copyright (c) 2008-2012, Kenneth Bell
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
using System.IO;
using DiscUtils.Internal;

namespace DiscUtils.Vhdx;

[VirtualDiskFactory("VHDX", ".vhdx,.avhdx")]
internal sealed class DiskFactory : VirtualDiskFactory
{
    public override string[] Variants => new[] { "fixed", "dynamic" };

    public override VirtualDiskTypeInfo GetDiskTypeInformation(string variant)
    {
        return MakeDiskTypeInfo(variant);
    }

    public override DiskImageBuilder GetImageBuilder(string variant)
    {
        var builder = new DiskBuilder();

        builder.DiskType = variant switch
        {
            "fixed" => DiskType.Fixed,
            "dynamic" => DiskType.Dynamic,
            _ => throw new ArgumentException(
                                $"Unknown VHD disk variant '{variant}'",
                                nameof(variant)),
        };
        return builder;
    }

    public override VirtualDisk CreateDisk(FileLocator locator, string variant, string path,
                                           VirtualDiskParameters diskParameters)
    {
        return variant switch
        {
            "fixed" => Disk.InitializeFixed(locator, path, diskParameters.Capacity, diskParameters.Geometry),
            "dynamic" => Disk.InitializeDynamic(locator, path, diskParameters.Capacity, diskParameters.Geometry,
                                FileParameters.DefaultDynamicBlockSize),
            _ => throw new ArgumentException(
                                $"Unknown VHD disk variant '{variant}'",
                                nameof(variant)),
        };
    }

    public override VirtualDisk OpenDisk(string path, FileAccess access, bool useAsync = false)
    {
        return new Disk(path, access, useAsync);
    }

    public override VirtualDisk OpenDisk(FileLocator locator, string path, FileAccess access)
    {
        return new Disk(locator, path, access);
    }

    public override VirtualDiskLayer OpenDiskLayer(FileLocator locator, string path, FileAccess access)
    {
        return new DiskImageFile(locator, path, access);
    }

    internal static VirtualDiskTypeInfo MakeDiskTypeInfo(string variant)
    {
        return new VirtualDiskTypeInfo
        {
            Name = "VHDX",
            Variant = variant,
            CanBeHardDisk = true,
            DeterministicGeometry = true,
            PreservesBiosGeometry = false,
            CalcGeometry = Geometry.FromCapacity
        };
    }
}