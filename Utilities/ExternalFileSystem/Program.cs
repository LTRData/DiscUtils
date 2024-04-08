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
using System.Linq;
using System.Text;
using DiscUtils;
using DiscUtils.Internal;
using DiscUtils.Setup;
using DiscUtils.Streams;
using DiscUtils.Vfs;
using LTRData.Extensions.Buffers;

namespace ExternalFileSystem;

class Program
{
    static void Main(string[] args)
    {
        SetupHelper.RegisterAssembly(typeof(Program).Assembly);

        var dummyFileSystemData = new MemoryStream(Encoding.ASCII.GetBytes("MYFS"));
        
        VirtualDisk dummyDisk = new DiscUtils.Raw.Disk(dummyFileSystemData, Ownership.None);
        var volMgr = new VolumeManager(dummyDisk);

        VolumeInfo volInfo = volMgr.GetLogicalVolumes()[0];
        var fsInfo = FileSystemManager.DetectFileSystems(volInfo)[0];

        var fs = fsInfo.Open(volInfo);
        ShowDir(fs.Root, 4);
    }

    private static void ShowDir(DiscDirectoryInfo dirInfo, int indent)
    {
        var indentStr = new string(' ', indent);
        Console.WriteLine($"{indentStr}{dirInfo.FullName,-50} [{dirInfo.CreationTimeUtc}]");
        foreach (var subDir in dirInfo.GetDirectories())
        {
            ShowDir(subDir, indent + 0);
        }

        foreach (var file in dirInfo.GetFiles())
        {
            Console.WriteLine($"{indentStr}{file.FullName,-50} [{file.CreationTimeUtc}]");
        }
    }
}

class MyDirEntry : VfsDirEntry
{
    private static long _nextId;

    private bool _isDir;
    private string _name;
    private long _id;

    public MyDirEntry(string name, bool isDir)
    {
        _name = name;
        _isDir = isDir;
        _id = _nextId++;
    }

    public override bool IsDirectory
    {
        get { return _isDir; }
    }

    public override bool IsSymlink
    {
        get { return false; }
    }

    public override string FileName
    {
        get { return _name; }
    }

    public override bool HasVfsTimeInfo
    {
        get { return true; }
    }

    public override DateTime LastAccessTimeUtc
    {
        get { return new DateTime(1980, 10, 21, 11, 04, 22); }
    }

    public override DateTime LastWriteTimeUtc
    {
        get { return new DateTime(1980, 10, 21, 11, 04, 22); }
    }

    public override DateTime CreationTimeUtc
    {
        get { return new DateTime(1980, 10, 21, 11, 04, 22); }
    }

    public override bool HasVfsFileAttributes
    {
        get { return true; }
    }

    public override FileAttributes FileAttributes
    {
        get
        {
            return IsDirectory ? FileAttributes.Directory : FileAttributes.Normal;
        }
    }

    public override long UniqueCacheId
    {
        get { return _id; }
    }
}

class MyFile : IVfsFile
{
    private MyDirEntry _dirEntry;

    public MyFile(MyDirEntry dirEntry)
    {
        _dirEntry = dirEntry;
    }

    public DateTime LastAccessTimeUtc
    {
        get
        {
            return _dirEntry.LastAccessTimeUtc;
        }
        set
        {
            throw new NotSupportedException();
        }
    }

    public DateTime LastWriteTimeUtc
    {
        get
        {
            return _dirEntry.LastWriteTimeUtc;
        }
        set
        {
            throw new NotImplementedException();
        }
    }

    public DateTime CreationTimeUtc
    {
        get
        {
            return _dirEntry.CreationTimeUtc;
        }
        set
        {
            throw new NotImplementedException();
        }
    }

    public FileAttributes FileAttributes
    {
        get
        {
            return _dirEntry.FileAttributes;
        }
        set
        {
            throw new NotImplementedException();
        }
    }

    public long FileLength
    {
        get { return 10; }
    }

    public IBuffer FileContent
    {
        get
        {
            var result = new SparseMemoryBuffer(10);
            result.Write(0, new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, 0, 10);
            return result;
        }
    }

    IEnumerable<StreamExtent> IVfsFile.EnumerateAllocationExtents() => throw new NotImplementedException();
}

class MyDirectory : MyFile, IVfsDirectory<MyDirEntry, MyFile>
{
    private readonly FastDictionary<MyDirEntry> _entries;

    public MyDirectory(MyDirEntry dirEntry, bool isRoot)
        : base(dirEntry)
    {
        _entries = new(StringComparer.OrdinalIgnoreCase, entry => entry.FileName);

        if (isRoot)
        {
            for (var i = 0; i < 4; ++i)
            {
                _entries.Add(new MyDirEntry($"DIR{i}", true));
            }
        }

        for (var i = 0; i < 6; ++i)
        {
            _entries.Add(new MyDirEntry($"FILE{i}", false));
        }
    }

    public IReadOnlyDictionary<string, MyDirEntry> AllEntries
    {
        get { return _entries; }
    }

    public MyDirEntry Self
    {
        get { return null; }
    }

    public MyDirEntry GetEntryByName(string name)
        => AllEntries.TryGetValue(name, out var entry) ? entry : null;

    public MyDirEntry CreateNewFile(string name)
    {
        throw new NotSupportedException();
    }
}

class MyContext : VfsContext
{
}

class MyFileSystem : VfsFileSystem<MyDirEntry, MyFile, MyDirectory, MyContext>
{
    public MyFileSystem()
        : base(new DiscFileSystemOptions())
    {
        this.Context = new MyContext();
        this.RootDirectory = new MyDirectory(new MyDirEntry("", true), true);
    }

    public override bool IsCaseSensitive => false;

    public override string VolumeLabel
    {
        get { return "Volume Label"; }
    }

    protected override MyFile ConvertDirEntryToFile(MyDirEntry dirEntry)
    {
        if (dirEntry.IsDirectory)
        {
            return new MyDirectory(dirEntry, false);
        }
        else
        {
            return new MyFile(dirEntry);
        }
    }

    public override string FriendlyName
    {
        get { return "My File System"; }
    }

    public override bool CanWrite
    {
        get { return false; }
    }

    public override long Size => throw new NotImplementedException();

    public override long UsedSpace => throw new NotImplementedException();

    public override long AvailableSpace => throw new NotImplementedException();
}

[VfsFileSystemFactory]
class MyFileSystemFactory : VfsFileSystemFactory
{
    public override IEnumerable<DiscUtils.FileSystemInfo> Detect(Stream stream, VolumeInfo volumeInfo)
    {
        var header = new byte[4];
        stream.Read(header, 0, 4);

        if (Encoding.ASCII.GetString(header, 0, 4) == "MYFS")
        {
            return SingleValueEnumerable.Get(new VfsFileSystemInfo("MyFs", "My File System", Open));
        }

        return Enumerable.Empty<DiscUtils.FileSystemInfo>();
    }

    private DiscFileSystem Open(Stream stream, VolumeInfo volInfo, FileSystemParameters parameters)
    {
        return new MyFileSystem();
    }
}
