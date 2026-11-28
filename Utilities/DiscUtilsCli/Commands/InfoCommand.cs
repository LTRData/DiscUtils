using System.ComponentModel;
using System.Globalization;

namespace DiscUtilsCli.Commands;

[Description("Show the volumes of an image and the details of the file system opened")]
internal sealed class InfoCommand : ImageCommand<ImageSettings>
{
    protected override int Run(CliContext cli, OpenedImage opened, ImageSettings settings, string? image, CancellationToken cancellationToken)
    {
        var volumes = OpenedImage.ListVolumes(image ?? settings.Image);

        if (cli.Terminal is null)
        {
            foreach (var volume in volumes)
            {
                cli.Output.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} {1,-4} offset {2,14} size {3,14}  {4,-24} {5}{6}",
                    volume.Index == opened.VolumeIndex ? "*" : " ", volume.Index == OpenedImage.WholeDisk ? "disk" : volume.Index.ToString(CultureInfo.InvariantCulture),
                    volume.Start, volume.Length, volume.Type, volume.FileSystem ?? "(no file system detected)", $"  {volume.Identity}"));
            }

            cli.Output.WriteLine();
        }
        else
        {
            cli.Render(terminal => terminal.Write(Details.VolumeTable(volumes, opened.VolumeIndex)));
        }

        Details.Write(cli, Details.Volume(opened), 12, image ?? settings.Image);
        return CliRunner.Ok;
    }
}