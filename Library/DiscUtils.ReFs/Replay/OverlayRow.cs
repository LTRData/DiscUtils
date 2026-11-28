namespace DiscUtils.ReFs.Replay;

/// <summary>
///     A row as the redo log leaves it. <see cref="Value" /> is the complete on-disk row value (null = the row was deleted).
///     <see cref="Incomplete" /> names the reason when later log records that change the row's embedded table (stream
///     allocation, stream summary, nested row edits) were not applied: such a row still carries correct directory
///     metadata (name, attributes, times, size) but its data streams and extended attributes must not be read.
/// </summary>
internal sealed record OverlayRow(byte[]? Value, string? Incomplete);