using System.Text;
using DiscUtils;
using DiscUtils.ReFs;
using DiscUtils.ReFs.Diagnostics;
using DiscUtilsCli.Commands;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DiscUtilsCli.Interactive;

/// <summary>
///     The interactive mode's file browser: a directory tree on the left (directories open in place, children read when first opened) and the
///     details of the entry under the cursor on the right, redrawn on every key.
///     <list type="table">
///         <item>
///             <term>Up, Down, Page Up, Page Down, Home, End</term><description>move</description>
///         </item>
///         <item>
///             <term>Right</term><description>open a directory, or go to its first entry when it is open</description>
///         </item>
///         <item>
///             <term>Left</term><description>close a directory, or go to the parent</description>
///         </item>
///         <item>
///             <term>Enter</term><description>open or close a directory; on a file, extract it</description>
///         </item>
///         <item>
///             <term>E</term><description>extract the entry under the cursor (file or directory)</description>
///         </item>
///         <item>
///             <term>Tab</term><description>select a stream of the file for E (ReFS: named streams and snapshots)</description>
///         </item>
///         <item>
///             <term>V</term><description>verify (ReFS: against checksums) or read the data of the file, or of every file below the directory</description>
///         </item>
///         <item>
///             <term>Ctrl+Page Down, Ctrl+Page Up</term><description>ReFS: step through the redo log written after the checkpoint</description>
///         </item>
///         <item>
///             <term>Esc, Q, Backspace</term><description>back to the menu</description>
///         </item>
///     </list>
///     Verified files turn green, failures red; a directory is green when nothing below it failed and at least one file was verified, red
///     when anything below it failed. Extraction and verification leave the live display; the tree, cursor, open directories and results are
///     kept for when they return.
/// </summary>
internal sealed class TreeBrowser
{
    private const int MinimumRows = 5;
    private const int RecordsShown = 6;
    private readonly IAnsiConsole _console;
    private readonly Func<int, OpenedImage>? _openAtStep;
    private readonly List<ReFsRedoTransaction> _transactions = [];
    private HashSet<ulong> _changedDirectories = [];
    private HashSet<(ulong Directory, string Name)> _changedNames = [];
    private int _cursor;
    private DiscFileSystem _fs;
    private OpenedImage? _owned;
    private Node _root;
    private int _step;
    private string? _stepError;
    private int _top;
    private List<Node> _visible = [];

    /// <summary>
    ///     Browses <paramref name="fs" />. With <paramref name="openAtStep" /> (ReFS: opens the same image with the first n committed redo
    ///     transactions applied) Ctrl+Page Down and Ctrl+Page Up step through the redo log written after the checkpoint.
    /// </summary>
    public TreeBrowser(IAnsiConsole console, DiscFileSystem fs, Func<int, OpenedImage>? openAtStep = null)
    {
        _console = console;
        _fs = fs;
        _openAtStep = fs is ReFsFileSystem ? openAtStep : null;
        _root = new Node(string.Empty, true, -1, null);
        Open(_root);
        Rebuild();

        if (_openAtStep is not null && fs is ReFsFileSystem refs)
        {
            try
            {
                if (refs.ReadRedoLog() is { } log)
                {
                    _transactions.AddRange(log.Transactions.Where(t => t.Committed));
                }
            }
            catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
            {
                _stepError = $"The redo log cannot be read: {ex.Message}";
            }
        }
    }

    /// <summary>Tree rows that fit: the console height less the panel borders, the key line and one spare line (no scrolling).</summary>
    private int Rows => Math.Max(MinimumRows, _console.Profile.Height - 4);

    /// <summary>
    ///     Browses until the user goes back. <paramref name="extract" /> is called (outside the live display) for each extraction asked for, with
    ///     the file system as currently shown (it changes while stepping through the log), the path and the stream chosen with Tab (null: the
    ///     whole file or directory); <paramref name="cancellable" /> runs a verification so that Ctrl+C stops it instead of the program.
    /// </summary>
    public void Run(Action<DiscFileSystem, string, StreamChoice?> extract, Action<Action<CancellationToken>> cancellable)
    {
        try
        {
            while (Browse() is { } chosen)
            {
                if (chosen.Action == KeyResult.Extract)
                {
                    extract(_fs, chosen.Node.Path, chosen.Node.SelectedStream);
                }
                else
                {
                    cancellable(token => Verify(chosen.Node, token));
                }
            }
        }
        finally
        {
            _owned?.Dispose(); // an image opened for a log step; the one passed in belongs to the caller
        }
    }

    /// <summary>
    ///     Shows the volume with the first <paramref name="step" /> committed transactions applied: opens it again, then opens the same directories
    ///     and puts the cursor on the same path (or the nearest one that still exists). Verification results are dropped, since the data may differ.
    ///     When the step cannot be applied the view stays where it was and the reason is shown.
    /// </summary>
    private void StepTo(int step)
    {
        var expanded = new HashSet<string>(AllNodes(_root).Where(n => n.IsDirectory && n.Expanded).Select(n => n.Path), StringComparer.OrdinalIgnoreCase);
        var cursorPath = _visible.Count > 0 ? _visible[_cursor].Path : string.Empty;
        OpenedImage next;

        try
        {
            next = _openAtStep!(step);
        }
        catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
        {
            _stepError = $"Transaction {step} cannot be applied: {ex.Message}";
            return;
        }

        _owned?.Dispose();
        _owned = next;
        _fs = next.FileSystem;
        _step = step;
        _stepError = null;
        (_changedDirectories, _changedNames) = Changes(step > 0 ? _transactions[step - 1] : null);

        _root = new Node(string.Empty, true, -1, null);
        Open(_root);
        Reopen(_root, expanded);
        Rebuild();

        // The cursor goes back to the same path, or to the nearest ancestor still shown.
        for (var path = cursorPath;; path = ParentPath(path))
        {
            var index = _visible.FindIndex(n => n.Path.Equals(path, StringComparison.OrdinalIgnoreCase));

            if (index >= 0 || path.Length == 0)
            {
                _cursor = Math.Max(0, index);
                break;
            }
        }
    }

    private static string ParentPath(string path)
    {
        var slash = path.LastIndexOf('\\');
        return slash < 0 ? string.Empty : path.Substring(0, slash);
    }

    private void Reopen(Node node, HashSet<string> expanded)
    {
        foreach (var child in node.Children ?? [])
        {
            if (child.IsDirectory && expanded.Contains(child.Path))
            {
                Open(child);
                Reopen(child, expanded);
            }
        }
    }

    private static IEnumerable<Node> AllNodes(Node node)
    {
        foreach (var child in node.Children ?? [])
        {
            yield return child;

            foreach (var below in AllNodes(child))
            {
                yield return below;
            }
        }
    }

    /// <summary>
    ///     What a transaction touched, as the tree can show it: the directory tables its records change (the first component of a record's path is
    ///     the table's object id) and the names in them (a name key 0x30 0 1 0 + UTF-16, in the path or in the first value).
    /// </summary>
    private static (HashSet<ulong>, HashSet<(ulong, string)>) Changes(ReFsRedoTransaction? transaction)
    {
        var directories = new HashSet<ulong>();
        var names = new HashSet<(ulong, string)>();

        foreach (var record in transaction?.Records ?? [])
        {
            if (record.Path.Count == 0 || record.Path[0].Kind != ReFsRedoPathComponent.RootKind)
            {
                continue;
            }

            var directory = record.Path[0].ObjectId;
            directories.Add(directory);

            ReadOnlyMemory<byte>? key = record.Path.Count > 1 ? record.Path[1].Key : record.Values.Count > 0 ? record.Values[0] : null;

            if (key is { Length: > 4 } name && name.Span[0] == 0x30)
            {
                names.Add((directory, Encoding.Unicode.GetString(name.Span.Slice(4).ToArray())));
            }
        }

        return (directories, names);
    }

    private bool IsChanged(Node node)
    {
        if (_changedDirectories.Count == 0 || _fs is not ReFsFileSystem)
        {
            return false;
        }

        return (node.IsDirectory && _changedDirectories.Contains(ObjectId(node)))
               || (node.Parent is { } parent && _changedNames.Contains((ObjectId(parent), node.Name)));
    }

    /// <summary>A directory's table id on ReFS (cached on the node); the root's is 0x600.</summary>
    private ulong ObjectId(Node node)
    {
        if (node.ObjectId is null && _fs is ReFsFileSystem refs)
        {
            try
            {
                node.ObjectId = refs.GetEntryDetails(node.Path).ObjectId;
            }
            catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
            {
                node.ObjectId = 0;
            }
        }

        return node.ObjectId ?? 0;
    }

    /// <summary>The redo log position and the transaction last applied, for the details pane.</summary>
    private Rows? LogPanel()
    {
        if (_openAtStep is null || (_transactions.Count == 0 && _stepError is null))
        {
            return null;
        }

        var lines = new List<IRenderable>();

        if (_stepError is not null)
        {
            lines.Add(new Markup($"[red]{Markup.Escape(_stepError)}[/]"));
        }

        if (_step == 0)
        {
            lines.Add(new Markup($"[grey]Redo log: none of {_transactions.Count} committed transaction(s) applied (the checkpoint's state). Ctrl+PgDn applies the next.[/]"));
        }
        else
        {
            var transaction = _transactions[_step - 1];
            lines.Add(new Markup($"[yellow]Redo log: transaction {_step} of {_transactions.Count} applied[/] [grey]({transaction.Records.Count} record(s); changed entries in yellow)[/]"));

            foreach (var record in transaction.Records.Take(RecordsShown))
            {
                lines.Add(new Text($"  {record.Operation} [{LogCommand.DescribePath(record)}]"));
            }

            if (transaction.Records.Count > RecordsShown)
            {
                lines.Add(new Markup($"[grey]  ... and {transaction.Records.Count - RecordsShown} more[/]"));
            }
        }

        return new Rows(lines);
    }

    /// <summary>One live session: returns what to do with which node, or null to go back to the menu.</summary>
    private (KeyResult Action, Node Node)? Browse()
    {
        (KeyResult, Node)? chosen = null;
        _console.Live(Render()).AutoClear(true).Overflow(VerticalOverflow.Crop).Cropping(VerticalOverflowCropping.Bottom).Start(context =>
        {
            // The live display draws only on a refresh: without this the tree stays blank until the first key.
            context.Refresh();

            while (true)
            {
                var read = _console.Input.ReadKey(true);

                if (read is not { } key)
                {
                    return;
                }

                var result = Handle(key);

                if (result == KeyResult.Back)
                {
                    return;
                }

                if (result is KeyResult.Extract or KeyResult.Verify)
                {
                    chosen = (result, _visible[_cursor]);
                    return;
                }

                context.UpdateTarget(Render());
            }
        });

        return chosen;
    }

    private KeyResult Handle(ConsoleKeyInfo key)
    {
        if (_visible.Count == 0)
        {
            return key.Key is ConsoleKey.Escape or ConsoleKey.Q or ConsoleKey.Backspace ? KeyResult.Back : KeyResult.Stay;
        }

        var node = _visible[_cursor];
        var page = Math.Max(1, Rows - 1);
        var control = (key.Modifiers & ConsoleModifiers.Control) != 0;

        switch (key.Key)
        {
            case ConsoleKey.UpArrow:
                Move(-1);
                break;
            case ConsoleKey.DownArrow:
                Move(1);
                break;
            case ConsoleKey.PageDown when control:
                if (_openAtStep is not null && _step < _transactions.Count)
                {
                    StepTo(_step + 1);
                }

                break;
            case ConsoleKey.PageUp when control:
                if (_openAtStep is not null && _step > 0)
                {
                    StepTo(_step - 1);
                }

                break;
            case ConsoleKey.PageUp:
                Move(-page);
                break;
            case ConsoleKey.PageDown:
                Move(page);
                break;
            case ConsoleKey.Home:
                Move(-_visible.Count);
                break;
            case ConsoleKey.End:
                Move(_visible.Count);
                break;
            case ConsoleKey.RightArrow when node.IsDirectory:
                if (!node.Expanded)
                {
                    Toggle(node);
                }
                else if (node.Children is { Count: > 0 })
                {
                    Move(1);
                }

                break;
            case ConsoleKey.LeftArrow:
                if (node.IsDirectory && node.Expanded)
                {
                    Toggle(node);
                }
                else if (node.Parent is { } parent && parent != _root)
                {
                    _cursor = _visible.IndexOf(parent);
                }

                break;
            case ConsoleKey.Enter when node.IsDirectory:
                Toggle(node);
                break;
            case ConsoleKey.Enter:
            case ConsoleKey.E:
                return KeyResult.Extract;
            case ConsoleKey.V:
                return KeyResult.Verify;
            case ConsoleKey.Tab when !node.IsDirectory && StreamsOf(node).Count > 1:
                // Tab and Shift+Tab step through the file's streams; E then extracts that one.
                var count = StreamsOf(node).Count;
                node.StreamIndex = (node.StreamIndex + ((key.Modifiers & ConsoleModifiers.Shift) != 0 ? count - 1 : 1)) % count;
                break;
            case ConsoleKey.Escape:
            case ConsoleKey.Q:
            case ConsoleKey.Backspace:
                return KeyResult.Back;
        }

        return KeyResult.Stay;
    }

    private void Move(int delta) => _cursor = Math.Min(Math.Max(_cursor + delta, 0), Math.Max(0, _visible.Count - 1));

    private void Toggle(Node node)
    {
        if (node.Expanded)
        {
            node.Expanded = false;
        }
        else
        {
            Open(node);
        }

        Rebuild();
        _cursor = Math.Max(0, _visible.IndexOf(node));
    }

    private void Open(Node node)
    {
        Load(node);
        node.Expanded = true;
    }

    /// <summary>Reads a directory's entries the first time (directories first, then by name). A read error is kept and shown.</summary>
    private void Load(Node node)
    {
        if (node.Children is not null)
        {
            return;
        }

        try
        {
            var directories = _fs.GetDirectories(node.Path).Select(p => new Node(p, true, node.Depth + 1, node));
            var files = _fs.GetFiles(node.Path).Select(p => new Node(p, false, node.Depth + 1, node) { Length = _fs.GetFileLength(p) });
            node.Children =
            [
                .. directories.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase),
                .. files.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            ];
        }
        catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
        {
            node.Children = [];
            node.Error = ex.Message;
        }
    }

    /// <summary>The rows of the tree as shown: every entry of every open directory, depth first.</summary>
    private void Rebuild()
    {
        var visible = new List<Node>();
        var pending = new Stack<Node>();
        PushChildren(_root);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            visible.Add(node);

            if (node.Expanded)
            {
                PushChildren(node);
            }
        }

        _visible = visible;
        Move(0);

        void PushChildren(Node parent)
        {
            if (parent.Children is null)
            {
                return;
            }

            for (var i = parent.Children.Count - 1; i >= 0; i--)
            {
                pending.Push(parent.Children[i]);
            }
        }
    }

    /// <summary>
    ///     Verifies a file, or every file below a directory (reading the directories it has not read yet, without opening them in the tree), with
    ///     a progress bar by bytes. On ReFS the data is checked against its checksums; elsewhere it is read in full, which shows it can be read.
    ///     Results stay on the nodes; directory results are worked out from their children, up to the root.
    /// </summary>
    private void Verify(Node node, CancellationToken cancellationToken)
    {
        var files = new List<Node>();
        _console.Status().Spinner(Spinner.Known.Dots).Start("Finding the files to verify...", _ => Collect(node, files, cancellationToken));
        var total = files.Sum(f => f.Length);
        int verified = 0, unchecked_ = 0, failed = 0;

        try
        {
            _console.Progress()
                    .AutoClear(true)
                    .Columns(new TaskDescriptionColumn { Alignment = Justify.Left }, new ProgressBarColumn(), new PercentageColumn(), new DownloadedColumn(),
                        new TransferSpeedColumn(), new RemainingTimeColumn())
                    .Start(context =>
                    {
                        var task = context.AddTask($"Verifying {files.Count} file(s)", maxValue: Math.Max(total, 1));
                        var receiver = new BytesReceiver(task);

                        for (var i = 0; i < files.Count; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var file = files[i];
                            task.Description = $"Verifying {i + 1}/{files.Count} [grey]{Markup.Escape(Shorten(file.Path, 50))}[/]";

                            try
                            {
                                if (_fs is ReFsFileSystem refs)
                                {
                                    var result = refs.VerifyData(file.Path, receiver, cancellationToken);
                                    file.Status = result.HasChecksums ? CheckStatus.Verified : CheckStatus.NoChecksums;
                                    file.Message = Describe(result);
                                }
                                else
                                {
                                    var read = ReadAll(file.Path, receiver, cancellationToken);
                                    file.Status = CheckStatus.NoChecksums;
                                    file.Message = $"read: {Details.Size(read)} (this file system has no data checksums)";
                                }
                            }
                            catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
                            {
                                file.Status = CheckStatus.Failed;
                                file.Message = ex.Message;
                            }

                            _ = file.Status switch
                            {
                                CheckStatus.Verified => verified++,
                                CheckStatus.NoChecksums => unchecked_++,
                                _ => failed++
                            };
                        }

                        task.Value = task.MaxValue;
                    });
        }
        finally
        {
            Summarize(node);

            for (var parent = node.Parent; parent is not null && parent != _root; parent = parent.Parent)
            {
                SummarizeOne(parent);
            }

            _console.MarkupLine(
                $"[green]{verified} verified[/], {unchecked_} without checksums, {(failed > 0 ? $"[red]{failed} failed[/]" : "0 failed")} [grey]({Markup.Escape(node.Path.Length == 0 ? "\\" : node.Path)})[/]");
        }
    }

    private long ReadAll(string path, IProgress<long> progress, CancellationToken cancellationToken)
    {
        var buffer = new byte[1 << 20];
        long total = 0;
        using var stream = FileContent.OpenRead(_fs, path);
        int read;

        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += read;
            progress.Report(read);
        }

        return total;
    }

    /// <summary>
    ///     A file's result in words, never-written space apart from written data without checksums: "verified with CRC32C: 4 KiB checked,
    ///     433.2 KiB never written (zeros, nothing to check)", or "all written data checked" when nothing is left unchecked.
    /// </summary>
    internal static string Describe(ReFsDataVerification result)
    {
        var parts = new List<string>();

        if (result.HasChecksums)
        {
            parts.Add(result.FullyVerified && result.UnwrittenBytes == 0
                ? $"all {Details.Size((long)result.VerifiedBytes)} checked"
                : result.FullyVerified
                    ? $"all written data checked ({Details.Size((long)result.VerifiedBytes)})"
                    : $"{Details.Size((long)result.VerifiedBytes)} checked");
        }

        if (result.UnwrittenBytes > 0)
        {
            parts.Add($"{Details.Size((long)result.UnwrittenBytes)} never written (zeros, nothing to check)");
        }

        if (result.UncheckedBytes > 0)
        {
            parts.Add($"{Details.Size((long)result.UncheckedBytes)} written without checksums (read, cannot be checked)");
        }

        if (parts.Count == 0)
        {
            parts.Add("empty: nothing to check");
        }

        var prefix = result.HasChecksums ? $"verified with {string.Join(" and ", result.Kinds.Select(Details.ChecksumName))}: " : "no checksums: ";
        return prefix + string.Join(", ", parts);
    }

    private void Collect(Node node, List<Node> files, CancellationToken cancellationToken)
    {
        if (!node.IsDirectory)
        {
            files.Add(node);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        Load(node);

        foreach (var child in node.Children!)
        {
            Collect(child, files, cancellationToken);
        }
    }

    /// <summary>Works out the results of a directory subtree from its files, bottom up.</summary>
    private static void Summarize(Node node)
    {
        if (!node.IsDirectory || node.Children is null)
        {
            return;
        }

        foreach (var child in node.Children)
        {
            Summarize(child);
        }

        SummarizeOne(node);
    }

    /// <summary>
    ///     A directory's result from its children: failed when a child failed (or the directory could not be read), verified when every child has a
    ///     result and at least one was verified, without checksums when every child was read without any, unknown otherwise.
    /// </summary>
    private static void SummarizeOne(Node directory)
    {
        var children = directory.Children ?? [];
        var failed = children.Count(c => c.Status == CheckStatus.Failed);
        var verified = children.Count(c => c.Status == CheckStatus.Verified);
        var allChecked = children.Count > 0 && children.All(c => c.Status != CheckStatus.Unknown);

        (directory.Status, directory.Message) = directory.Error is not null
            ? (CheckStatus.Failed, directory.Error)
            : failed > 0
                ? (CheckStatus.Failed, $"{failed} entr{(failed == 1 ? "y" : "ies")} below failed")
                : allChecked && verified > 0
                    ? (CheckStatus.Verified, "nothing below failed; files with checksums verified")
                    : allChecked
                        ? (CheckStatus.NoChecksums, "no checksums below: the data was read but cannot be checked")
                        : (CheckStatus.Unknown, null);
    }

    private Rows Render()
    {
        var width = _console.Profile.Width;
        var treeWidth = Math.Max(30, width * 11 / 20);
        var rows = Rows;

        if (_cursor < _top)
        {
            _top = _cursor;
        }
        else if (_cursor >= _top + rows)
        {
            _top = _cursor - rows + 1;
        }

        _top = Math.Min(Math.Max(_top, 0), Math.Max(0, _visible.Count - rows));
        var textWidth = Math.Max(10, treeWidth - 4);
        var lines = new List<IRenderable>();

        if (_visible.Count == 0)
        {
            lines.Add(new Markup(_root.Error is null ? "[grey](empty)[/]" : $"[red]{Markup.Escape(_root.Error)}[/]"));
        }

        for (var i = _top; i < Math.Min(_visible.Count, _top + rows); i++)
        {
            lines.Add(new Markup(Line(_visible[i], textWidth, i == _cursor, IsChanged(_visible[i]))));
        }

        while (lines.Count < rows)
        {
            lines.Add(new Text(string.Empty));
        }

        var selected = _visible.Count > 0 ? _visible[_cursor] : null;
        var position = _visible.Count > 0 ? $" {_cursor + 1}/{_visible.Count} " : string.Empty;
        var tree = new Panel(new Rows(lines)).Header($"[bold]\\{Markup.Escape(Shorten(selected?.Parent?.Path ?? string.Empty, textWidth - 12))}[/]")
                                             .Border(BoxBorder.Rounded).Expand();

        tree.Height = rows + 2;

        IRenderable details = selected is null ? new Text(string.Empty) : DetailsOf(selected);
        var grid = new Grid().AddColumn(new GridColumn().Width(treeWidth).NoWrap()).AddColumn(new GridColumn().Width(Math.Max(20, width - treeWidth - 2)));
        grid.AddRow(tree, details);

        var log = _openAtStep is not null && _transactions.Count > 0 ? $"  Ctrl+PgUp/PgDn log {_step}/{_transactions.Count}" : string.Empty;
        return new Rows(grid,
            new Markup($"[grey]↑↓ move  → open  ← close/parent  Enter open/extract  E extract  Tab stream  V verify{log}  Esc back[/][dim]{position}[/]"));
    }

    /// <summary>One tree row: green or red after verification, yellow when the log step just applied changed it, the cursor row highlighted.</summary>
    private string Line(Node node, int width, bool selected, bool changed)
    {
        var indent = new string(' ', node.Depth * 2);
        var marker = node.IsDirectory ? node.Expanded ? "▾ " : "▸ " : "  ";
        var stream = node.SelectedStream;
        var name = node.IsDirectory ? node.Name + "\\" : stream is null ? node.Name : $"{node.Name}:{StreamLabel(stream.Name)}";
        var size = node.IsDirectory ? string.Empty : Details.Size(stream?.Length ?? node.Length);
        var room = Math.Max(1, width - indent.Length - marker.Length - size.Length - 1);
        var text = Markup.Escape(indent + marker + (name.Length > room ? name.Substring(0, Math.Max(1, room - 1)) + "…" : name.PadRight(room)));
        var sizeText = Markup.Escape(size);

        if (selected)
        {
            return node.Status switch
            {
                CheckStatus.Verified => $"[black on green]{text} {sizeText}[/]",
                CheckStatus.Failed => $"[white on red]{text} {sizeText}[/]",
                _ => $"[black on deepskyblue1]{text} {sizeText}[/]"
            };
        }

        var colour = node.Status switch
        {
            CheckStatus.Verified => "green",
            CheckStatus.Failed => "red",
            _ when changed => "yellow",
            _ => node.IsDirectory ? "blue" : "default"
        };

        return $"[{colour}]{text}[/] [grey]{sizeText}[/]";
    }

    private Rows DetailsOf(Node node)
    {
        var parts = new List<IRenderable>();

        try
        {
            var streams = StreamsOf(node);
            var table = Details.Table(Details.Entry(_fs, node.Path, true, streams.Count <= 1), node.Path.Length == 0 ? "\\" : node.Path);
            table.Expand();
            parts.Add(table);

            if (!node.IsDirectory && streams.Count > 1)
            {
                parts.Add(StreamTable(node, streams));
            }
        }
        catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
        {
            parts.Add(new Markup($"[red]{Markup.Escape(ex.Message)}[/]"));
        }

        if (node.Message is not null)
        {
            parts.Add(new Markup(node.Status switch
            {
                CheckStatus.Verified => $"[green]{Markup.Escape(node.Message)}[/]",
                CheckStatus.Failed => $"[red]{Markup.Escape(node.Message)}[/]",
                _ => $"[yellow]{Markup.Escape(node.Message)}[/]"
            }));
        }
        else if (node.Error is not null)
        {
            parts.Add(new Markup($"[red]{Markup.Escape(node.Error)}[/]"));
        }

        if (node.IsDirectory && node.Children is { } children)
        {
            parts.Add(new Markup($"[grey]{children.Count(c => c.IsDirectory)} folder(s), {children.Count(c => !c.IsDirectory)} file(s)[/]"));
        }

        if (ExtendedAttributes(node) is { } attributes)
        {
            parts.Add(attributes);
        }

        // The log position goes first: the pane is cropped at the bottom when it does not fit.
        if (LogPanel() is { } log)
        {
            parts.Insert(0, log);
        }

        return new Rows(parts);
    }

    /// <summary>
    ///     A file's streams, the default stream first (read once, then kept): on ReFS with lengths and snapshots, elsewhere the alternate data
    ///     stream names the file system lists.
    /// </summary>
    private IReadOnlyList<StreamChoice> StreamsOf(Node node)
    {
        if (node.Streams is not null)
        {
            return node.Streams;
        }

        var streams = new List<StreamChoice>();

        if (!node.IsDirectory)
        {
            try
            {
                if (_fs is ReFsFileSystem refs)
                {
                    streams.AddRange(refs.GetEntryDetails(node.Path).Streams
                                         .OrderBy(s => s.Name.Length == 0 ? 0 : s.IsSnapshot ? 2 : 1)
                                         .Select(s => new StreamChoice(s.Name, (long)s.Length, s.IsSnapshot, s.IsResident, s.SnapshotTime)));
                }
                else
                {
                    streams.Add(new StreamChoice(string.Empty, node.Length, false, false, null));

                    if (_fs is IFileSystemWithAltStreams alternate)
                    {
                        streams.AddRange(alternate.GetAlternateDataStreams(node.Path).Select(name => new StreamChoice(name, -1, false, false, null)));
                    }
                }
            }
            catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
            {
                node.Error = ex.Message;
            }
        }

        node.Streams = streams;
        return streams;
    }

    /// <summary>The file's streams, the one Tab selected (what E extracts) highlighted.</summary>
    private static Table StreamTable(Node node, IReadOnlyList<StreamChoice> streams)
    {
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Stream").AddColumn("Size").AddColumn("").Expand()
                               .Title("[bold]Streams[/] [grey](Tab selects what E extracts)[/]");

        for (var i = 0; i < streams.Count; i++)
        {
            var stream = streams[i];
            var note = stream.IsSnapshot ? $"snapshot, {stream.SnapshotTime:yyyy-MM-dd HH:mm:ss}" : stream.IsResident ? "inline" : string.Empty;
            var style = i == node.StreamIndex ? "black on deepskyblue1" : "default";
            table.AddRow(new Markup($"[{style}]{Markup.Escape((i == node.StreamIndex ? "▶ " : "  ") + StreamLabel(stream.Name))}[/]"),
                new Text(stream.Length < 0 ? string.Empty : Details.SizeWithBytes(stream.Length)), new Text(note));
        }

        return table;
    }

    private static string StreamLabel(string name) => name.Length == 0 ? "(default)" : name;

    /// <summary>The entry's extended attributes as a table (ReFS; read when the entry is first shown, then kept), or null when it has none.</summary>
    private IRenderable? ExtendedAttributes(Node node)
    {
        if (_fs is not ReFsFileSystem refs)
        {
            return null;
        }

        if (node.Attributes is null)
        {
            try
            {
                node.Attributes = refs.GetExtendedAttributes(node.Path);
            }
            catch (Exception ex) when (ImageCommand<ImageSettings>.IsReadFailure(ex))
            {
                node.Attributes = [];
                node.AttributesError = ex.Message;
            }
        }

        if (node.AttributesError is not null)
        {
            return new Markup($"[red]Extended attributes: {Markup.Escape(node.AttributesError)}[/]");
        }

        if (node.Attributes.Count == 0)
        {
            return null;
        }

        var table = new Table().Border(TableBorder.Rounded).Title("[bold]Extended attributes[/]").AddColumn("Name").AddColumn("Size").AddColumn("Value").Expand();

        foreach (var attribute in node.Attributes)
        {
            table.AddRow(new Text(attribute.Name), new Text(Details.SizeWithBytes(attribute.Value.Length)), new Text(Preview(attribute.Value)));
        }

        return table;
    }

    /// <summary>Text when the value is printable ASCII (a trailing NUL allowed), otherwise the first bytes in hex.</summary>
    private static string Preview(byte[] value)
    {
        var length = value.Length > 0 && value[value.Length - 1] == 0 ? value.Length - 1 : value.Length;
        var printable = length > 0 && length <= 80;

        for (var i = 0; i < length; i++)
        {
            printable &= value[i] is >= 0x20 and < 0x7F;
        }

        if (printable)
        {
            return Encoding.ASCII.GetString(value, 0, length);
        }

        const int Shown = 24;
        return LogCommand.Hex(value.AsSpan(0, Math.Min(value.Length, Shown))) + (value.Length > Shown ? "…" : string.Empty);
    }

    private static string Shorten(string text, int length) => length <= 3 || text.Length <= length ? text : "…" + text.Substring(text.Length - (length - 1));

    private enum KeyResult
    {
        Stay,
        Back,
        Extract,
        Verify
    }

    private enum CheckStatus
    {
        Unknown,
        Verified,
        NoChecksums,
        Failed
    }

    /// <summary>Moves the bar by the bytes each read reports, on the reading thread.</summary>
    private sealed class BytesReceiver(ProgressTask task) : IProgress<long>
    {
        public void Report(long value) => task.Increment(value);
    }

    private sealed class Node(string path, bool isDirectory, int depth, Node? parent)
    {
        public string Path { get; } = path;

        public string Name { get; } = Details.Name(path);

        public bool IsDirectory { get; } = isDirectory;

        public int Depth { get; } = depth;

        public Node? Parent { get; } = parent;

        public long Length { get; init; }

        public List<Node>? Children { get; set; }

        public bool Expanded { get; set; }

        public string? Error { get; set; }

        public CheckStatus Status { get; set; }

        /// <summary>The verification result for the details pane.</summary>
        public string? Message { get; set; }

        public IReadOnlyList<ReFsExtendedAttribute>? Attributes { get; set; }

        public string? AttributesError { get; set; }

        public ulong? ObjectId { get; set; }

        public IReadOnlyList<StreamChoice>? Streams { get; set; }

        /// <summary>The stream Tab selected: an index into the file's streams (0, the default stream, until Tab is pressed).</summary>
        public int StreamIndex { get; set; }

        /// <summary>The stream E extracts on its own, or null for the whole file (the default stream selected).</summary>
        public StreamChoice? SelectedStream => !IsDirectory && Streams is { Count: > 1 } streams && StreamIndex > 0 ? streams[StreamIndex] : null;
    }
}