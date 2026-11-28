namespace DiscUtils.ReFs.Diagnostics;

/// <summary>A redo-log transaction.</summary>
/// <param name="Committed">True when the transaction was committed (only committed transactions are replayed).</param>
/// <param name="Records">The records, in log order.</param>
public sealed record ReFsRedoTransaction(bool Committed, IReadOnlyList<ReFsRedoRecord> Records);