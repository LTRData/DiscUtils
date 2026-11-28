using System.ComponentModel;
using System.Text;
using DiscUtils.ReFs;
using DiscUtils.ReFs.Diagnostics;

namespace DiscUtilsCli.Commands;

[Description("ReFS diagnostic: the redo records written after the last checkpoint (nothing is replayed)")]
internal sealed class LogCommand : ImageCommand<LogSettings>
{
    protected override int Run(CliContext cli, OpenedImage opened, LogSettings settings, string? image, CancellationToken cancellationToken)
    {
        if (opened.ReFs is not { } refs)
        {
            throw new NotSupportedException($"'log' reads the ReFS redo log; this file system is {opened.FileSystemName}.");
        }

        Write(cli.Output, refs, settings.Values);
        return CliRunner.Ok;
    }

    internal static void Write(TextWriter output, ReFsFileSystem refs, bool values)
    {
        if (refs.ReadRedoLog() is not { } log)
        {
            output.WriteLine("The volume has no redo log information.");
            return;
        }

        output.WriteLine($"Log region    : {log.RegionPages} pages ({log.RegionPages * 4} KiB); an LSN is {{page index, lap}}");
        output.WriteLine($"Log start LSN : 0x{log.StartLsn:X} (lap {log.StartLsn >> 32}, page {(uint)log.StartLsn}; from the checkpoint)");
        output.WriteLine($"Log end LSN   : 0x{log.EndLsn:X}");
        output.WriteLine($"Entries       : {log.Entries}");
        output.WriteLine($"Transactions  : {log.Transactions.Count} ({log.Transactions.Count(t => t.Committed)} committed)");
        output.WriteLine(log.HasWorkToReplay
            ? refs.ReplayEnabled
                ? "Committed records exist after the checkpoint: experimental replay is enabled."
                : "Committed records exist after the checkpoint: replay is disabled."
            : "Nothing to replay.");

        var number = 0;

        foreach (var transaction in log.Transactions)
        {
            output.WriteLine($"transaction {++number} ({(transaction.Committed ? "committed" : "not committed")}, {transaction.Records.Count} records)");

            foreach (var record in transaction.Records)
            {
                var key = record.Values.Count > 0 && record.Values[0].Length > 4 && record.Values[0].Span[0] == 0x30
                    ? " key '" + Encoding.Unicode.GetString(record.Values[0].Span.Slice(4).ToArray()) + "'"
                    : string.Empty;

                output.WriteLine(
                    $"  0x{record.Lsn:X} {record.Operation}({record.OperationCode}) [{DescribePath(record)}] values={record.Values.Count}{key} clock=0x{record.Clock:X} restart=0x{record.RestartClock:X}");

                if (values)
                {
                    for (var i = 0; i < record.Values.Count; i++)
                    {
                        const int Shown = 96;
                        var value = record.Values[i];
                        output.WriteLine($"      v{i} ({value.Length} bytes): {Hex(value.Span.Slice(0, Math.Min(value.Length, Shown)))}{(value.Length > Shown ? "..." : string.Empty)}");
                    }
                }
            }
        }
    }

    internal static string DescribePath(ReFsRedoRecord record) =>
        string.Join("/", record.Path.Select(c => c.Kind == ReFsRedoPathComponent.RootKind
            ? $"object 0x{c.ObjectId:X}"
            : c.Key.Length > 4 && c.Key.Span[0] == 0x30
                ? $"'{Encoding.Unicode.GetString(c.Key.Span.Slice(4).ToArray())}'"
                : $"{c.Kind:X}:{Hex(c.Key.Span)}"));

    internal static string Hex(ReadOnlySpan<byte> bytes) => BitConverter.ToString(bytes.ToArray()).Replace("-", string.Empty);
}