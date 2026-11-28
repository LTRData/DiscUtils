using System.ComponentModel;
using Spectre.Console.Cli;

namespace DiscUtilsCli.Commands;

internal sealed class CatSettings : PathSettings
{
    [CommandOption("--stream <name>")]
    [Description("Read a named (alternate) data stream; on ReFS also a snapshot")]
    public string? Stream { get; init; }
}