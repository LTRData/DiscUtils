using System.ComponentModel;
using Spectre.Console.Cli;

namespace DiscUtilsCli.Commands;

internal sealed class ExtractSettings : PathSettings
{
    [CommandArgument(2, "<destination>")]
    [Description("Folder to extract into (created); with a wildcard image each image gets a folder of its own, named after the image file")]
    public string Destination { get; init; } = "";

    [CommandOption("--force")]
    [Description("Overwrite existing files")]
    public bool Force { get; init; }
}