namespace DiscUtils.ReFs.Replay;

/// <summary>A transaction: the records from one with the begin flag to one with the commit flag (or the open tail).</summary>
internal sealed record RedoTransaction(IReadOnlyList<RedoRecord> Records, bool Committed);