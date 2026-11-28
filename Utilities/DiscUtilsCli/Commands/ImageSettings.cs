using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DiscUtilsCli.Commands;

/// <summary>The options every command that opens an image shares.</summary>
internal class ImageSettings : CommandSettings
{
    [CommandArgument(0, "<image>")]
    [Description(
        @"Disk image (VHDX, VHD, VMDK, VDI, raw) or Windows device (\\.\PhysicalDriveN, \\.\X:, \\?\Volume{...}; needs administrator rights). An image file name may hold * and ? to run on every match.")]
    public string Image { get; init; } = "";

    [CommandOption("--volume <n>")]
    [Description("Index of the volume to open, as 'info' lists them (default: the first volume with a file system)")]
    public int? Volume { get; init; }

    [CommandOption("--no-verify")]
    [Description("ReFS: skip metadata and data checksum verification")]
    public bool NoVerify { get; init; }

    [CommandOption("--replay")]
    [Description("ReFS, experimental: also apply the redo log records written after the last checkpoint (dirty volumes)")]
    public bool Replay { get; init; }

    public ImageOptions Options => new ImageOptions(Volume, NoVerify, Replay);

    public override ValidationResult Validate() => Volume is < 0 ? ValidationResult.Error("--volume takes a number from 0.") : ValidationResult.Success();
}