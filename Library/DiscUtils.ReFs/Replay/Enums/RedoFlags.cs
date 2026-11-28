namespace DiscUtils.ReFs.Replay.Enums;

/// <summary>
///     The flags of a redo record (the u32 at +0x2C). Seen on disk: 0, 2,
///     0x2001, 0x2003, 0x2005, 0x2007. Windows also tests 0x2000 and clears 0x10000 in memory.
/// </summary>
[Flags]
internal enum RedoFlags : uint
{
    None = 0,

    /// <summary>The record opens a transaction.</summary>
    Begin = 1,

    /// <summary>The record commits the transaction; only committed transactions are replayed.</summary>
    Commit = 2,

    /// <summary>
    ///     Inferred, from the callee name: <c>RedoOneRecordBlock</c> queues a checkpoint when it is set. Records with it are replayed like any other
    ///     here.
    /// </summary>
    Checkpoint = 4
}