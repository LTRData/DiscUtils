using System.ComponentModel;
using Spectre.Console.Cli;

namespace DiscUtilsCli.Commands;

/// <summary>Settings of the commands that take a path inside the file system after the image.</summary>
internal class PathSettings : ImageSettings
{
    [CommandArgument(1, "<path>")]
    [Description(@"Path inside the file system ('\' or '/' separators; '' is the root)")]
    public string Path { get; init; } = "";
}