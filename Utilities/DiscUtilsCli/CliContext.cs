using Spectre.Console;

namespace DiscUtilsCli;

/// <summary>
///     Where a command writes. <see cref="Output" /> takes the plain text (listings, details, summaries) that scripts read, <see cref="Error" />
///     error lines, <see cref="Binary" /> the raw bytes of <c>cat</c>. <see cref="Terminal" /> is set only when standard output is an interactive
///     console: commands then show progress and render details as tables instead of plain lines.
/// </summary>
internal sealed class CliContext(TextWriter output, TextWriter error, Stream binary, IAnsiConsole? terminal)
{
    public TextWriter Output { get; } = output;

    public TextWriter Error { get; } = error;

    public Stream Binary { get; } = binary;

    public IAnsiConsole? Terminal { get; } = terminal;

    /// <summary>Runs a render on the terminal after flushing the buffered plain output, so both reach the console in order.</summary>
    public void Render(Action<IAnsiConsole> render)
    {
        Output.Flush();
        render(Terminal ?? throw new InvalidOperationException("No terminal."));
    }
}