namespace DiscUtils.DeduplicationRole;

/// <summary>The chunk store of a volume read through any <see cref="DiscFileSystem"/> (NTFS, ReFS, ...).</summary>
/// <param name="fileSystem">The file system that holds <c>System Volume Information\Dedup</c>.</param>
public sealed class DiscFileSystemDedupSource(DiscFileSystem fileSystem) : IDedupSource
{
    private readonly DiscFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    /// <inheritdoc/>
    public IReadOnlyList<string>? ListFiles(string directory)
    {
        if (!_fileSystem.DirectoryExists(directory))
        {
            return null;
        }

        return _fileSystem.GetFiles(directory).Select(path => path.Substring(path.LastIndexOf('\\') + 1)).ToList();
    }

    /// <inheritdoc/>
    public Stream OpenFile(string path) => _fileSystem.OpenFile(path, FileMode.Open, FileAccess.Read);
}
