using DiscUtils.Streams;
using LTRData.Extensions.Buffers;
using System;
using System.IO;
using System.IO.Compression;

namespace DiscUtils.VirtualFileSystem;

public class ZipFileSystem : VirtualFileSystem
{
    private readonly WeakReference _zip;

    public int FileDatabufferChunkSize { get; set; } = 32 * 1024 * 1024;

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

            return EndianUtilities.ToUInt32LittleEndian(buffer) == 0x04034B50U;
        }
        catch
        {
            return false;
        }
    }

    public ZipFileSystem(FileStream zip_stream, bool ownsStream)
        : this(zip_stream, Path.GetFileName(zip_stream.Name), ownsStream) { }

    public ZipFileSystem(Stream zip_stream, string label, bool ownsStream)
        : base(new VirtualFileSystemOptions
        {
            VolumeLabel = label
        })
    {
        if (zip_stream.CanSeek)
        {
            zip_stream.Position = 0;
        }

        var zip = new ZipArchive(zip_stream, ZipArchiveMode.Read, leaveOpen: false);

        if (ownsStream)
        {
            _zip = new WeakReference(zip);
        }

        foreach (var file in zip.Entries)
        {
            var path = file.FullName;

            if (path.StartsWith(@"./", StringComparison.Ordinal) ||
                path.StartsWith(@".\", StringComparison.Ordinal))
            {
                path = path.Substring(2);
            }

            path = path.Replace('/', '\\');

            if (path.EndsWith('\\'))
            {
                path = path.TrimEnd('\\');

                AddDirectory(path,
                    file.LastWriteTime.DateTime, file.LastWriteTime.DateTime, file.LastWriteTime.DateTime, FileAttributes.Directory);
            }
            else
            {
                var entry = AddFile(path, (mode, access) =>
                {
                    if (file.Length >= FileDatabufferChunkSize)
                    {
                        var data = new SparseMemoryBuffer(FileDatabufferChunkSize);

                        if (data.WriteFromStream(0, file.Open(), file.Length) < file.Length)
                        {
                            throw new EndOfStreamException("Unexpected end of zip stream");
                        }

                        return new SparseMemoryStream(data, FileAccess.Read);
                    }
                    else
                    {
                        var data = new byte[file.Length];

                        file.Open().ReadExactly(data, 0, data.Length);

                        return new MemoryStream(data, writable: false);
                    }
                });

                entry.Length = file.Length;
                
                entry.LastAccessTimeUtc =
                    entry.CreationTimeUtc =
                    entry.LastWriteTimeUtc =
                    file.LastWriteTime.UtcDateTime;
            }
        }

        Freeze();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _zip?.Target is IDisposable archive)
        {
            archive.Dispose();
        }

        base.Dispose(disposing);
    }
}
