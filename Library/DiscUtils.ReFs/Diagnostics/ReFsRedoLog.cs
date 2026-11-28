namespace DiscUtils.ReFs.Diagnostics;

/// <summary>The redo log written after the checkpoint in use, as read (nothing is applied) (experimental).</summary>
/// <param name="RegionPages">4 KiB pages in the log's circular data region.</param>
/// <param name="StartLsn">The LSN replay starts from (from the checkpoint); an LSN is {page index, lap}.</param>
/// <param name="EndLsn">The LSN after the last valid entry.</param>
/// <param name="Entries">Log entries read.</param>
/// <param name="Transactions">The transactions, in log order.</param>
public sealed record ReFsRedoLog(long RegionPages, ulong StartLsn, ulong EndLsn, int Entries, IReadOnlyList<ReFsRedoTransaction> Transactions)
{
    /// <summary>True when at least one committed transaction follows the checkpoint.</summary>
    public bool HasWorkToReplay => Transactions.Any(t => t.Committed);
}