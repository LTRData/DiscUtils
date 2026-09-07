using System;
using System.Collections.Generic;
using System.IO;
using DiscUtils;
using DiscUtils.Vfs;

namespace GeneratedRegistrationPlugin;

[VfsFileSystemFactory]
public sealed class PrivateFileSystemFactory : VfsFileSystemFactory
{
    public static int Constructions;
    public PrivateFileSystemFactory() => Constructions++;

    public override IEnumerable<DiscUtils.FileSystemInfo> Detect(Stream stream, VolumeInfo? volumeInfo)
    {
        stream.Position = 0;
        if (stream.Length >= 512 && stream.ReadByte() == 0x71 && stream.ReadByte() == 0x93)
            yield return new VfsFileSystemInfo("GeneratedPrivateFS", "Private test format",
                (_, _, _) => throw new NotSupportedException());
    }
}
