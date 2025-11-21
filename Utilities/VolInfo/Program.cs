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
using System.IO;
using DiscUtils;
using DiscUtils.Common;
using LTRData.Extensions.Formatting;

namespace VolInfo;

class Program : ProgramBase
{
    private CommandLineMultiParameter _inFiles;

    static void Main(string[] args)
    {
        DiscUtils.Transports.SetupHelper.SetupTransports();
        DiscUtils.Containers.SetupHelper.SetupContainers();

        var program = new Program();
        program.Run(args);
    }

    protected override ProgramBase.StandardSwitches DefineCommandLine(CommandLineParser parser)
    {
        _inFiles = FileOrUriMultiParameter("disk", "Paths to the disks to inspect.", false);

        parser.AddMultiParameter(_inFiles);

        return StandardSwitches.UserAndPassword;
    }

    protected override void DoRun()
    {
        DiscUtils.Containers.SetupHelper.SetupContainers();
        DiscUtils.Transports.SetupHelper.SetupTransports();

        var volMgr = new VolumeManager();
        foreach (var path in _inFiles.Values)
        {
            var disk = VirtualDisk.OpenDisk(path, FileAccess.Read, UserName, Password, useAsync: false);

            if (disk is null)
            {
                Console.Error.WriteLine($"Failed to open '{path}' as virtual disk.");
                continue;
            }

            volMgr.AddDisk(disk);
        }

        Console.WriteLine("PHYSICAL VOLUMES");
        foreach (var physVol in volMgr.GetPhysicalVolumes())
        {
            Console.WriteLine($"      Identity: {physVol.Identity}");
            Console.WriteLine($"          Type: {physVol.VolumeType}");
            Console.WriteLine($"       Disk Id: {physVol.DiskIdentity}");
            Console.WriteLine($"      Disk Sig: {physVol.DiskSignature:X8}");
            Console.WriteLine($"       Part Id: {physVol.PartitionIdentity}");
            Console.WriteLine($"        Length: {physVol.Length} bytes ({SizeFormatting.FormatBytes(physVol.Length)})");
            Console.WriteLine($" Disk Geometry: {physVol.PhysicalGeometry}");
            Console.WriteLine($"  First Sector: {physVol.PhysicalStartSector}");
            Console.WriteLine();
        }

        Console.WriteLine("LOGICAL VOLUMES");
        foreach (var logVol in volMgr.GetLogicalVolumes())
        {
            Console.WriteLine($"      Identity: {logVol.Identity}");
            Console.WriteLine($"        Length: {logVol.Length} bytes ({SizeFormatting.FormatBytes(logVol.Length)})");
            Console.WriteLine($" Disk Geometry: {logVol.PhysicalGeometry}");
            Console.WriteLine($"  First Sector: {logVol.PhysicalStartSector}");
            Console.WriteLine();
        }
    }

}
