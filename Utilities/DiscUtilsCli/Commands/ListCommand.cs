using System.ComponentModel;
using System.Globalization;
using DiscUtils;

namespace DiscUtilsCli.Commands;

/// <summary>Plain lines in every case: listings are for reading and for scripts, and a recursive one can be millions of lines.</summary>
[Description("List a directory")]
internal sealed class ListCommand : ImageCommand<ListSettings>
{
    protected override int Run(CliContext cli, OpenedImage opened, ListSettings settings, string? image, CancellationToken cancellationToken)
    {
        var fs = opened.FileSystem;
        var path = Details.NormalizePath(settings.Path);

        if (fs.FileExists(path))
        {
            WriteLine(cli.Output, fs, path, Details.Name(path), settings.Long);
            return CliRunner.Ok;
        }

        if (!fs.DirectoryExists(path))
        {
            throw new FileNotFoundException($"'{settings.Path}' was not found.");
        }

        ListDirectory(cli.Output, fs, path, settings, settings.Recursive, cancellationToken);
        return CliRunner.Ok;
    }

    private static void ListDirectory(TextWriter output, DiscFileSystem fs, string path, ListSettings settings, bool header, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (header)
        {
            output.WriteLine($"{(path.Length == 0 ? "\\" : path)}:");
        }

        var directories = fs.GetDirectories(path).OrderBy(Details.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var entries = directories.Select(d => (Path: d, Directory: true))
                                 .Concat(fs.GetFiles(path).Select(f => (Path: f, Directory: false)))
                                 .OrderBy(e => Details.Name(e.Path), StringComparer.OrdinalIgnoreCase);

        foreach (var (entryPath, directory) in entries)
        {
            WriteLine(output, fs, entryPath, directory ? Details.Name(entryPath) + "\\" : Details.Name(entryPath), settings.Long);
        }

        if (!settings.Recursive)
        {
            return;
        }

        foreach (var subdirectory in directories)
        {
            output.WriteLine();
            ListDirectory(output, fs, subdirectory, settings, true, cancellationToken);
        }
    }

    private static void WriteLine(TextWriter output, DiscFileSystem fs, string path, string display, bool longFormat)
    {
        if (!longFormat)
        {
            output.WriteLine(display);
            return;
        }

        var info = fs.GetFileSystemInfo(path);
        var length = info is DiscFileInfo file ? file.Length : 0;
        output.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0} {1,14} {2:yyyy-MM-dd HH:mm:ss} {3}",
            Details.AttributeLetters(info.Attributes), length, info.LastWriteTimeUtc, display));
    }
}