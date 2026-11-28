using DiscUtils.ReFs.Entries;
using DiscUtils.ReFs.Entries.Enums;
using DiscUtils.Streams;
using DiscUtils.Vfs;

namespace DiscUtils.ReFs;

/// <summary>A ReFS file: metadata comes from the directory entry, content from the file record's streams.</summary>
internal class ReFsFile : IVfsFileWithStreams
{
    public ReFsFile(ReFsContext context, ReFsEntry entry)
    {
        Context = context;
        Entry = entry;
    }

    protected ReFsContext Context { get; }

    public ReFsEntry Entry { get; }

    public DateTime CreationTimeUtc
    {
        get => Entry.Created.UtcDateTime;
        set => throw new NotSupportedException();
    }

    public DateTime LastAccessTimeUtc
    {
        get => Entry.Accessed.UtcDateTime;
        set => throw new NotSupportedException();
    }

    public DateTime LastWriteTimeUtc
    {
        get => Entry.Modified.UtcDateTime;
        set => throw new NotSupportedException();
    }

    public FileAttributes FileAttributes
    {
        get => ToFileAttributes(Entry);
        set => throw new NotSupportedException();
    }

    public long FileLength => checked((long)Entry.Length);

    /// <summary>Content of the default data stream.</summary>
    public IBuffer FileContent
    {
        get
        {
            if (Entry.IsDirectory)
            {
                throw new IOException("Attempt to open directory as a file.");
            }

            return field ??= new StreamBuffer(Context.Opener.OpenRead(Entry), Ownership.Dispose);
        }
    }

    /// <summary>Names of the named (alternate) data streams, excluding the default stream and snapshots.</summary>
    public IEnumerable<string> AlternateStreamNames
    {
        get
        {
            foreach (var stream in Entry.Streams)
            {
                if (stream.Name.Length != 0 && !stream.IsSnapshot)
                {
                    yield return stream.Name;
                }
            }
        }
    }

    /// <summary>Physical byte ranges are not mapped yet; fail rather than report an empty mapping.</summary>
    public IEnumerable<StreamExtent> EnumerateAllocationExtents() => throw new NotSupportedException("ReFS allocation extents are not supported.");

    /// <summary>Opens a named stream, or returns <c>null</c> if the file has no such stream.</summary>
    public SparseStream OpenExistingStream(string name)
    {
        foreach (var stream in Entry.Streams)
        {
            if (!stream.IsSnapshot && string.Equals(stream.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                var data = Context.Opener.OpenRead(Entry, stream.Name.Length == 0 ? null : stream.Name);
                return data as SparseStream ?? SparseStream.FromStream(data, Ownership.Dispose);
            }
        }

        return null!;
    }

    public SparseStream CreateStream(string name) => throw new NotSupportedException();

    internal static FileAttributes ToFileAttributes(ReFsEntry entry)
    {
        // The stored value carries ReFS-internal marker bits; the Win32 attributes live in the low word.
        var attributes = (FileAttributes)((uint)entry.Attributes.WithoutMarkers() & 0xFFFF);

        if (entry.IsDirectory)
        {
            return attributes | FileAttributes.Directory;
        }

        return attributes & ~FileAttributes.Directory;
    }
}