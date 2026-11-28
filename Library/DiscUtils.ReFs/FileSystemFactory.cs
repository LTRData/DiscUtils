using DiscUtils.Vfs;
using LTRData.Extensions.Buffers;

namespace DiscUtils.ReFs;

[VfsFileSystemFactory]
internal sealed class FileSystemFactory : VfsFileSystemFactory
{
    public override IEnumerable<FileSystemInfo> Detect(Stream stream, VolumeInfo? volume)
    {
        if (ReFsFileSystem.Detect(stream))
        {
            return SingleValueEnumerable.Get(new VfsFileSystemInfo("ReFS", "Resilient File System", Open));
        }

        return [];
    }

    private static ReFsFileSystem Open(Stream stream, VolumeInfo? volumeInfo, FileSystemParameters? parameters)
    {
        return new ReFsFileSystem(stream, parameters);
    }
}