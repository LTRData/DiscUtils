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
using DiscUtils.Ntfs;
using LTRData.Extensions.Formatting;

namespace ImageFileList;

class Program : ProgramBase
{
    private CommandLineMultiParameter _diskFiles;
    private CommandLineParameter _DirPath;
    private CommandLineParameter _Pattern;
    private CommandLineSwitch _diskType;
    private CommandLineSwitch _Recurse;
    private CommandLineSwitch _Short;

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
        _Short = new CommandLineSwitch("s", null, "Short listing - only full paths");

        parser.AddMultiParameter(_diskFiles);
        parser.AddParameter(_DirPath);
        parser.AddParameter(_Pattern);
        parser.AddSwitch(_diskType);
        parser.AddSwitch(_Recurse);
        parser.AddSwitch(_Short);

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

        if (fs is NtfsFileSystem ntfs)
        {
            ntfs.NtfsOptions.HideHiddenFiles = false;
            ntfs.NtfsOptions.HideSystemFiles = false;
            ntfs.NtfsOptions.HideMetafiles = false;
        }

        var dir = fs.GetDirectoryInfo(_DirPath.Value);

        DoDir(volInfo.Identity, fsInfo.Name, dirs: new(), dir);
    }

    private void DoDir(string volId, string fsInfoName, Dictionary<long, string> dirs, DiscDirectoryInfo dir)
    {
        if (dir.Name is "." or "..")
        {
            return;
        }

        var headerPrinted = false;

        if (!_Short.IsPresent && !_Recurse.IsPresent)
        {
            Console.WriteLine($"Listing directory '{dir.FullName}' on volume '{volId}' ({fsInfoName}):");

            Console.WriteLine($"{"Last Write Time",-22}  {"Length",16}  {"Alloc Length",18}  Name");

            headerPrinted = true;
        }

        var unixFs = dir.FileSystem as IUnixFileSystem;

        var allocFs = dir.FileSystem as IAllocationExtentsFileSystem;

        var altstrFs = dir.FileSystem as IFileSystemWithAltStreams;

        if (unixFs?.GetUnixFileInfo(dir.FullName) is { Inode: not 0 } dirUnixInfo)
        {
            if (dirs.TryGetValue(dirUnixInfo.Inode, out var existingPath))
            {
                if (!_Short.IsPresent)
                {
                    Console.WriteLine($"Directory '{dir.FullName}' linked to '{existingPath}' (inode {dirUnixInfo.Inode})");
                    Console.WriteLine();
                }

                return;
            }

            dirs[dirUnixInfo.Inode] = dir.FullName;
        }

        foreach (var entry in dir.GetFileSystemInfos(_Pattern.IsPresent ? _Pattern.Value : "*"))
        {
            try
            {
                var isDir = entry.Attributes.HasFlag(FileAttributes.Directory);

                if (_Short.IsPresent)
                {
                    if (isDir)
                    {
                        if (unixFs?.GetUnixFileInfo(entry.FullName) is { Inode: not 0 } unixInfo
                            && dirs.TryGetValue(unixInfo.Inode, out var existingPath))
                        {
                            Console.WriteLine($"{entry.FullName} -> {existingPath}");
                            continue;
                        }

                        Console.WriteLine($"{entry.FullName}{Path.DirectorySeparatorChar}");
                    }
                    else if (entry.FileSystem.FileExists(entry.FullName))
                    {
                        Console.WriteLine(entry.FullName);
                    }

                    if (altstrFs is not null)
                    {
                        foreach (var altStream in altstrFs.GetAlternateDataStreams(entry.FullName))
                        {
                            Console.WriteLine($"{entry.FullName}:{altStream}");
                        }
                    }

                    continue;
                }

                if (!headerPrinted)
                {
                    Console.WriteLine($"Listing directory '{dir.FullName}' on volume '{volId}' ({fsInfoName}):");

                    Console.WriteLine($"{"Last Write Time",-22}  {"Length",16}  {"Alloc Length",18}  Name");

                    headerPrinted = true;
                }

                if (isDir || entry.FileSystem.FileExists(entry.FullName))
                {
                    var fileLength = isDir
                        ? "<DIR>"
                        : entry.FileSystem.GetFileLength(entry.FullName).ToString("N0");

                    var fileAllocLength = !isDir && allocFs is not null
                        ? $"({allocFs.PathToExtents(entry.FullName).Sum(extent => extent.Length):N0})"
                        : "";

                    Console.WriteLine($"{entry.LastWriteTime,-22}  {fileLength,16}  {fileAllocLength,18}  {entry.Name}");
                }

                if (entry.FileSystem is IFileSystemWithAltStreams altstrfs)
                {
                    foreach (var altStream in altstrfs.GetAlternateDataStreams(entry.FullName))
                    {
                        var altStreamPath = $"{entry.FullName}:{altStream}";
                        var altStreamLength = altstrfs.GetFileLength(altStreamPath).ToString("N0");
                        var altStreamAllocLength = allocFs is not null
                            ? $"({allocFs.PathToExtents(altStreamPath).Sum(extent => extent.Length):N0})"
                            : "";

                        Console.WriteLine($"{entry.LastWriteTime,-22}  {altStreamLength,16}  {altStreamAllocLength,18}  {entry.Name}:{altStream}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Failed to get info for '{entry.FullName}': {ex.JoinMessages()}");
                Console.ResetColor();
            }
        }

        if (headerPrinted && !_Short.IsPresent)
        {
            Console.WriteLine();
        }

        if (!_Recurse.IsPresent)
        {
            return;
        }

        foreach (var subdir in dir.GetDirectories())
        {
            DoDir(volId, fsInfoName, dirs, subdir);
        }
    }
}
