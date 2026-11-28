using System.ComponentModel;
using Spectre.Console.Cli;

namespace DiscUtilsCli.Commands;

internal sealed class ListSettings : ImageSettings
{
    [CommandArgument(1, "[path]")]
    [Description("Directory or file inside the file system (default: the root)")]
    public string Path { get; init; } = "";

    [CommandOption("-l|--long")]
    [Description("Long listing: attributes, size, modified time")]
    public bool Long { get; init; }

    [CommandOption("-r|--recursive")]
    [Description("List recursively")]
    public bool Recursive { get; init; }
}