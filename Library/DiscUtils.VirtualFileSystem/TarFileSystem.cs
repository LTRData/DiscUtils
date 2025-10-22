using DiscUtils.Archives;
using DiscUtils.Internal;
using DiscUtils.Streams;
using LTRData.Extensions.Buffers;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
        : this(tar_stream, label, ownsStream, initialize: true) { }

    private TarFileSystem(Stream tar_stream, string label, bool ownsStream, bool initialize)
        : base(new VirtualFileSystemOptions
        {
            VolumeLabel = label,
            CaseSensitive = true
        })
    {
        if (ownsStream)
        {
            _tar = new(tar_stream);
        }

        if (!initialize)
        {
            return;
        }

        if (tar_stream.CanSeek)
        {
            tar_stream.Position = 0;
        }

        foreach (var file in TarFile.EnumerateFiles(tar_stream))
        {
            ProcessTarFileEntry(file);
        }

        Freeze();
    }

#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
    public static async Task<TarFileSystem> GetTarFileSystemAsync(Stream tar_stream, string label, bool ownsStream, CancellationToken cancellationToken)
    {
        var fs = new TarFileSystem(tar_stream, label, ownsStream, initialize: false);

        if (tar_stream.CanSeek)
        {
            tar_stream.Position = 0;
        }

        await foreach (var file in TarFile.EnumerateFilesAsync(tar_stream, cancellationToken).ConfigureAwait(false))
        {
            fs.ProcessTarFileEntry(file);
        }

        fs.Freeze();

        return fs;
    }
#endif

    private void ProcessTarFileEntry(TarFileData file)
    {
        var path = file.Name;

        if (path.StartsWith('.'))
        {
            path = path.Substring(1);
        }

        path = path.Replace('/', '\\');

        if (path.EndsWith('\\')
            || file.Header.FileType == TarFileType.TarEntryDirectory)
        {
            path = path.TrimEnd('\\');

            AddDirectory(path, file.Header.OwnerId, file.Header.GroupId, file.Header.FileMode,
                file.Header.CreationTime.LocalDateTime, file.Header.ModificationTime.LocalDateTime, file.Header.LastAccessTime.LocalDateTime);
        }
        else if (file.Header.FileType == TarFileType.TarEntryLink)
        {
            AddLink(file.Header.LinkName, path);
        }
        else if (file.Header.FileType == TarFileType.TarEntrySymbolicLink)
        {
            // Symbolic links are not supported in this VFS implementation
            Trace.WriteLine($"TarFileSystem: Skipping symbolic link '{file.Name}' -> '{file.Header.LinkName}'");
        }
        else
        {
            if (Exists(path))
            {
                if (DirectoryExists(path))
                {
                    Trace.WriteLine($"TarFileSystem: Path '{path}' exists as a directory, cannot add file");
                    return;
                }

                DeleteFile(path);
            }

            AddFile(path, file.GetStream() ?? Stream.Null, file.Header.OwnerId, file.Header.GroupId, file.Header.FileMode, file.Header.FileType.ToUnixFileType(),
                file.Header.CreationTime.LocalDateTime, file.Header.ModificationTime.LocalDateTime, file.Header.LastAccessTime.LocalDateTime);
        }
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
