using System.ComponentModel;
using Spectre.Console.Cli;

namespace DiscUtilsCli.Commands;

internal sealed class ValidateSettings : ImageSettings
{
    [CommandOption("--no-data")]
    [Description("Check metadata only; do not read file data")]
    public bool NoData { get; init; }
}