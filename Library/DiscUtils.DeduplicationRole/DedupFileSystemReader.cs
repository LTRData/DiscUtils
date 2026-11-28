namespace DiscUtils.DeduplicationRole;

/// <summary>
/// Reads the files of a volume as Windows does with the Data Deduplication filter loaded: a file optimized by the role (reparse tag
/// <see cref="DedupReader.ReparseTag"/>) is read from the volume's chunk store, any other file as the file system gives it. The file system must
/// expose reparse points (<see cref="IWindowsFileSystem"/>: NTFS, ReFS); on any other file system every file is read as stored. When the file system reads deduplicated files by itself
/// (<see cref="IDedupAwareFileSystem"/>), <see cref="OpenRead"/> returns what it opens. Use one reader per
/// volume: it keeps the chunk store's container files open until disposed. Not thread-safe.
/// </summary>
public sealed class DedupFileSystemReader : IDisposable
{
    private readonly DedupReader _reader;

    /// <param name="fileSystem">The volume's file system.</param>
    /// <param name="verify">Whether to check each chunk's SHA-512 hash and each record's CRC-64.</param>
    public DedupFileSystemReader(DiscFileSystem fileSystem, bool verify = true)
    {
        FileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _reader = new DedupReader(new DiscFileSystemDedupSource(fileSystem), verify);
    }

    /// <summary>The file system read from.</summary>
    public DiscFileSystem FileSystem { get; }

    /// <summary>Whether the file at <paramref name="path"/> is optimized by Data Deduplication.</summary>
    public bool IsDeduplicated(string path) => ReparseData(path) is not null;

    /// <summary>
    /// Opens the content of the file at <paramref name="path"/>: a deduplicated file's content assembled from the chunk store and the ranges written
    /// since its optimization, any other file's default stream as stored. The stream is valid until this reader and the file system are disposed.
    /// </summary>
    /// <exception cref="DedupFormatException">The reparse data, a stream map, a container or a chunk is damaged.</exception>
    /// <exception cref="NotSupportedException">The file uses a layout this reader does not handle (a recall bitmap, an unknown format).</exception>
    /// <exception cref="FileNotFoundException">The file, or a container of the chunk store, does not exist.</exception>
    public Stream OpenRead(string path)
    {
        var stored = FileSystem.OpenFile(path, FileMode.Open, FileAccess.Read);

        // The file system has already read a deduplicated file from the chunk store.
        if (FileSystem is IDedupAwareFileSystem { ReadsDeduplicatedFiles: true })
        {
            return stored;
        }

        byte[]? reparseData;

        try
        {
            reparseData = ReparseData(path);
        }
        catch
        {
            stored.Dispose();
            throw;
        }

        if (reparseData is not null)
        {
            return _reader.Open(reparseData, stored);
        }

        return stored;
    }

    public void Dispose() => _reader.Dispose();

    /// <summary>The data of the file's Data Deduplication reparse point (after the 8-byte reparse header), or null when it has none.</summary>
    private byte[]? ReparseData(string path)
    {
        if (FileSystem is IWindowsFileSystem windows && windows.GetReparsePoint(path) is { } reparse && unchecked((uint)reparse.Tag) == DedupReader.ReparseTag)
        {
            return reparse.Content;
        }

        return null;
    }
}