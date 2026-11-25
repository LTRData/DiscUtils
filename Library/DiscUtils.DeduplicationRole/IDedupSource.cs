namespace DiscUtils.DeduplicationRole;

/// <summary>
///     What <see cref="DedupReader" /> needs from the file system that holds the chunk store (<c>System Volume Information\Dedup</c>): directory listings
///     and file streams, by path from the volume root with backslash separators. <see cref="DiscFileSystemDedupSource"/> provides it for any <see cref="DiscFileSystem"/>.
/// </summary>
public interface IDedupSource
{
    /// <summary>The names of the files (not directories) in <paramref name="directory" />, or null when the directory does not exist.</summary>
    IReadOnlyList<string>? ListFiles(string directory);

    /// <summary>Opens the default data stream of the file at <paramref name="path" /> as stored. The caller disposes it.</summary>
    Stream OpenFile(string path);
}
