using System;

using DiscUtils;
using DiscUtils.Archives;
using DiscUtils.Internal;
using System.IO;
using System.Collections.Specialized;
using System.Linq;
using LTRData.Extensions.Buffers;

namespace DiscUtils.VirtualFileSystem;
public class TarFileSystemBuilder : TarFileBuilder, IFileSystemBuilder
{
    // Progress reporting event
    public event EventHandler<ProgressEventArgs> ProgressChanged;

    private ProgressEventArgs progressEventArgs;

    protected override void AddFile(UnixBuildFileRecord file)
    {
        base.AddFile(file);

        if (ProgressChanged is not null)
        {
            progressEventArgs ??= new();
            progressEventArgs.TotalFiles += file.Name.EndsWith('/') ? 0 : 1;
            progressEventArgs.TotalItems = FileCount;
            ProgressChanged(this, progressEventArgs);
        }
    }

    public string VolumeIdentifier { get; set; }

    public IFileSystem GenerateFileSystem() => new TarFileSystem(Build(), VolumeIdentifier, ownsStream: true);

    void IFileSystemBuilder.AddDirectory(
       string name, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
    {
        AddDirectory(name, 0, 0, Utilities.UnixFilePermissionsFromFileAttributes(attributes), writtenTime);
    }

    void IFileSystemBuilder.AddFile(string name, byte[] buffer, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
    {
        AddFile(name, buffer, 0, 0, Utilities.UnixFilePermissionsFromFileAttributes(attributes), writtenTime);
    }

    void IFileSystemBuilder.AddFile(string name, string sourcefile, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
    {
        AddFile(name, File.ReadAllBytes(sourcefile), 0, 0, Utilities.UnixFilePermissionsFromFileAttributes(attributes), writtenTime);
    }

    void IFileSystemBuilder.AddFile(string name, Stream stream, DateTime creationTime, DateTime writtenTime, DateTime accessedTime, FileAttributes attributes)
    {
        AddFile(name, stream, 0, 0, Utilities.UnixFilePermissionsFromFileAttributes(attributes), writtenTime);
    }
}
