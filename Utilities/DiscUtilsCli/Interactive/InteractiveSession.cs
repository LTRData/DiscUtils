using System.Globalization;
using DiscUtils;
using DiscUtils.Windows;
using DiscUtilsCli.Commands;
using Spectre.Console;

namespace DiscUtilsCli.Interactive;

/// <summary>
///     <c>discutils</c> without arguments in a console: asks for an image, shows its volumes (partitions) to choose from, then offers a menu
///     (browse, details, and for ReFS validate and redo log). Browsing is a directory tree with a details pane, driven by the arrow keys (<see cref="TreeBrowser" />); a file or
///     folder can be
///     extracted from it. Ctrl+C stops a running validation or extraction and returns to the menu; at a prompt it ends the program.
/// </summary>
internal static class InteractiveSession
{
    private const string Browse = "Browse files";
    private const string VolumeDetails = "Volume details";
    private const string Validate = "Validate (ReFS)";
    private const string RedoLog = "Redo log (ReFS)";
    private const string OtherVolume = "Choose another volume";
    private const string OtherImage = "Open another image";
    private const string Quit = "Quit";

    public static int Run(IAnsiConsole console)
    {
        console.Write(new Rule($"[bold]{CliRunner.Name}[/] [grey]read-only disk image browser[/]").LeftJustified());

        while (AskImage(console) is { } image)
        {
            if (BrowseVolumes(console, image) == MenuExit.Quit)
            {
                return CliRunner.Ok;
            }
        }

        return CliRunner.Ok;
    }

    /// <summary>Shows the partition browser for an image and opens the volumes chosen from it, until the user quits or wants another image.</summary>
    private static MenuExit BrowseVolumes(IAnsiConsole console, string image)
    {
        while (ChooseVolume(console, image) is { } volume)
        {
            OpenedImage? opened = null;

            try
            {
                console.Status().Spinner(Spinner.Known.Dots).Start($"Opening volume {VolumeNumber(volume)} of {Markup.Escape(image)}...",
                    _ => opened = OpenedImage.Open(image, new ImageOptions(volume.Index)));
            }
            catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
            {
                console.MarkupLine($"[red]Could not open volume {VolumeNumber(volume)}:[/] {Markup.Escape(ex.Message)}");
                continue;
            }

            using (opened)
            {
                var fs = opened!.FileSystem;
                console.MarkupLine($"[green]Opened[/] {Markup.Escape(opened.FileSystemName)} on volume {Markup.Escape(opened.Volume)}, {Details.Size(fs.Size)}"
                                   + (Details.TryGetSpace(fs, out _, out var available, out _) ? $", {Details.Size(available)} available" : string.Empty)
                                   + (fs.VolumeLabel is { Length: > 0 } label ? $", label [bold]{Markup.Escape(label)}[/]" : string.Empty));

                var exit = Menu(console, opened, image);

                if (exit != MenuExit.OtherVolume)
                {
                    return exit;
                }
            }
        }

        return MenuExit.OtherImage;
    }

    /// <summary>
    ///     The partition browser: the image's volumes with their offset, size, type and detected file system, one of which the user picks. Returns
    ///     null to go back to the image prompt. A disk without a partition table is shown as one volume.
    /// </summary>
    private static VolumeChoice? ChooseVolume(IAnsiConsole console, string image)
    {
        IReadOnlyList<VolumeChoice> volumes = [];

        try
        {
            console.Status().Spinner(Spinner.Known.Dots).Start($"Reading the volumes of {Markup.Escape(image)}...", _ => volumes = OpenedImage.ListVolumes(image));
        }
        catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
        {
            console.MarkupLine($"[red]Could not open {Markup.Escape(image)}:[/] {Markup.Escape(ex.Message)}");
            return null;
        }

        console.Write(Details.VolumeTable(volumes, title: $"Volumes of {Path.GetFileName(image)}"));

        // Back is a choice of its own; the cursor starts on the first volume with a file system.
        var back = new VolumeChoice(int.MinValue, string.Empty, 0, 0, string.Empty, null);
        var ordered = volumes.Where(v => v.FileSystem is not null).Concat(volumes.Where(v => v.FileSystem is null)).ToList();
        var chosen = console.Prompt(new SelectionPrompt<VolumeChoice>()
                                    .Title("Open which volume?")
                                    .PageSize(Math.Max(3, Math.Min(volumes.Count + 1, 20)))
                                    .UseConverter(v => v == back
                                        ? "Back (another image)"
                                        : $"{VolumeNumber(v),-5} {Details.Size(v.Length),12}  {Markup.Escape(v.Type)}  "
                                          + (v.FileSystem is null ? "[grey](no file system detected)[/]" : $"[green]{Markup.Escape(v.FileSystem)}[/]"))
                                    .AddChoices(ordered)
                                    .AddChoices(back));

        return chosen == back ? null : chosen;
    }

    private static string VolumeNumber(VolumeChoice volume) => volume.Index == OpenedImage.WholeDisk ? "disk" : volume.Index.ToString(CultureInfo.InvariantCulture);

    /// <summary>The image path or device path (such as <c>\\.\PhysicalDrive1</c>) to open, or null to quit. Quotes around a pasted path are removed.</summary>
    private static string? AskImage(IAnsiConsole console)
    {
        var answer = console.Prompt(new TextPrompt<string>(@"Disk image ([grey]VHDX, VHD, VMDK, VDI, raw[/]) or device ([grey]\\.\PhysicalDriveN, \\.\X:[/]; empty to quit):")
                                    .AllowEmpty()
                                    .Validate(text =>
                                    {
                                        var path = text.Trim().Trim('"');
                                        return path.Length == 0 || File.Exists(path) || DeviceDisk.IsDevicePath(path)
                                            ? ValidationResult.Success()
                                            : ValidationResult.Error("[red]No such file.[/]");
                                    }));

        var image = answer.Trim().Trim('"');
        return image.Length == 0 ? null : image;
    }

    /// <summary>The main menu of an open volume, until the user quits or picks another volume or image.</summary>
    private static MenuExit Menu(IAnsiConsole console, OpenedImage opened, string image)
    {
        var choices = opened.ReFs is null
            ? new[] { Browse, VolumeDetails, OtherVolume, OtherImage, Quit }
            : new[] { Browse, VolumeDetails, Validate, RedoLog, OtherVolume, OtherImage, Quit };

        while (true)
        {
            var choice = console.Prompt(new SelectionPrompt<string>()
                                        .Title($"[bold]{Markup.Escape(Path.GetFileName(image) is { Length: > 0 } name ? name : image)}[/]")
                                        .AddChoices(choices));

            switch (choice)
            {
                case Browse:
                    TreeBrowser? browser = null;

                    if (Guard(console, () => browser = new TreeBrowser(console, opened.FileSystem,
                            step => OpenedImage.Open(image, new ImageOptions(opened.VolumeIndex, ReplayTransactions: step)))))
                    {
                        browser!.Run((fs, path, stream) => ExtractInteractively(console, fs, path, stream), action => Cancellable(console, action));
                    }

                    break;
                case VolumeDetails:
                    Guard(console, () => console.Write(Details.Table(Details.Volume(opened), image)));
                    break;
                case Validate:
                    var data = console.Confirm("Also read and verify all file data?");
                    Cancellable(console, token =>
                    {
                        var report = ValidateCommand.RunWithProgress(console, opened.ReFs!, data, token);
                        ValidateCommand.WriteTable(console, report, !data);
                    });

                    break;
                case RedoLog:
                    var log = new StringWriter();
                    Guard(console, () => LogCommand.Write(log, opened.ReFs!, false));
                    console.WriteLine(log.ToString().TrimEnd());
                    break;
                case OtherVolume:
                    return MenuExit.OtherVolume;
                case OtherImage:
                    return MenuExit.OtherImage;
                default:
                    return MenuExit.Quit;
            }
        }
    }

    /// <summary>Extracts a file or directory, or only one stream of a file (chosen with Tab in the browser) to "name_stream".</summary>
    private static void ExtractInteractively(IAnsiConsole console, DiscFileSystem fs, string path, StreamChoice? stream)
    {
        var destination = console.Prompt(new TextPrompt<string>(stream is null ? "Extract into folder:" : $"Extract stream '{Markup.Escape(stream.Name)}' into folder:")
            .DefaultValue(Path.Combine(Environment.CurrentDirectory, "discutils-extract")));

        destination = destination.Trim().Trim('"');

        var target = stream is not null ? Path.Combine(destination, Extraction.StreamFileName(path, stream.Name))
            : path.Length == 0 ? destination
            : Path.Combine(destination, Details.Name(path));

        var force = (File.Exists(target) || Directory.Exists(target)) && console.Confirm($"{Markup.Escape(target)} exists. Overwrite existing files?", false);

        Cancellable(console, token =>
        {
            var extraction = stream is null
                ? Extraction.Run(fs, path, destination, force, TextWriter.Null, console, token)
                : Extraction.RunStream(fs, path, stream.Name, stream.Length, stream.SnapshotTime?.UtcDateTime ?? fs.GetLastWriteTimeUtc(path), destination,
                    force, console, token);

            console.MarkupLine(extraction.Failures == 0 ? $"[green]{extraction.Summary}[/]" : $"[yellow]{extraction.Summary}[/]");
        });
    }

    /// <summary>Runs a long operation that Ctrl+C stops (back to the menu) instead of ending the program; read errors are shown.</summary>
    private static void Cancellable(IAnsiConsole console, Action<CancellationToken> action)
    {
        using var cancellation = new CancellationTokenSource();

        void Stop(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += Stop;

        try
        {
            Guard(console, () => action(cancellation.Token));
        }
        catch (OperationCanceledException)
        {
            console.MarkupLine("[yellow]Cancelled.[/]");
        }
        finally
        {
            Console.CancelKeyPress -= Stop;
        }
    }

    private static bool Guard(IAnsiConsole console, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
        {
            console.MarkupLine($"[red]{Markup.Escape(ex.Message)}[/]");
            return false;
        }
    }

    private enum MenuExit
    {
        Quit,
        OtherImage,
        OtherVolume
    }
}