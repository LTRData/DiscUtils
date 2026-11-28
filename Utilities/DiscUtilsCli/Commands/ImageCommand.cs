using DiscUtils;
using DiscUtils.Windows;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DiscUtilsCli.Commands;

/// <summary>
///     Base of the commands that open an image. Handles a wildcard in the image's file name (the command runs on every match, in name order,
///     each under a "== path" heading; a failing image does not stop the others), opening (a spinner on a terminal) and the errors that end a
///     command with exit code 1.
/// </summary>
internal abstract class ImageCommand<TSettings> : Command<TSettings> where TSettings : ImageSettings
{
    /// <summary>True for a command whose output cannot be split by image (<c>cat</c>).</summary>
    protected virtual bool SingleImageOnly => false;

    public sealed override int Execute(CommandContext context, TSettings settings, CancellationToken cancellationToken)
    {
        var cli = (CliContext)context.Data!;
        return HasWildcard(settings.Image)
            ? RunOnMatches(cli, settings, cancellationToken)
            : RunOnImage(cli, settings, settings.Image, null, cancellationToken);
    }

    /// <summary>Runs the command on one open image. <paramref name="image" /> is set when several images are processed.</summary>
    protected abstract int Run(CliContext cli, OpenedImage opened, TSettings settings, string? image, CancellationToken cancellationToken);

    /// <summary>True when the file name of the image argument holds <c>*</c> or <c>?</c> (only the last path segment is a pattern).</summary>
    internal static bool HasWildcard(string image) => !DeviceDisk.IsDevicePath(image) && Path.GetFileName(image).IndexOfAny(['*', '?']) >= 0;

    /// <summary>
    ///     The failures a command reports as an error line (exit code 1, or a failed file in an extraction) rather than a crash: read errors,
    ///     damaged or unsupported data, and features a file system reader does not implement yet.
    /// </summary>
    internal static bool IsReadFailure(Exception ex) =>
        ex is IOException or NotSupportedException or NotImplementedException or UnauthorizedAccessException or InvalidFileSystemException
            or InvalidDataException;

    private int RunOnMatches(CliContext cli, TSettings settings, CancellationToken cancellationToken)
    {
        var pattern = settings.Image;
        List<string> images;

        try
        {
            var directory = Path.GetDirectoryName(pattern) is { Length: > 0 } named ? named : ".";
            images = [.. Directory.GetFiles(directory, Path.GetFileName(pattern)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            cli.Error.WriteLine($"error: {ex.Message}");
            return CliRunner.Failure;
        }

        if (images.Count == 0)
        {
            cli.Error.WriteLine($"error: No image matches '{pattern}'.");
            return CliRunner.Failure;
        }

        if (SingleImageOnly && images.Count > 1)
        {
            cli.Error.WriteLine($"This command writes one stream to standard output, but '{pattern}' matches {images.Count} images.");
            return CliRunner.Usage;
        }

        var failed = 0;

        foreach (var image in images)
        {
            if (!SingleImageOnly)
            {
                cli.Output.WriteLine($"== {image}");
            }

            if (RunOnImage(cli, settings, image, image, cancellationToken) != CliRunner.Ok)
            {
                failed++;
            }

            if (!SingleImageOnly)
            {
                cli.Output.WriteLine();
            }
        }

        if (!SingleImageOnly)
        {
            cli.Output.WriteLine($"{images.Count} image(s), {failed} failed.");
        }

        return failed == 0 ? CliRunner.Ok : CliRunner.Failure;
    }

    private int RunOnImage(CliContext cli, TSettings settings, string image, string? errorPrefix, CancellationToken cancellationToken)
    {
        try
        {
            using var opened = Open(cli, settings, image);
            return Run(cli, opened, settings, errorPrefix, cancellationToken);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            cli.Error.WriteLine(errorPrefix is null ? $"error: {ex.Message}" : $"error: {errorPrefix}: {ex.Message}");
            return CliRunner.Failure;
        }
    }

    private static OpenedImage Open(CliContext cli, ImageSettings settings, string image)
    {
        if (cli.Terminal is null)
        {
            return OpenedImage.Open(image, settings.Options);
        }

        OpenedImage? opened = null;
        cli.Render(terminal => terminal.Status().Spinner(Spinner.Known.Dots).Start($"Opening {Markup.Escape(image)}...",
            _ => opened = OpenedImage.Open(image, settings.Options)));

        return opened!;
    }
}