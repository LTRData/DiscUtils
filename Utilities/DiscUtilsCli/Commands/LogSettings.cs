using System.ComponentModel;
using Spectre.Console.Cli;

namespace DiscUtilsCli.Commands;

internal sealed class LogSettings : ImageSettings
{
    [CommandOption("--values")]
    [Description("Hex-dump the values of each record")]
    public bool Values { get; init; }
}