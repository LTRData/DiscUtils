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

using System;
using System.IO;

namespace DiscUtils;

public interface IFileSystemBuilder
{
    int FileCount { get; }

    long TotalSize { get; }

    string VolumeIdentifier { get; set; }

    event EventHandler<ProgressEventArgs> ProgressChanged;

    void AddDirectory(string name);

    void AddDirectory(string name, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes);

    void AddDirectory(string name, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime);

    void AddFile(string name, byte[] content);

    void AddFile(string name, Stream source);

    void AddFile(string name, string sourcePath);

    void AddFile(string name, byte[] content, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes);

    void AddFile(string name, Stream source, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes);

    void AddFile(string name, string sourcePath, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes);

    void AddFile(string name, byte[] content, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime);

    void AddFile(string name, Stream source, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime);

    void AddFile(string name, string sourcePath, int ownerId, int groupId, UnixFilePermissions fileMode, DateTime modificationTime);

    bool Exists(string path);

    IFileSystem GenerateFileSystem();
}