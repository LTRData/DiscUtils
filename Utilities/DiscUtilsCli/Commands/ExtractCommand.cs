using System.ComponentModel;
using Spectre.Console;

namespace DiscUtilsCli.Commands;

[Description("Copy a file or directory tree out of the file system, with progress on a terminal")]
internal sealed class ExtractCommand : ImageCommand<ExtractSettings>
{
    protected override int Run(CliContext cli, OpenedImage opened, ExtractSettings settings, string? image, CancellationToken cancellationToken)
    {
        var destination = image is null ? settings.Destination : Path.Combine(settings.Destination, Path.GetFileName(image));
        var path = Details.NormalizePath(settings.Path);

        if (path.Length > 0 && !opened.FileSystem.Exists(path))
        {
            throw new FileNotFoundException($"'{settings.Path}' was not found.");
        }

        if (cli.Terminal is null)
        {
            var plain = Extraction.Run(opened.FileSystem, path, destination, settings.Force, cli.Error, null, cancellationToken);
            cli.Output.WriteLine(plain.Summary);
            return plain.Failures == 0 ? CliRunner.Ok : CliRunner.Failure;
        }

        Extraction? extraction = null;
        cli.Render(terminal =>
        {
            extraction = Extraction.Run(opened.FileSystem, path, destination, settings.Force, cli.Error, terminal, cancellationToken);
            terminal.MarkupLine(extraction.Failures == 0 ? $"[green]{extraction.Summary}[/]" : $"[yellow]{extraction.Summary}[/]");
        });

        return extraction!.Failures == 0 ? CliRunner.Ok : CliRunner.Failure;
    }
}