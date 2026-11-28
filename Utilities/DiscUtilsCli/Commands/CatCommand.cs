using System.ComponentModel;

namespace DiscUtilsCli.Commands;

[Description("Write a file's raw bytes to standard output")]
internal sealed class CatCommand : ImageCommand<CatSettings>
{
    protected override bool SingleImageOnly => true;

    protected override int Run(CliContext cli, OpenedImage opened, CatSettings settings, string? image, CancellationToken cancellationToken)
    {
        using var stream = FileContent.OpenRead(opened.FileSystem, Details.NormalizePath(settings.Path), settings.Stream);
        stream.CopyTo(cli.Binary);
        cli.Binary.Flush();
        return CliRunner.Ok;
    }
}