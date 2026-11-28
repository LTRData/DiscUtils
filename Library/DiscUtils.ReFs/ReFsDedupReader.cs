using DiscUtils.ReFs.Compat;
using DiscUtils.DeduplicationRole;
using DiscUtils.ReFs.Entries;

namespace DiscUtils.ReFs;

/// <summary>
///     Reads a ReFS volume's files as Windows does with the Data Deduplication filter loaded: a deduplicated file's default stream comes from the chunk
///     store (<see cref="DedupReader" />), everything else as <see cref="ReFsVolume.OpenRead(ReFsEntry, string?)" /> gives it. Use one reader per volume:
///     it keeps the chunk store's container files open until disposed. Not thread-safe.
/// </summary>
internal sealed class ReFsDedupReader : IStreamOpener, IDisposable
{
    private readonly DedupReader _reader;
    private readonly ReFsVolume _volume;

    /// <summary>Reads through <paramref name="volume" />; chunks are verified when the volume verifies data checksums.</summary>
    public ReFsDedupReader(ReFsVolume volume)
    {
        ArgumentNullException.ThrowIfNull(volume);
        _volume = volume;
        _reader = new DedupReader(new Source(volume), volume.VerifyDataChecksums);
    }

    /// <summary>Whether <paramref name="entry" /> is a file optimized by Data Deduplication (reparse tag 0x80000013).</summary>
    public static bool IsDeduplicated(ReFsEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Reparse?.Tag == DedupReader.ReparseTag;
    }

    public void Dispose()
    {
        _reader.Dispose();
    }

    /// <summary>
    ///     Opens a data stream of a file: the default stream of a deduplicated file assembled from the chunk store and the ranges written since the
    ///     optimization, any other stream as stored. The returned stream is valid until this reader and the volume are disposed.
    /// </summary>
    public Stream OpenRead(ReFsEntry entry, string? stream = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Stream stored = _volume.OpenRead(entry, stream);
        if (!string.IsNullOrEmpty(stream) || !IsDeduplicated(entry))
        {
            return stored;
        }

        byte[] data;
        try
        {
            data = _volume.GetReparseData(entry) ?? throw new ReFsFormatException($"'{entry.Name}' is listed with a Data Deduplication reparse point that cannot be read.");
        }
        catch
        {
            stored.Dispose();
            throw;
        }

        return _reader.Open(data, stored);
    }

    private sealed class Source(ReFsVolume volume) : IDedupSource
    {
        public IReadOnlyList<string>? ListFiles(string directory)
        {
            if (volume.TryGetEntry(directory, out var entry) && entry.IsDirectory)
            {
                return [.. volume.Enumerate(entry).Where(e => !e.IsDirectory).Select(e => e.Name)];
            }

            return null;
        }

        public Stream OpenFile(string path)
        {
            if (volume.TryGetEntry(path, out var entry))
            {
                return volume.OpenRead(entry);
            }

            throw new FileNotFoundException("Data Deduplication chunk store file not found.", path);
        }
    }
}