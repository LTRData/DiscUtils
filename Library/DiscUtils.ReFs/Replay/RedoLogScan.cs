using DiscUtils.ReFs.IO;

namespace DiscUtils.ReFs.Replay;

/// <summary>
///     The result of reading the redo log from the checkpoint's log start LSN to the end of the valid log. Nothing is applied:
///     replay is a later stage.
/// </summary>
internal sealed record RedoLogScan(
    LogLocation Location,
    ulong StartLsn,
    ulong EndLsn,
    int Entries,
    uint ControlGeneration,
    IReadOnlyList<RedoRecord> Records,
    IReadOnlyList<RedoTransaction> Transactions)
{
    /// <summary>True when there is at least one committed record to replay (a clean volume has an empty log tail).</summary>
    public bool HasWorkToReplay => Transactions.Any(t => t.Committed);

    public static RedoLogScan Read(IBlockSource volume, uint clusterSize, LogLocation location, ulong startLsn)
    {
        var log = RedoLog.Open(volume, clusterSize, location);
        List<RedoRecord> records = [];
        var entries = 0;
        var end = startLsn;
        IReadOnlyList<RedoPathComponent> path = [];

        foreach (var entry in log.ReadEntries(startLsn))
        {
            entries++;
            end = entry.Lsn + (ulong)entry.AllocatedPages;
            records.AddRange(RedoRecord.ParsePayload(entry.Lsn, entry.Payload, ref path));
        }

        return new RedoLogScan(location, startLsn, end, entries, log.Control.Generation, records, GroupTransactions(records));
    }

    private static List<RedoTransaction> GroupTransactions(List<RedoRecord> records)
    {
        List<RedoTransaction> result = [];
        List<RedoRecord> open = [];

        foreach (var record in records)
        {
            if (record.IsBegin && open.Count > 0)
            {
                result.Add(new RedoTransaction(open, false)); // a new begin while one is open: the open one never committed
                open = [];
            }

            open.Add(record);

            if (record.IsCommit)
            {
                result.Add(new RedoTransaction(open, true));
                open = [];
            }
        }

        if (open.Count > 0)
        {
            result.Add(new RedoTransaction(open, false));
        }

        return result;
    }
}