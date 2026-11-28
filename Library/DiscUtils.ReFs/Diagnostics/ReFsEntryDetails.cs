using DiscUtils.ReFs.Entries.Enums;

namespace DiscUtils.ReFs.Diagnostics;

/// <summary>ReFS details of a file or directory (experimental).</summary>
/// <param name="IsDirectory">True for a directory.</param>
/// <param name="AllocatedLength">Bytes allocated to the default stream.</param>
/// <param name="ObjectId">The id of a directory's table; 0 for files.</param>
/// <param name="FileId">The file number in the file's record (unique only within one directory, except for hard-linked files); the object id for directories.</param>
/// <param name="IntegrityStream">True when the file's data has checksums (on a directory: the default for new files).</param>
/// <param name="Replay">Whether redo-log replay created or changed the entry.</param>
/// <param name="Streams">The file's streams, including snapshots; empty for directories.</param>
public sealed record ReFsEntryDetails(
    bool IsDirectory,
    ulong AllocatedLength,
    ulong ObjectId,
    ulong FileId,
    bool IntegrityStream,
    ReFsReplayState Replay,
    IReadOnlyList<ReFsStreamDetails> Streams);