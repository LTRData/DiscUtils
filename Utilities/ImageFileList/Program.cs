//
// Copyright (c) 2026, Olof Lagerkvist
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DiscUtils;
using DiscUtils.Common;

namespace ImageFileList;

class Program : ProgramBase
{
    private CommandLineMultiParameter _diskFiles;
    private CommandLineParameter _DirPath;
    private CommandLineParameter _Pattern;
    private CommandLineSwitch _diskType;
    private CommandLineSwitch _Recurse;

    static void Main(string[] args)
    {
        DiscUtils.Containers.SetupHelper.SetupContainers();
        DiscUtils.FileSystems.SetupHelper.SetupFileSystems();
        DiscUtils.Transports.SetupHelper.SetupTransports();

        var program = new Program();
        program.Run(args);
    }

    protected override ProgramBase.StandardSwitches DefineCommandLine(CommandLineParser parser)
    {
        _diskFiles = FileOrUriMultiParameter("disk", "The disks to inspect.", false);
        _DirPath = new CommandLineParameter("dir_path", "The path to directory to list.", false);
        _Pattern = new CommandLineParameter("pattern", "File name pattern. Use * for all files and directories.", false);
        _diskType = new CommandLineSwitch("dt", "disktype", "type", $"Force the type of disk - use a file extension (one of {string.Join(", ", VirtualDiskManager.SupportedDiskTypes)})");
        _Recurse = new CommandLineSwitch("r", null, "Recurse into subdirectories");

        parser.AddMultiParameter(_diskFiles);
        parser.AddParameter(_DirPath);
        parser.AddParameter(_Pattern);
        parser.AddSwitch(_diskType);
        parser.AddSwitch(_Recurse);

        return StandardSwitches.UserAndPassword | StandardSwitches.PartitionOrVolume;
    }

    protected override void DoRun()
    {
        var volMgr = new VolumeManager();
        foreach (var path in _diskFiles.Values)
        {
            var disk = VirtualDisk.OpenDisk(path, _diskType.IsPresent ? _diskType.Value : null, FileAccess.Read, UserName, Password, useAsync: false);

            if (disk is null)
            {
                Console.Error.WriteLine($"Failed to open '{path}' as virtual disk.");
                continue;
            }

            volMgr.AddDisk(disk);
        }

        VolumeInfo volInfo;
        if (!string.IsNullOrEmpty(VolumeId))
        {
            volInfo = volMgr.GetVolume(VolumeId)
                ?? throw new DriveNotFoundException($"Volume {VolumeId} not found");
        }
        else if (Partition >= 0)
        {
            volInfo = volMgr.GetPhysicalVolumes().ElementAtOrDefault(Partition)
                ?? throw new DriveNotFoundException($"Partition {Partition} not found");
        }
        else
        {
            volInfo = volMgr.GetLogicalVolumes().FirstOrDefault()
                ?? throw new DriveNotFoundException("Logical volume not found");
        }

        var fsInfo = FileSystemManager.DetectFileSystems(volInfo).FirstOrDefault()
             ?? throw new DriveNotFoundException("No supported file system found");

        using var fs = fsInfo.Open(volInfo, FileSystemParameters);

        var dir = fs.GetDirectoryInfo(_DirPath.Value);

        DoDir(volInfo.Identity, fsInfo.Name, dir);
    }

    private void DoDir(string volId, string fsInfoName, DiscDirectoryInfo dir)
    {
        Console.WriteLine($"Listing directory '{dir.FullName}' on volume '{volId}' ({fsInfoName}):");

        Console.WriteLine($"{"Last Write Time",-19}  {"Length",16}  {"Alloc Length",18}  Name");

        foreach (var entry in dir.GetFileSystemInfos(_Pattern.IsPresent ? _Pattern.Value : "*"))
        {
            var fileLength = entry.Attributes.HasFlag(FileAttributes.Directory) ? "<DIR>" : entry.FileSystem.GetFileLength(entry.FullName).ToString("N0");

            var fileAllocLength = entry.FileSystem is IAllocationExtentsFileSystem allocFs
                ? $"({allocFs.PathToExtents(entry.FullName).Sum(extent => extent.Length).ToString("N0")})"
                : "";

            Console.WriteLine($"{entry.LastWriteTime}  {fileLength,16}  {fileAllocLength,18}  {entry.Name}");
        }

        Console.WriteLine();

        if (!_Recurse.IsPresent)
        {
            return;
        }

        foreach (var subdir in dir.GetDirectories())
        {
            DoDir(volId, fsInfoName, subdir);
        }
    }
}
