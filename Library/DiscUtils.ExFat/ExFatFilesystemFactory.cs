// This is ExFat, an exFAT accessor written in pure C#
// Released under MIT license
// https://github.com/picrap/ExFat

using System.Collections.Generic;
using System.IO;
using DiscUtils.Vfs;

namespace DiscUtils.ExFat;
/// <summary>
/// </summary>
[VfsFileSystemFactory]
// ReSharper disable once UnusedMember.Global
public class ExFatFilesystemFactory : VfsFileSystemFactory
{
    /// <summary>
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="volume"></param>
    /// <returns></returns>
    public override IEnumerable<FileSystemInfo> Detect(Stream stream, VolumeInfo volume)
    {
        if (ExFatFileSystem.Detect(stream))
        {
            yield return new VfsFileSystemInfo("exFAT", ExFatFileSystem.Name, Open);
        }
    }

    private static DiscFileSystem Open(Stream stream, VolumeInfo volumeInfo, FileSystemParameters parameters) => new ExFatFileSystem(stream);
}
