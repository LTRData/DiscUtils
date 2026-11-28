namespace DiscUtils.DeduplicationRole;

/// <summary>
/// A file system that can read files optimized by Data Deduplication by itself (NTFS, ReFS). <see cref="DedupFileSystemReader"/> uses it to avoid
/// resolving such a file a second time.
/// </summary>
public interface IDedupAwareFileSystem
{
    /// <summary>
    /// Whether opening the default data stream of a deduplicated file for reading already gives its content from the chunk store, rather than the stub.
    /// </summary>
    bool ReadsDeduplicatedFiles { get; }
}
