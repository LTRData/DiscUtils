using DiscUtils.ReFs.Entries.Enums;
using DiscUtils.ReFs.Entries.Files;
#if NET46
using DiscUtils.Streams.Compatibility;
#endif

namespace DiscUtils.ReFs.Entries;

/// <summary>
///     A file or directory with its metadata. <c>FileId</c> is the file number stored in the file's record (for files, unique only among the files of one directory: the
///     same number repeats across directories, so it does not identify a file volume-wide) and the object id for directories;
///     <c>ObjectId</c> is the id of a directory's table (0 for files).
///     Linked names expose the name row's timestamps, attributes and sizes; streams and data access resolve the backing record.
/// </summary>
internal sealed record ReFsEntry(
    string Name,
    ReFsEntryKind Kind,
    ulong FileId,
    ulong ObjectId,
    ReFsAttributes Attributes,
    ulong Length,
    ulong AllocatedLength,
    DateTimeOffset Created,
    DateTimeOffset Modified,
    DateTimeOffset Changed,
    DateTimeOffset Accessed,
    IReadOnlyList<ReFsStreamInfo> Streams)
{
    public bool IsDirectory => Kind == ReFsEntryKind.Directory;

    /// <summary>Reparse data (e.g. symlink target) for files that carry it; null otherwise.</summary>
    public ReFsReparsePointDto? Reparse { get; init; }

    /// <summary>Whether redo-log replay created or changed this entry (only with <see cref="ReFsFileSystemOptions.ReplayLog" />).</summary>
    public ReFsReplayState Replay { get; init; }

    /// <summary>Security id from the file record (0 for directories listed through their parent, and for unresolved link records).</summary>
    internal ulong SecurityId { get; init; }

    /// <summary>
    ///     For a name that is a hard-link record: the directory whose table holds the file's record (with <c>FileId</c>, the file's
    ///     volume-wide identity, the high half of Windows' 128-bit file id). 0 when the record lives in the listing directory.
    /// </summary>
    internal ulong HolderDirectory { get; init; }

    /// <summary>
    ///     A 128-bit file id the record keeps from where the file was before (standard information 0x68 directory, 0x60 ordinal); zero when
    ///     there is none. Seen on ReFS 3.1 and 3.4, where a move to another directory relocates the record and Windows goes on reporting the old id.
    /// </summary>
    internal (ulong High, ulong Low) KeptFileId { get; init; }

    /// <summary>Where the record lives on disk (leaf page and slot); used to open the file's data later.</summary>
    internal EntryLocation? Location { get; init; }

    /// <summary>The original namespace key when replay is active; a linked file's backing-record key can be different.</summary>
    internal byte[]? NamespaceKey { get; init; }

    /// <summary>Value equality, including the streams (compared element-wise).</summary>
    public bool Equals(ReFsEntry? other) =>
        other is not null
        && Name == other.Name
        && Kind == other.Kind
        && FileId == other.FileId
        && ObjectId == other.ObjectId
        && Attributes == other.Attributes
        && Length == other.Length
        && AllocatedLength == other.AllocatedLength
        && Created == other.Created
        && Modified == other.Modified
        && Changed == other.Changed
        && Accessed == other.Accessed
        && Equals(Reparse, other.Reparse)
        && Replay == other.Replay
        && Streams.SequenceEqual(other.Streams);

    public override int GetHashCode() => HashCode.Combine(Name, Kind, FileId, ObjectId, Length, Modified);
}