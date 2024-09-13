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
using System.Collections.Generic;
using System.IO;
using DiscUtils;
using DiscUtils.BootConfig;
using DiscUtils.Common;
using DiscUtils.Ntfs;
using DiscUtils.Partitions;
using DiscUtils.Registry;
using DiscUtils.Streams;

namespace OSClone;

class Program : ProgramBase
{
    // Shared to avoid continual re-allocation of a large buffer
    private static byte[] _copyBuffer = new byte[10 * 1024 * 1024];

    private static string[] _excludedFiles =
    [
        @"\PAGEFILE.SYS", @"\HIBERFIL.SYS", @"\SYSTEM VOLUME INFORMATION"
    ];

    private CommandLineParameter _sourceFile;
    private CommandLineParameter _destFile;
    private CommandLineSwitch _labelSwitch;

    private Dictionary<long, string> _uniqueFiles = [];

    static void Main(string[] args)
    {
        DiscUtils.Containers.SetupHelper.SetupContainers();
        var program = new Program();
        program.Run(args);
    }

    protected override StandardSwitches DefineCommandLine(CommandLineParser parser)
    {
        _sourceFile = FileOrUriParameter("in_file", "The disk image containing the Operating System image to be cloned.", false);
        _destFile = FileOrUriParameter("out_file", "The path to the output disk image.", false);
        _labelSwitch = new CommandLineSwitch("l", "label", "name", "The volume label for the NTFS file system created.");

        parser.AddParameter(_sourceFile);
        parser.AddParameter(_destFile);
        parser.AddSwitch(_labelSwitch);

        return StandardSwitches.OutputFormatAndAdapterType | StandardSwitches.UserAndPassword | StandardSwitches.DiskSize;
    }

    protected override void DoRun()
    {
        if (DiskSize <= 0)
        {
            DisplayHelp();
            return;
        }

        using var sourceDisk = VirtualDisk.OpenDisk(_sourceFile.Value, FileAccess.Read, UserName, Password, useAsync: false);

        if (sourceDisk is null)
        {
            Console.Error.WriteLine($"Failed to open '{_sourceFile.Value}' as virtual disk.");
            return;
        }

        using var destDisk = VirtualDisk.CreateDisk(OutputDiskType, OutputDiskVariant, _destFile.Value, DiskParameters, UserName, Password, useAsync: false);

        if (destDisk is DiscUtils.Vhd.Disk)
        {
            ((DiscUtils.Vhd.Disk)destDisk).AutoCommitFooter = false;
        }

        // Copy the MBR from the source disk, and invent a new signature for this new disk
        destDisk.SetMasterBootRecord(sourceDisk.GetMasterBootRecord());
        destDisk.Signature = new Random().Next();

        var sourcePartStream = SparseStream.FromStream(sourceDisk.Partitions[0].Open(), Ownership.None);
        var sourceNtfs = new NtfsFileSystem(sourcePartStream);

        // Copy the OS boot code into memory, so we can apply it when formatting the new disk
        byte[] bootCode;
        using (var bootStream = sourceNtfs.OpenFile("$Boot", FileMode.Open, FileAccess.Read))
        {
            bootCode = new byte[bootStream.Length];
            var totalRead = 0;
            while (totalRead < bootCode.Length)
            {
                totalRead += bootStream.Read(bootCode, totalRead, bootCode.Length - totalRead);
            }
        }

        // Partition the new disk with a single NTFS partition
        BiosPartitionTable.Initialize(destDisk, WellKnownPartitionType.WindowsNtfs);
        var volMgr = new VolumeManager(destDisk);

        var label = _labelSwitch.IsPresent ? _labelSwitch.Value : sourceNtfs.VolumeLabel;
        using var destNtfs = NtfsFileSystem.Format(volMgr.GetLogicalVolumes()[0], label, bootCode);
        destNtfs.SetSecurity(@"\", sourceNtfs.GetSecurity(@"\"));
        destNtfs.NtfsOptions.ShortNameCreation = ShortFileNameOption.Disabled;

        sourceNtfs.NtfsOptions.HideHiddenFiles = false;
        sourceNtfs.NtfsOptions.HideSystemFiles = false;
        CopyFiles(sourceNtfs, destNtfs, @"\", true);

        if (destNtfs.FileExists(@"\boot\BCD"))
        {
            // Force all boot entries in the BCD to point to the newly created NTFS partition - does _not_ cope with
            // complex multi-volume / multi-boot scenarios at all.
            using Stream bcdStream = destNtfs.OpenFile(@"\boot\BCD", FileMode.Open, FileAccess.ReadWrite);
            using var hive = new RegistryHive(bcdStream);
            var store = new Store(hive.Root);
            foreach (var obj in store.Objects)
            {
                foreach (var elem in obj.Elements)
                {
                    if (elem.Format == ElementFormat.Device)
                    {
                        elem.Value = ElementValue.ForDevice(elem.Value.ParentObject, volMgr.GetPhysicalVolumes()[0]);
                    }
                }
            }
        }
    }

    private void CopyFiles(NtfsFileSystem sourceNtfs, NtfsFileSystem destNtfs, string path, bool subs)
    {
        if (subs)
        {
            foreach (var dir in sourceNtfs.GetDirectories(path))
            {
                if (!IsExcluded(dir))
                {
                    var hardLinksRemaining = sourceNtfs.GetHardLinkCount(dir) - 1;
                    var newDir = false;

                    var sourceFileId = sourceNtfs.GetFileId(dir);
                    if (_uniqueFiles.TryGetValue(sourceFileId, out var refPath))
                    {
                        // If this is another name for a known dir, recreate the hard link
                        destNtfs.CreateHardLink(refPath, dir);
                    }
                    else
                    {
                        destNtfs.CreateDirectory(dir);
                        newDir = true;

                        var fileAttrs = sourceNtfs.GetAttributes(dir);
                        if ((fileAttrs & FileAttributes.ReparsePoint) != 0)
                        {
                            destNtfs.SetReparsePoint(dir, sourceNtfs.GetReparsePoint(dir));
                        }

                        destNtfs.SetAttributes(dir, fileAttrs);

                        destNtfs.SetSecurity(dir, sourceNtfs.GetSecurity(dir));
                    }

                    // File may have a short name
                    var shortName = sourceNtfs.GetShortName(dir);
                    if (!string.IsNullOrEmpty(shortName) && shortName != dir)
                    {
                        destNtfs.SetShortName(dir, shortName);
                        --hardLinksRemaining;
                    }

                    if (newDir)
                    {
                        if (hardLinksRemaining > 0)
                        {
                            _uniqueFiles[sourceFileId] = dir;
                        }

                        CopyFiles(sourceNtfs, destNtfs, dir, subs);
                    }

                    // Set standard information last (includes modification timestamps)
                    destNtfs.SetFileStandardInformation(dir, sourceNtfs.GetFileStandardInformation(dir));
                }
            }
        }

        foreach (var file in sourceNtfs.GetFiles(path))
        {
            Console.WriteLine(file);

            var hardLinksRemaining = sourceNtfs.GetHardLinkCount(file) - 1;

            var sourceFileId = sourceNtfs.GetFileId(file);

            if (_uniqueFiles.TryGetValue(sourceFileId, out var refPath))
            {
                // If this is another name for a known file, recreate the hard link
                destNtfs.CreateHardLink(refPath, file);
            }
            else
            {
                CopyFile(sourceNtfs, destNtfs, file);

                if (hardLinksRemaining > 0)
                {
                    _uniqueFiles[sourceFileId] = file;
                }
            }

            // File may have a short name
            var shortName = sourceNtfs.GetShortName(file);
            if (!string.IsNullOrEmpty(shortName) && shortName != file)
            {
                destNtfs.SetShortName(file, shortName);
            }
        }
    }

    private static void CopyFile(NtfsFileSystem sourceNtfs, NtfsFileSystem destNtfs, string path)
    {
        if (IsExcluded(path))
        {
            return;
        }

        using (var s = sourceNtfs.OpenFile(path, FileMode.Open, FileAccess.Read))
        using (var d = destNtfs.OpenFile(path, FileMode.Create, FileAccess.ReadWrite))
        {
            d.SetLength(s.Length);
            var numRead = s.Read(_copyBuffer, 0, _copyBuffer.Length);
            while (numRead > 0)
            {
                d.Write(_copyBuffer, 0, numRead);
                numRead = s.Read(_copyBuffer, 0, _copyBuffer.Length);
            }
        }

        destNtfs.SetSecurity(path, sourceNtfs.GetSecurity(path));
        destNtfs.SetFileStandardInformation(path, sourceNtfs.GetFileStandardInformation(path));
    }

    private static bool IsExcluded(string path)
    {
        var pathUpper = path.ToUpperInvariant();

        for (var i = 0; i < _excludedFiles.Length; ++i)
        {
            if (pathUpper == _excludedFiles[i])
            {
                return true;
            }
        }

        return false;
    }
}
