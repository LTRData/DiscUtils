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
using System.Linq;
using DiscUtils;
using DiscUtils.Common;

namespace FileExtract;

class Program : ProgramBase
{
    private CommandLineMultiParameter _diskFiles;
    private CommandLineParameter _inFilePath;
    private CommandLineParameter _outFilePath;
    private CommandLineSwitch _diskType;
    private CommandLineSwitch _hexDump;

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
        _inFilePath = new CommandLineParameter("file_path", "The path of the file to extract.", false);
        _outFilePath = new CommandLineParameter("out_file", "The output file to be written.", false);
        _diskType = new CommandLineSwitch("dt", "disktype", "type", $"Force the type of disk - use a file extension (one of {string.Join(", ", VirtualDiskManager.SupportedDiskTypes)})");
        _hexDump = new CommandLineSwitch("hd", "hexdump", null, "Output a HexDump of the NTFS stream to the console, in addition to writing it to the output file.");

        parser.AddMultiParameter(_diskFiles);
        parser.AddParameter(_inFilePath);
        parser.AddParameter(_outFilePath);
        parser.AddSwitch(_diskType);
        parser.AddSwitch(_hexDump);

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
        using var source = fs.OpenFile(_inFilePath.Value, FileMode.Open, FileAccess.Read);
        
        using (var outFile = new FileStream(_outFilePath.Value, FileMode.Create, FileAccess.ReadWrite, FileShare.Delete, bufferSize: 2 << 20))
        {
            source.CopyTo(outFile);
        }

        if (_hexDump.IsPresent)
        {
            source.Position = 0;
            HexDump.Generate(source, Console.Out);
        }
    }
}
