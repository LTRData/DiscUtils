using DiscUtils.Archives;
using DiscUtils.Internal;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;
using System;
using System.Diagnostics;
using System.IO;

namespace DiscUtils.VirtualFileSystem;

public class TarFileSystem : VirtualFileSystem
{
    private readonly WeakReference<Stream> _tar;

    public override bool IsThreadSafe => true;

    public static bool Detect(Stream archive)
    {
        archive.Position = 0;

        try
        {
            Span<byte> buffer = stackalloc byte[512];

            if (archive.ReadMaximum(buffer) < 512)
            {
                return false;
            }

            return TarHeader.IsValid(buffer);
        }
        catch
        {
            return false;
        }
    }

    public TarFileSystem(FileStream tar_stream, bool ownsStream)
        : this(tar_stream, tar_stream.Name, ownsStream) { }

    public TarFileSystem(Stream tar_stream, string label, bool ownsStream)
        : base(new VirtualFileSystemOptions
        {
            VolumeLabel = label,
            CaseSensitive = true
        })
    {
        if (tar_stream.CanSeek)
        {
            tar_stream.Position = 0;
        }

        if (ownsStream)
        {
            _tar = new(tar_stream);
        }

        foreach (var file in TarFile.EnumerateFiles(tar_stream))
        {
            var path = file.Name;

            if (path.StartsWith('.'))
            {
                path = path.Substring(1);
            }

            path = path.Replace('/', '\\');

            if (path.EndsWith('\\'))
            {
                path = path.TrimEnd('\\');

                AddDirectory(path, file.Header.OwnerId, file.Header.GroupId, file.Header.FileMode,
                    file.Header.CreationTime.LocalDateTime, file.Header.ModificationTime.LocalDateTime, file.Header.LastAccessTime.LocalDateTime);
            }
            else
            {
                if (Exists(path))
                {
                    Trace.WriteLine($"TarFileSystem: Duplicate file path '{file.Name}'");
                    continue;
                }

                AddFile(path, file.GetStream() ?? Stream.Null, file.Header.OwnerId, file.Header.GroupId, file.Header.FileMode, file.Header.FileType,
                    file.Header.CreationTime.LocalDateTime, file.Header.ModificationTime.LocalDateTime, file.Header.LastAccessTime.LocalDateTime);
            }
        }

        Freeze();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _tar is not null && _tar.TryGetTarget(out var archive))
        {
            archive.Dispose();
        }

        base.Dispose(disposing);
    }
}
