using System.ComponentModel;

namespace DiscUtilsCli.Commands;

[Description("Show the metadata and streams of a file or directory")]
internal sealed class StatCommand : ImageCommand<PathSettings>
{
    protected override int Run(CliContext cli, OpenedImage opened, PathSettings settings, string? image, CancellationToken cancellationToken)
    {
        var path = Details.NormalizePath(settings.Path);

        if (path.Length > 0 && !opened.FileSystem.Exists(path))
        {
            throw new FileNotFoundException($"'{settings.Path}' was not found.");
        }

        Details.Write(cli, Details.Entry(opened.FileSystem, path, cli.Terminal is not null), 10);
        return CliRunner.Ok;
    }
}