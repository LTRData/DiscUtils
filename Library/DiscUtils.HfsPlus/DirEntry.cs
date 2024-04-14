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
using DiscUtils.Internal;
using DiscUtils.Streams;
using DiscUtils.Vfs;

namespace DiscUtils.HfsPlus;

internal sealed class DirEntry : VfsDirEntry
{
    public DirEntry(string name, byte[] dirEntryData)
    {
        FileName = name;
        CatalogFileInfo = ParseDirEntryData(dirEntryData);
    }

    public CommonCatalogFileInfo CatalogFileInfo { get; }

    public override DateTime CreationTimeUtc => CatalogFileInfo.CreateTime;

    public override FileAttributes FileAttributes => Utilities.FileAttributesFromUnixFileType(CatalogFileInfo.FileSystemInfo.FileType);

    public override string FileName { get; }

    public override bool HasVfsFileAttributes => true;

    public override bool HasVfsTimeInfo => true;

    public override bool IsDirectory => CatalogFileInfo.RecordType == CatalogRecordType.FolderRecord;

    public override bool IsSymlink => !IsDirectory
                && (FileTypeFlags)((CatalogFileInfo)CatalogFileInfo).FileInfo.FileType == FileTypeFlags.SymLinkFileType;

    public override DateTime LastAccessTimeUtc => CatalogFileInfo.AccessTime;

    public override DateTime LastWriteTimeUtc => CatalogFileInfo.ContentModifyTime;

    public CatalogNodeId NodeId => CatalogFileInfo.FileId;

    public override long UniqueCacheId => CatalogFileInfo.FileId;

    internal static bool IsFileOrDirectory(byte[] dirEntryData)
    {
        var type = (CatalogRecordType)EndianUtilities.ToInt16BigEndian(dirEntryData, 0);
        return type is CatalogRecordType.FolderRecord or CatalogRecordType.FileRecord;
    }

    private static CommonCatalogFileInfo ParseDirEntryData(byte[] dirEntryData)
    {
        var type = (CatalogRecordType)EndianUtilities.ToInt16BigEndian(dirEntryData, 0);
        CommonCatalogFileInfo result = type switch
        {
            CatalogRecordType.FolderRecord => new CatalogDirInfo(),
            CatalogRecordType.FileRecord => new CatalogFileInfo(),
            _ => throw new NotImplementedException($"Unknown catalog record type: {type}"),
        };
        result.ReadFrom(dirEntryData);
        return result;
    }
}