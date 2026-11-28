namespace DiscUtils.ReFs.Diagnostics;

/// <summary>One redo record.</summary>
/// <param name="Lsn">The LSN of the log entry holding the record.</param>
/// <param name="OperationCode">The raw operation code.</param>
/// <param name="Operation">The operation's name, or the code when it has none.</param>
/// <param name="Path">The table path the record applies to.</param>
/// <param name="Values">The record's values (for row operations, value 0 is the key).</param>
/// <param name="Clock">The checkpoint virtual clock when the record was written.</param>
/// <param name="RestartClock">The restart-clock field.</param>
public sealed record ReFsRedoRecord(
    ulong Lsn,
    uint OperationCode,
    string Operation,
    IReadOnlyList<ReFsRedoPathComponent> Path,
    IReadOnlyList<ReadOnlyMemory<byte>> Values,
    ulong Clock,
    ulong RestartClock);