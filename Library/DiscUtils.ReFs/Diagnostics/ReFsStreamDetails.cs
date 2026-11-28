namespace DiscUtils.ReFs.Diagnostics;

/// <summary>A data stream of a file: the default stream (empty name), a named stream, or a snapshot of the default stream.</summary>
/// <param name="Name">The stream name; empty for the default stream.</param>
/// <param name="Length">The stream length in bytes.</param>
/// <param name="IsResident">True when the data is stored inline in the file record.</param>
/// <param name="IsSnapshot">True for a snapshot of the default stream.</param>
/// <param name="SnapshotTime">When the snapshot was taken; null for other streams.</param>
public sealed record ReFsStreamDetails(string Name, ulong Length, bool IsResident, bool IsSnapshot, DateTimeOffset? SnapshotTime);