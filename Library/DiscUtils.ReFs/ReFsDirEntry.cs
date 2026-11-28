using DiscUtils.ReFs.Entries;
using DiscUtils.Vfs;

namespace DiscUtils.ReFs;

internal sealed class ReFsDirEntry : VfsDirEntry
{
    public ReFsDirEntry(ReFsEntry entry, long cacheId)
    {
        Entry = entry;
        UniqueCacheId = cacheId;
    }

    public ReFsEntry Entry { get; }

    public override string FileName => Entry.Name;

    public override bool IsDirectory => Entry.IsDirectory;

    // Reparse points (including symbolic links) are listed as ordinary entries with the ReparsePoint attribute,
    // as the NTFS implementation does; targets are not followed because they usually name host drive letters.
    public override bool IsSymlink => false;

    public override long UniqueCacheId { get; }

    public override bool HasVfsFileAttributes => true;

    public override bool HasVfsTimeInfo => true;

    public override FileAttributes FileAttributes => ReFsFile.ToFileAttributes(Entry);

    public override DateTime CreationTimeUtc => Entry.Created.UtcDateTime;

    public override DateTime LastAccessTimeUtc => Entry.Accessed.UtcDateTime;

    public override DateTime LastWriteTimeUtc => Entry.Modified.UtcDateTime;

    public override string ToString() => FileName;
}
