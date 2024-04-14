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
using DiscUtils.Ntfs;
using DiscUtils.Ntfs.Internals;
using DiscUtils.Streams;

namespace FileRecover;

class Program : ProgramBase
{
    private CommandLineMultiParameter _diskFiles;
    private CommandLineSwitch _recoverFile;
    private CommandLineSwitch _showMeta;
    private CommandLineSwitch _showZeroSize;

    static void Main(string[] args)
    {
        DiscUtils.Containers.SetupHelper.SetupContainers();
        var program = new Program();
        program.Run(args);
    }

    protected override ProgramBase.StandardSwitches DefineCommandLine(CommandLineParser parser)
    {
        _diskFiles = FileOrUriMultiParameter("disk", "The disks to inspect.", false);
        _recoverFile = new CommandLineSwitch("r", "recover", "file_index", "Tries to recover the file at MFT index file_index from the disk.");
        _showMeta = new CommandLineSwitch("M", "meta", null, "Don't hide files and directories that are part of the file system itself.");
        _showZeroSize = new CommandLineSwitch("Z", "empty", null, "Don't hide files shown as zero size.");

        parser.AddMultiParameter(_diskFiles);
        parser.AddSwitch(_recoverFile);
        parser.AddSwitch(_showMeta);
        parser.AddSwitch(_showZeroSize);

        return StandardSwitches.UserAndPassword | StandardSwitches.PartitionOrVolume;
    }

    protected override string[] HelpRemarks => new string[] {
                "By default this utility shows the files that it may be possible to recover from an NTFS formatted virtual disk.  Once a "
                + "candidate file is determined, use the '-r' option to attempt recovery of the file's contents." };

    protected override void DoRun()
    {
        var volMgr = new VolumeManager();
        foreach (var path in _diskFiles.Values)
        {
            var disk = VirtualDisk.OpenDisk(path, FileAccess.Read, UserName, Password, useAsync: false);

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
            volInfo = volMgr.GetVolume(VolumeId);
        }
        else if (Partition >= 0)
        {
            volInfo = volMgr.GetPhysicalVolumes()[Partition];
        }
        else
        {
            volInfo = volMgr.GetLogicalVolumes()[0];
        }

        using var fs = new NtfsFileSystem(volInfo.Open());
        var mft = fs.GetMasterFileTable();
        if (_recoverFile.IsPresent)
        {
            var entry = mft[long.Parse(_recoverFile.Value)];
            var content = GetContent(entry);
            if (content == null)
            {
                Console.WriteLine("Sorry, unable to recover content");
                Environment.Exit(1);
            }

            var outFile = $"{_recoverFile.Value}__recovered.bin";
            if (File.Exists(outFile))
            {
                Console.WriteLine($"Sorry, the file already exists: {outFile}");
                Environment.Exit(1);
            }

            using (var outFileStream = new FileStream(outFile, FileMode.CreateNew, FileAccess.Write, FileShare.Delete, bufferSize: 2 << 20))
            {
                Pump(content, outFileStream);
            }

            Console.WriteLine($"Possible file contents saved as: {outFile}");
            Console.WriteLine();
            Console.WriteLine("Caution! It is rare for the file contents of deleted files to be intact - most");
            Console.WriteLine("likely the contents recovered are corrupt as the space has been reused.");
        }
        else
        {
            foreach (var entry in mft.GetEntries(EntryStates.NotInUse))
            {
                // Skip entries with no attributes, they've probably never been used.  We're certainly
                // not going to manage to recover any useful data from them.
                if (!entry.HasAttributes())
                {
                    continue;
                }

                // Skip directories - any useful files inside will be found separately
                if ((entry.Flags & MasterFileTableEntryFlags.IsDirectory) != 0)
                {
                    continue;
                }

                var size = GetSize(entry);
                var path = GetPath(mft, entry);

                if (!_showMeta.IsPresent && path.StartsWith(@"<root>\$Extend"))
                {
                    continue;
                }

                if (!_showZeroSize.IsPresent && size == 0)
                {
                    continue;
                }

                Console.WriteLine("Index: {0,-4}   Size: {1,-8}   Path: {2}", entry.Index, Utilities.ApproximateDiskSize(size), path);
            }
        }
    }

    static IBuffer GetContent(MasterFileTableEntry entry) =>
        entry.EnumerateAttributes(AttributeType.Data).FirstOrDefault()?.Content;

    static long GetSize(MasterFileTableEntry entry)
    {
        foreach (var fna in entry.EnumerateAttributes(AttributeType.FileName).OfType<FileNameAttribute>())
        {
            return fna.RealSize;
        }

        return 0;
    }

    static string GetPath(MasterFileTable mft, MasterFileTableEntry entry)
    {
        if (entry.SequenceNumber == MasterFileTable.RootDirectoryIndex)
        {
            return "<root>";
        }

        FileNameAttribute fna = null;

        foreach (var thisFna in entry.EnumerateAttributes(AttributeType.FileName).OfType<FileNameAttribute>())
        {
            if (fna == null || thisFna.FileNameNamespace == NtfsNamespace.Win32 || thisFna.FileNameNamespace == NtfsNamespace.Win32AndDos)
            {
                fna = thisFna;
            }
        }

        if (fna == null)
        {
            return "<unknown>";
        }

        var parentPath = "<unknown>";
        var parentEntry = mft[fna.ParentDirectory.RecordIndex];
        if (parentEntry != null)
        {
            if (parentEntry.SequenceNumber == fna.ParentDirectory.RecordSequenceNumber || parentEntry.SequenceNumber == fna.ParentDirectory.RecordSequenceNumber + 1)
            {
                parentPath = GetPath(mft, parentEntry);
            }
        }

        return parentPath + Path.DirectorySeparatorChar + fna.FileName;
    }

    private static void Pump(IBuffer inBuffer, Stream outStream)
    {
        long inPos = 0;
        var buffer = new byte[4096];
        var bytesRead = inBuffer.Read(inPos, buffer, 0, 4096);
        while (bytesRead != 0 && inPos < inBuffer.Capacity)
        {
            inPos += bytesRead;
            outStream.Write(buffer, 0, bytesRead);
            bytesRead = inBuffer.Read(inPos, buffer, 0, 4096);
        }
    }
}
