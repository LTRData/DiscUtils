using DiscUtils;
using DiscUtilsCli.Commands;
using Spectre.Console;

namespace DiscUtilsCli;

/// <summary>
///     Copies a file or a directory tree out of a file system, keeping creation and modification times. On a terminal it first measures the tree
///     (a metadata walk) and shows a progress bar by bytes; otherwise it copies silently and writes failures to the error writer as they occur.
/// </summary>
internal sealed class Extraction
{
    private const int BufferSize = 1 << 20;
    private readonly byte[] _buffer = new byte[BufferSize];
    private readonly List<string> _failures = [];

    public int Files { get; private set; }

    public int Directories { get; private set; }

    public long Bytes { get; private set; }

    public int Failures => _failures.Count;

    public string Summary => $"Extracted {Files} file(s), {Directories} directorie(s), {Bytes} bytes; {Failures} failure(s).";

    /// <summary>Extracts the file or directory at <paramref name="path" /> into <paramref name="destination" />, which is created.</summary>
    public static Extraction Run(DiscFileSystem fs, string path, string destination, bool force, TextWriter error, IAnsiConsole? terminal,
        CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(destination);
        Directory.CreateDirectory(root);
        var target = path.Length == 0 ? root : Path.Combine(root, Details.Name(path));
        var extraction = new Extraction();

        if (terminal is null)
        {
            extraction.Copy(fs, path, target, root, force, null, failure =>
            {
                extraction._failures.Add(failure);
                error.WriteLine(failure);
            }, cancellationToken);

            return extraction;
        }

        var (files, bytes) = (0, 0L);
        terminal.Status().Spinner(Spinner.Known.Dots).Start("Measuring what to extract...", status =>
            (files, bytes) = Measure(fs, path, status, cancellationToken));

        terminal.Progress()
                .AutoClear(false)
                .Columns(new TaskDescriptionColumn { Alignment = Justify.Left }, new ProgressBarColumn(), new PercentageColumn(), new DownloadedColumn(),
                    new TransferSpeedColumn(), new RemainingTimeColumn(), new SpinnerColumn())
                .Start(context =>
                {
                    var task = context.AddTask($"Extracting {files} file(s)", maxValue: Math.Max(bytes, 1));
                    extraction.Copy(fs, path, target, root, force, (copied, name) =>
                    {
                        task.Increment(copied);

                        if (name is not null)
                        {
                            task.Description = $"Extracting {extraction.Files + 1}/{files} [grey]{Markup.Escape(Shorten(name, 50))}[/]";
                        }
                    }, extraction._failures.Add, cancellationToken);

                    task.Description = $"Extracted {extraction.Files} file(s)";
                    task.Value = task.MaxValue;
                });

        foreach (var failure in extraction._failures.Take(50))
        {
            terminal.MarkupLine($"[red]{Markup.Escape(failure)}[/]");
        }

        if (extraction._failures.Count > 50)
        {
            terminal.MarkupLine($"[red]... and {extraction._failures.Count - 50} more failure(s)[/]");
        }

        return extraction;
    }

    /// <summary>
    ///     The file name one stream of a file is extracted to: "name_stream" (characters a file name cannot hold become '_'), so it works on any
    ///     destination volume, not only one with alternate data streams.
    /// </summary>
    public static string StreamFileName(string path, string stream)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return $"{Details.Name(path)}_{new string(stream.Select(c => invalid.Contains(c) ? '_' : c).ToArray())}";
    }

    /// <summary>Extracts one stream (a named stream or, on ReFS, a snapshot) of a file to <see cref="StreamFileName" />, with a progress bar.</summary>
    public static Extraction RunStream(DiscFileSystem fs, string path, string stream, long length, DateTime modified, string destination, bool force,
        IAnsiConsole terminal, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destination);
        var target = Path.Combine(Path.GetFullPath(destination), StreamFileName(path, stream));
        var extraction = new Extraction();

        terminal.Progress()
                .AutoClear(false)
                .Columns(new TaskDescriptionColumn { Alignment = Justify.Left }, new ProgressBarColumn(), new PercentageColumn(), new DownloadedColumn(),
                    new TransferSpeedColumn(), new RemainingTimeColumn(), new SpinnerColumn())
                .Start(context =>
                {
                    var task = context.AddTask($"Extracting [grey]{Markup.Escape(path)}:{Markup.Escape(stream)}[/]", maxValue: Math.Max(length, 1));

                    try
                    {
                        using (var source = FileContent.OpenRead(fs, path, stream))
                        using (var output = new FileStream(target, force ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            int read;

                            while ((read = source.Read(extraction._buffer, 0, extraction._buffer.Length)) > 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                output.Write(extraction._buffer, 0, read);
                                extraction.Bytes += read;
                                task.Increment(read);
                            }
                        }

                        File.SetCreationTimeUtc(target, fs.GetCreationTimeUtc(path));
                        File.SetLastWriteTimeUtc(target, modified);
                        extraction.Files++;
                    }
                    catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
                    {
                        extraction._failures.Add($"failed: {path}:{stream}: {ex.Message}");
                    }

                    task.Value = task.MaxValue;
                });

        foreach (var failure in extraction._failures)
        {
            terminal.MarkupLine($"[red]{Markup.Escape(failure)}[/]");
        }

        if (extraction.Files == 1)
        {
            terminal.MarkupLine($"[grey]Written to {Markup.Escape(target)}[/]");
        }

        return extraction;
    }

    /// <summary>Files and bytes (default streams) below a path: a metadata walk.</summary>
    private static (int Files, long Bytes) Measure(DiscFileSystem fs, string path, StatusContext status, CancellationToken cancellationToken)
    {
        if (!fs.DirectoryExists(path))
        {
            return (1, fs.GetFileLength(path));
        }

        var files = 0;
        long bytes = 0;
        var directories = 0;
        var pending = new Stack<string>([path]);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                foreach (var child in fs.GetDirectories(directory))
                {
                    pending.Push(child);
                }

                foreach (var file in fs.GetFiles(directory))
                {
                    files++;
                    bytes += fs.GetFileLength(file);
                }
            }
            catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
            {
                // Reported by the copy, which meets the same failure.
            }

            if (++directories % 256 == 0)
            {
                status.Status($"Measuring what to extract: {files} file(s), {Details.Size(bytes)}...");
            }
        }

        return (files, bytes);
    }

    private void Copy(DiscFileSystem fs, string path, string target, string root, bool force, Action<long, string?>? progress, Action<string> fail,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var full = Path.GetFullPath(target);
        var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar);

        if (!full.Equals(trimmedRoot, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            fail($"skipped (outside destination): {path}");
            return;
        }

        try
        {
            if (fs.DirectoryExists(path))
            {
                Directory.CreateDirectory(full);
                Directories++;

                foreach (var child in fs.GetFileSystemEntries(path))
                {
                    Copy(fs, child, Path.Combine(full, Details.Name(child)), root, force, progress, fail, cancellationToken);
                }

                Directory.SetCreationTimeUtc(full, fs.GetCreationTimeUtc(path));
                Directory.SetLastWriteTimeUtc(full, fs.GetLastWriteTimeUtc(path));
                return;
            }

            progress?.Invoke(0, path);

            using (var source = FileContent.OpenRead(fs, path))
            using (var destination = new FileStream(full, force ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                int read;

                while ((read = source.Read(_buffer, 0, _buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    destination.Write(_buffer, 0, read);
                    Bytes += read;
                    progress?.Invoke(read, null);
                }
            }

            File.SetCreationTimeUtc(full, fs.GetCreationTimeUtc(path));
            File.SetLastWriteTimeUtc(full, fs.GetLastWriteTimeUtc(path));
            Files++;
        }
        catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
        {
            fail($"failed: {path}: {ex.Message}");
        }
    }

    internal static string Shorten(string text, int length) => length <= 3 || text.Length <= length ? text : "..." + text.Substring(text.Length - (length - 3));
}