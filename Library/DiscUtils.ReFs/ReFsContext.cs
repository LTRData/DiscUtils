using DiscUtils.Vfs;

namespace DiscUtils.ReFs;

internal sealed class ReFsContext : VfsContext
{
    private long _nextId;

    public override Stream RawStream { get; set; } = null!;

    public ReFsVolume Volume { get; set; } = null!;

    /// <summary>Opens file data: the volume, or a Data Deduplication reader over it (<see cref="ReFsFileSystemOptions.ReadDeduplicatedFiles"/>).</summary>
    public Entries.IStreamOpener Opener { get; set; } = null!;

    /// <summary>
    /// File ids repeat between directories, so the VFS cache key is a per-volume counter: never wrong, at worst a cache miss.
    /// </summary>
    public long NextCacheId() => Interlocked.Increment(ref _nextId);
}
