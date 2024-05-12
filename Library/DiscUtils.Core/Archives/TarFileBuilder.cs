//
// Copyright (c) 2008-2024, Kenneth Bell, Olof Lagerkvist and contributors
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


using DiscUtils.Streams;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace DiscUtils.Archives;
/// <summary>
/// Builder to create UNIX Tar archive files.
/// </summary>
public class TarFileBuilder : StreamBuilder
{
    private readonly List<UnixBuildFileRecord> _files;

    /// <summary>
    /// Initializes a new instance of the <see cref="TarFileBuilder"/> class.
    /// </summary>
    public TarFileBuilder()
    {
        _files = [];
    }

    public bool Exists(string name)
    {
        var nameSpan = name.AsSpan().TrimEnd('/');

        foreach (var file in _files)
        {
            if (nameSpan.Equals(file.Name.AsSpan().TrimEnd('/'), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public long TotalSize
    {
        get
        {
            var size = 0L;

            foreach (var file in _files)
            {
                size += file.Length + 512;
            }

            return size;
        }
    }

    public int FileCount => _files.Count;

    protected virtual void AddFile(UnixBuildFileRecord file)
    {
        _files.Add(file);
    }

    /// <summary>
    /// Add a directory to the tar archive.
    /// </summary>
    /// <param name="name">The name of the directory.</param>
    public void AddDirectory(string name)
    {
        if (name.Length < 1 || name[name.Length - 1] != '/')
        {
            name += "/";
        }

        AddFile(name, Array.Empty<byte>());
    }

    /// <summary>
    /// Add a directory to the tar archive.
    /// </summary>
    /// <param name="name">The name of the directory.</param>
    /// <param name="ownerId">The uid of the owner.</param>
    /// <param name="groupId">The gid of the owner.</param>
    /// <param name="fileMode">The access mode of the directory.</param>
    /// <param name="modificationTime">The modification time for the directory.</param>
    public void AddDirectory(
        string name, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime)
    {
        if (name.Length < 1 || name[name.Length - 1] != '/')
        {
            name += "/";
        }

        AddFile(name, Array.Empty<byte>(), ownerId, groupId, fileMode, modificationTime);
    }

    /// <summary>
    /// Add a file to the tar archive.
    /// </summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="buffer">The file data.</param>
    public void AddFile(string name, byte[] buffer)
    {
        AddFile(new UnixBuildFileRecord(name, buffer));
    }

    /// <summary>
    /// Add a file to the tar archive.
    /// </summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="sourcefile">The file to add.</param>
    public void AddFile(string name, string sourcefile)
    {
        AddFile(new UnixBuildFileRecord(name, File.ReadAllBytes(sourcefile)));
    }

    /// <summary>
    /// Add a file to the tar archive.
    /// </summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="buffer">The file data.</param>
    /// <param name="ownerId">The uid of the owner.</param>
    /// <param name="groupId">The gid of the owner.</param>
    /// <param name="fileMode">The access mode of the file.</param>
    /// <param name="modificationTime">The modification time for the file.</param>
    public void AddFile(
        string name, byte[] buffer, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime)
    {
        AddFile(new UnixBuildFileRecord(name, buffer, fileMode, ownerId, groupId, modificationTime));
    }

    /// <summary>
    /// Add a file to the tar archive.
    /// </summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="sourcefile">The file to add.</param>
    /// <param name="ownerId">The uid of the owner.</param>
    /// <param name="groupId">The gid of the owner.</param>
    /// <param name="fileMode">The access mode of the file.</param>
    /// <param name="modificationTime">The modification time for the file.</param>
    public void AddFile(
        string name, string sourcefile, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime)
    {
        AddFile(new UnixBuildFileRecord(name, File.ReadAllBytes(sourcefile), fileMode, ownerId, groupId, modificationTime));
    }

    /// <summary>
    /// Add a file to the tar archive.
    /// </summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="stream">The file data.</param>
    public void AddFile(string name, Stream stream)
    {
        AddFile(new UnixBuildFileRecord(name, stream));
    }

    /// <summary>
    /// Add a file to the tar archive.
    /// </summary>
    /// <param name="name">The name of the file.</param>
    /// <param name="stream">The file data.</param>
    /// <param name="ownerId">The uid of the owner.</param>
    /// <param name="groupId">The gid of the owner.</param>
    /// <param name="fileMode">The access mode of the file.</param>
    /// <param name="modificationTime">The modification time for the file.</param>
    public void AddFile(
        string name, Stream stream, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime)
    {
        AddFile(new UnixBuildFileRecord(name, stream, fileMode, ownerId, groupId, modificationTime));
    }

    protected override List<BuilderExtent> FixExtents(out long totalLength)
    {
        var result = new List<BuilderExtent>((_files.Count * 2) + 2);
        long pos = 0;

        foreach (var file in _files)
        {
            var fileContentExtent = file.Fix(pos + TarHeader.Length);

            result.Add(new TarHeaderExtent(
                pos, file.Name, fileContentExtent.Length, file.FileMode, file.OwnerId, file.GroupId, file.ModificationTime));
            pos += TarHeader.Length;

            result.Add(fileContentExtent);
            pos += MathUtilities.RoundUp(fileContentExtent.Length, 512);
        }

        // Two empty 512-byte blocks at end of tar file.
        result.Add(new BuilderBufferExtent(pos, new byte[1024]));

        totalLength = pos + 1024;
        return result;
    }
}
