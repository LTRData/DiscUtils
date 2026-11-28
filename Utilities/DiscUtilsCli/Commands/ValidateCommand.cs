using System.ComponentModel;
using DiscUtils.ReFs;
using DiscUtils.ReFs.Validation;
using Spectre.Console;

namespace DiscUtilsCli.Commands;

[Description("ReFS: read and verify all metadata and, unless --no-data, all file data (exit code 1 on problems)")]
internal sealed class ValidateCommand : ImageCommand<ValidateSettings>
{
    private const int MaximumIssuesShown = 200;

    protected override int Run(CliContext cli, OpenedImage opened, ValidateSettings settings, string? image, CancellationToken cancellationToken)
    {
        if (opened.ReFs is not { } refs)
        {
            throw new NotSupportedException($"'validate' checks ReFS volumes; this file system is {opened.FileSystemName}.");
        }

        ValidationReport report;

        if (cli.Terminal is null)
        {
            report = refs.Validate(!settings.NoData, null, cancellationToken);
            WritePlain(cli.Output, report, settings.NoData);
        }
        else
        {
            report = null!;
            cli.Render(terminal =>
            {
                report = RunWithProgress(terminal, refs, !settings.NoData, cancellationToken);
                WriteTable(terminal, report, settings.NoData);
            });
        }

        return report.IsValid ? CliRunner.Ok : CliRunner.Failure;
    }

    /// <summary>Validation with a live display: the directory walk (counts and bytes so far) and the object trees (a bar).</summary>
    internal static ValidationReport RunWithProgress(IAnsiConsole terminal, ReFsFileSystem refs, bool verifyFileData, CancellationToken cancellationToken)
    {
        ValidationReport report = null!;
        terminal.Progress()
                .AutoClear(false)
                .Columns(new TaskDescriptionColumn { Alignment = Justify.Left }, new ProgressBarColumn(), new PercentageColumn(), new ElapsedTimeColumn(),
                    new SpinnerColumn())
                .Start(context =>
                {
                    var tables = context.AddTask("Tables").IsIndeterminate();
                    var directories = context.AddTask("Directories", false).IsIndeterminate();
                    var objects = context.AddTask("Object trees", false);

                    var receiver = new ProgressReceiver(progress =>
                    {
                        switch (progress.Phase)
                        {
                            case ValidationPhase.Tables:
                                break;
                            case ValidationPhase.Directories:
                                Finish(tables, "Tables");
                                directories.StartTask();
                                directories.Description =
                                    $"Directories {progress.Directories:N0} · files {progress.Files:N0}{(verifyFileData ? $" · {Details.Size((long)progress.DataBytes)}" : string.Empty)}"
                                    + (progress.Issues > 0 ? $" · [red]{progress.Issues} problem(s)[/]" : string.Empty);

                                break;
                            case ValidationPhase.Objects:
                                Finish(tables, "Tables");
                                directories.IsIndeterminate(false);
                                directories.Value = directories.MaxValue;
                                directories.StopTask();
                                objects.StartTask();
                                objects.MaxValue = Math.Max(progress.ObjectsTotal, 1);
                                objects.Value = progress.Objects;
                                objects.Description = $"Object trees {progress.Objects:N0}/{progress.ObjectsTotal:N0}";
                                break;
                        }
                    });

                    report = refs.Validate(verifyFileData, receiver, cancellationToken);
                    objects.Value = objects.MaxValue;
                    objects.StopTask();
                });

        return report;

        static void Finish(ProgressTask task, string description)
        {
            if (task.IsFinished)
            {
                return;
            }

            task.IsIndeterminate(false);
            task.Description = description;
            task.Value = task.MaxValue;
            task.StopTask();
        }
    }

    internal static void WriteTable(IAnsiConsole terminal, ValidationReport report, bool noData)
    {
        List<(string, string)> rows =
        [
            ("Objects", $"{report.Objects:N0} tree(s) successfully read"),
            ("Metadata reads", $"{report.MetadataPages:N0}"),
            ("Metadata checksums", report.MetadataChecksumsEnabled ? "enabled" : "disabled"),
            ("Directories", $"{report.Directories:N0}"),
            ("Files", $"{report.Files:N0}"),
            ("File data", noData
                ? "not checked (--no-data)"
                : $"{report.Streams:N0} stream(s), {Details.Size((long)report.DataBytes)} read (integrity checks {(report.DataChecksumsEnabled ? "enabled" : "disabled")})"),
            ("Time", $"{report.Elapsed.TotalSeconds:F1} s")
        ];

        terminal.Write(Details.Table(rows, "Validation"));

        foreach (var warning in report.Warnings)
        {
            terminal.MarkupLine($"[yellow]warning:[/] {Markup.Escape(warning)}");
        }

        foreach (var issue in report.Issues.Take(MaximumIssuesShown))
        {
            terminal.MarkupLine($"[red]ERROR[/] {Markup.Escape(issue.Location)}: {Markup.Escape(issue.Message)}");
        }

        if (report.Issues.Count > MaximumIssuesShown)
        {
            terminal.MarkupLine($"[red]... and {report.Issues.Count - MaximumIssuesShown} more problem(s)[/]");
        }

        terminal.MarkupLine(report.IsValid
            ? report.Warnings.Count == 0 ? "[green]Result: OK[/]" : "[green]Result: OK[/] [yellow]with limitations (see warnings)[/]"
            : $"[red]Result: {report.Issues.Count} problem(s) found[/]");
    }

    private static void WritePlain(TextWriter output, ValidationReport report, bool noData)
    {
        output.WriteLine($"Objects       : {report.Objects} tree(s) successfully read");
        output.WriteLine($"Metadata reads: {report.MetadataPages}");
        output.WriteLine($"Metadata checksums: {(report.MetadataChecksumsEnabled ? "enabled" : "disabled")}");
        output.WriteLine($"Directories   : {report.Directories}");
        output.WriteLine($"Files         : {report.Files}");
        output.WriteLine(noData
            ? "File data     : not checked (--no-data)"
            : $"File data     : {report.Streams} stream(s), {report.DataBytes} bytes read (integrity checks {(report.DataChecksumsEnabled ? "enabled" : "disabled")})");

        output.WriteLine($"Time          : {report.Elapsed.TotalSeconds:F1} s");

        foreach (var warning in report.Warnings)
        {
            output.WriteLine($"warning: {warning}");
        }

        foreach (var issue in report.Issues.Take(MaximumIssuesShown))
        {
            output.WriteLine($"ERROR {issue.Location}: {issue.Message}");
        }

        if (report.Issues.Count > MaximumIssuesShown)
        {
            output.WriteLine($"... and {report.Issues.Count - MaximumIssuesShown} more problem(s)");
        }

        output.WriteLine(report.IsValid
            ? report.Warnings.Count == 0 ? "Result        : OK" : "Result        : OK with limitations (see warnings)"
            : $"Result        : {report.Issues.Count} problem(s) found");
    }

    /// <summary>Calls back on the reporting thread (<see cref="Progress{T}" /> would post to the thread pool, out of order).</summary>
    private sealed class ProgressReceiver(Action<ValidationProgress> report) : IProgress<ValidationProgress>
    {
        public void Report(ValidationProgress value) => report(value);
    }
}