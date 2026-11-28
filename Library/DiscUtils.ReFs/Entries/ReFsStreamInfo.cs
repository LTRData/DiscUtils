namespace DiscUtils.ReFs.Entries;

/// <summary>
///     A data stream of a file: the unnamed stream (empty name), a named (alternate) stream, or a stream snapshot (an earlier version of the unnamed
///     stream kept under a name, <c>refsutil streamsnapshot</c>). All three open by name with <c>OpenRead</c>.
/// </summary>
/// <param name="Name">Empty for the unnamed stream.</param>
/// <param name="Length">Size in bytes.</param>
/// <param name="IsResident">The data is stored in the file's record instead of in clusters.</param>
/// <param name="IsSnapshot">The stream is a snapshot of the unnamed stream, not an alternate data stream.</param>
/// <param name="SnapshotTime">When the snapshot was taken; null for other streams.</param>
internal readonly record struct ReFsStreamInfo(string Name, ulong Length, bool IsResident, bool IsSnapshot = false, DateTimeOffset? SnapshotTime = null);