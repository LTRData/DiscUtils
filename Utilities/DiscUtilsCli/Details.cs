using System.Globalization;
using DiscUtils;
using DiscUtils.ReFs;
using DiscUtils.ReFs.Diagnostics.Enums;
using DiscUtils.ReFs.Entries.Enums;
using LTRData.Extensions.Formatting;
using Spectre.Console;

namespace DiscUtilsCli;

/// <summary>Label and value rows for <c>info</c> and <c>stat</c>, written as aligned plain lines or as a table on a terminal.</summary>
internal static class Details
{
    /// <summary>The file system and, for ReFS, the volume's own details.</summary>
    public static List<(string Label, string Value)> Volume(OpenedImage opened)
    {
        var fs = opened.FileSystem;
        var rows = new List<(string, string)>
        {
            ("File system", $"{opened.FileSystemName} ({fs.FriendlyName})"),
            ("Volume", opened.VolumeIndex < 0 ? opened.Volume : $"{opened.VolumeIndex} ({opened.Volume})"),
            ("Size", SizeWithBytes(fs.Size)),
            ("Label", fs.VolumeLabel ?? string.Empty)
        };

        if (TryGetSpace(fs, out var used, out var available, out var error))
        {
            rows.Add(("Used", SizeWithBytes(used)));
            rows.Add(("Available", SizeWithBytes(available)));
        }
        else if (error is not null)
        {
            rows.Add(("Used", $"unavailable: {error}"));
        }

        if (opened.ReFs is { } refs)
        {
            var info = refs.GetVolumeDetails();
            rows.AddRange(
            [
                ("ReFS version", $"{info.FormatVersion} (boot sector {info.BootSectorVersion}, checkpoint second pair {info.SecondaryCheckpointVersion})"),
                ("Cluster size", $"{info.ClusterSize}"),
                ("Volume size", $"{info.TotalClusters * info.ClusterSize} bytes ({info.TotalClusters} clusters)"),
                ("Checksums", $"metadata {ChecksumName(info.MetadataChecksum)}, integrity streams "
                              + (info.DataChecksum == ReFsChecksumKind.None
                                  ? "not supported by this reader"
                                  : $"{ChecksumName(info.DataChecksum)} per {info.DataChecksumBlockSize} byte block")),
                ("Serial", $"0x{info.Serial:X16}"),
                ("Volume id", $"{info.VolumeId}"),
                ("Created", $"{info.Created:O}"),
                ("Last mounted", $"{info.LastMounted:O} (version {info.LastMountedByVersion}, volume {info.VolumeVersion}, flags 0x{info.VolumeFlags:X})"),
                ("Checkpoint", $"{info.CheckpointClock} (restart clock 0x{info.RestartClock:X})"),
                ("Replay", info.ReplayEnabled ? "enabled (experimental)" : "disabled"),
                ("Dev Drive", info.DeveloperVolume ? "yes (developer volume)" : "no"),
                ("Flags", $"0x{info.CheckpointFlags:X} {info.CheckpointFlagNames}")
            ]);
        }

        return rows;
    }

    /// <summary>
    ///     Used and available bytes when the file system driver reports them (<see cref="DiscFileSystem.SupportsUsedAvailableSpace" />). Returns false with a
    ///     null <paramref name="error" /> when the driver does not support it, and with the reason when reading the figures failed (a damaged volume can
    ///     still be browsed).
    /// </summary>
    public static bool TryGetSpace(DiscFileSystem fs, out long used, out long available, out string? error)
    {
        used = available = 0;
        error = null;

        if (!fs.SupportsUsedAvailableSpace)
        {
            return false;
        }

        try
        {
            used = fs.UsedSpace;
            available = fs.AvailableSpace;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or NotImplementedException or OverflowException)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    ///     The rows of <c>stat</c> and the browser's details pane. <paramref name="humanized" /> (the details view on a terminal) writes sizes as
    ///     "68.4 KB (70001)"; plain output keeps the exact byte counts. <paramref name="includeStreams" /> lists the streams as rows.
    /// </summary>
    public static List<(string Label, string Value)> Entry(DiscFileSystem fs, string path, bool humanized = false, bool includeStreams = true)
    {
        Func<long, string> size = humanized ? SizeWithBytes : bytes => $"{bytes}";
        var directory = fs.DirectoryExists(path);
        var attributes = fs.GetAttributes(path);
        var rows = new List<(string, string)>
        {
            ("Name", Name(path)),
            ("Kind", directory ? "Directory" : "File")
        };

        if (!directory)
        {
            rows.Add(("Size", size(fs.GetFileLength(path))));
        }

        rows.Add(("Attributes", $"{attributes} (0x{(int)attributes:X})"));
        rows.Add(("Created", $"{fs.GetCreationTimeUtc(path):O}"));
        rows.Add(("Modified", $"{fs.GetLastWriteTimeUtc(path):O}"));

        if (fs is IWindowsFileSystem windows)
        {
            rows.Add(("Changed", $"{windows.GetFileStandardInformation(path).ChangeTime:O}"));
        }

        rows.Add(("Accessed", $"{fs.GetLastAccessTimeUtc(path):O}"));

        if (fs is ReFsFileSystem refs)
        {
            var entry = refs.GetEntryDetails(path);
            rows.Add(directory ? ("Object id", $"0x{entry.ObjectId:X}") : ("File id", $"0x{entry.FileId:X}"));

            if (!directory)
            {
                rows.Add(("Allocated", size((long)entry.AllocatedLength)));
            }

            // The integrity-stream attribute decides whether a file's data has checksums; on a directory it is the default for new files.
            var kind = refs.GetVolumeDetails().DataChecksum;
            var kindName = kind == ReFsChecksumKind.None ? "a kind this reader does not support" : ChecksumName(kind);
            rows.Add(("Checksums", directory
                ? entry.IntegrityStream ? $"new files are integrity streams ({kindName})" : "new files are not integrity streams"
                : entry.IntegrityStream
                    ? $"{kindName} (integrity stream)"
                    : "none (not an integrity stream)"));

            if (entry.Replay != ReFsReplayState.None)
            {
                rows.Add(("Replay", $"{entry.Replay}"));
            }

            foreach (var stream in includeStreams ? entry.Streams : [])
            {
                rows.Add(stream.IsSnapshot
                    ? ("Snapshot", $"'{stream.Name}' {StreamSize(humanized, stream.Length)}, taken {stream.SnapshotTime:O}")
                    : ("Stream", $"'{stream.Name}' {StreamSize(humanized, stream.Length)}{(stream.IsResident ? " (inline)" : string.Empty)}"));
            }
        }
        else
        {
            if (fs is IWindowsFileSystem windowsIds)
            {
                rows.Add(("File id", $"0x{windowsIds.GetFileId(path):X}"));
            }

            if (includeStreams && !directory && fs is IFileSystemWithAltStreams streams)
            {
                foreach (var name in streams.GetAlternateDataStreams(path))
                {
                    rows.Add(("Stream", $"'{name}'"));
                }
            }
        }

        if (fs is IWindowsFileSystem links && !directory && links.GetHardLinkCount(path) > 1)
        {
            rows.Add(("Hard links", $"{links.GetHardLinkCount(path)}"));
        }

        return rows;
    }

    /// <summary>Plain lines, labels padded to <paramref name="width" />, or a two-column table on a terminal.</summary>
    public static void Write(CliContext cli, IReadOnlyList<(string Label, string Value)> rows, int width, string? title = null)
    {
        if (cli.Terminal is null)
        {
            foreach (var (label, value) in rows)
            {
                cli.Output.WriteLine($"{label.PadRight(width)} : {value}");
            }

            return;
        }

        cli.Render(terminal => terminal.Write(Table(rows, title)));
    }

    /// <summary>The volumes of an image as a table; the one marked <paramref name="current" /> (a volume index) gets an arrow.</summary>
    public static Table VolumeTable(IReadOnlyList<VolumeChoice> volumes, int? current = null, string? title = "Volumes")
    {
        var table = new Table().Border(TableBorder.Rounded)
                               .AddColumn("#").AddColumn(new TableColumn("Offset").RightAligned()).AddColumn(new TableColumn("Size").RightAligned())
                               .AddColumn("Type").AddColumn("File system").AddColumn("Identity");

        if (title is not null)
        {
            table.Title($"[bold]{Markup.Escape(title)}[/]");
        }

        foreach (var volume in volumes)
        {
            var number = volume.Index == OpenedImage.WholeDisk ? "disk" : volume.Index.ToString(CultureInfo.InvariantCulture);
            table.AddRow(
                new Text((volume.Index == current ? "▶ " : "  ") + number),
                new Text(Size(volume.Start)),
                new Text(Size(volume.Length)),
                new Text(volume.Type),
                volume.FileSystem is null ? new Markup("[grey]none detected[/]") : new Markup($"[green]{Markup.Escape(volume.FileSystem)}[/]"),
                new Markup($"[grey]{Markup.Escape(volume.Identity)}[/]"));
        }

        return table;
    }

    public static Table Table(IReadOnlyList<(string Label, string Value)> rows, string? title = null)
    {
        var table = new Table().Border(TableBorder.Rounded).HideHeaders().AddColumn("").AddColumn("");

        if (title is not null)
        {
            table.Title($"[bold]{Markup.Escape(title)}[/]");
        }

        foreach (var (label, value) in rows)
        {
            table.AddRow(new Markup($"[grey]{Markup.Escape(label)}[/]"), new Text(value));
        }

        return table;
    }

    /// <summary>The usual name of a checksum kind.</summary>
    public static string ChecksumName(ReFsChecksumKind kind) =>
        kind switch
        {
            ReFsChecksumKind.Crc32C => "CRC32C",
            ReFsChecksumKind.Crc64 => "CRC64",
            ReFsChecksumKind.Sha256 => "SHA-256",
            ReFsChecksumKind.Xxh64 => "XXH64",
            ReFsChecksumKind.None => "none",
            _ => $"type {(int)kind}"
        };

    /// <summary>A size for people: "512 B", "68.4 KiB".</summary>
    public static string Size(long bytes) => bytes < 1024 ? $"{bytes} B" : SizeFormatting.FormatBytes(bytes);

    /// <summary>The details view's form: "512 B" below a kilobyte, otherwise the size for people with the exact bytes.</summary>
    public static string SizeWithBytes(long bytes) => bytes < 1024 ? $"{bytes} B" : string.Format(CultureInfo.InvariantCulture, "{0} ({1})", Size(bytes), bytes);

    private static string StreamSize(bool humanized, ulong length) => humanized ? SizeWithBytes((long)length) : $"{length} bytes";

    public static string AttributeLetters(FileAttributes attributes)
    {
        char[] letters = ['-', '-', '-', '-', '-', '-', '-'];

        if (attributes.HasFlag(FileAttributes.Directory))
        {
            letters[0] = 'd';
        }

        if (attributes.HasFlag(FileAttributes.ReadOnly))
        {
            letters[1] = 'r';
        }

        if (attributes.HasFlag(FileAttributes.Hidden))
        {
            letters[2] = 'h';
        }

        if (attributes.HasFlag(FileAttributes.System))
        {
            letters[3] = 's';
        }

        if (attributes.HasFlag(FileAttributes.Archive))
        {
            letters[4] = 'a';
        }

        if (attributes.HasFlag(FileAttributes.SparseFile))
        {
            letters[5] = 'p';
        }

        if (attributes.HasFlag(FileAttributes.Encrypted))
        {
            letters[6] = 'e';
        }

        return new string(letters);
    }

    /// <summary>A path inside the file system as DiscUtils takes it: backslash separators, no leading or trailing separator; "" is the root.</summary>
    public static string NormalizePath(string path) => path.Replace('/', '\\').Trim('\\');

    public static string Combine(string directory, string name) => directory.Length == 0 ? name : $"{directory}\\{name}";

    /// <summary>The last component of a path; "\" for the root.</summary>
    public static string Name(string path)
    {
        var trimmed = path.TrimEnd('\\');

        if (trimmed.Length == 0)
        {
            return "\\";
        }

        var slash = trimmed.LastIndexOf('\\');
        return slash < 0 ? trimmed : trimmed.Substring(slash + 1);
    }
}