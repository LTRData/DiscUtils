using System.Text;
using DiscUtils.Containers;
using DiscUtilsCli;
using DiscUtilsCli.Interactive;
using Spectre.Console;

SetupHelper.SetupContainers();
DiscUtils.FileSystems.SetupHelper.SetupFileSystems();
DiscUtils.Transports.SetupHelper.SetupTransports();

var terminal = !Console.IsOutputRedirected;

// No arguments in a console: the interactive mode (it handles Ctrl+C itself).
if (args.Length == 0 && terminal && !Console.IsInputRedirected)
{
    return InteractiveSession.Run(AnsiConsole.Console);
}

// Ctrl+C asks the running command to stop (it ends at its next check, with exit code 1); a second Ctrl+C ends the process.
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    if (cancellation.IsCancellationRequested)
    {
        return;
    }

    e.Cancel = true;
    cancellation.Cancel();
};

// Console.Out flushes every line, and a console window draws only a few thousand lines a second, which limits a recursive listing.
// Text goes through one buffered writer instead, flushed when the command ends (and before anything is drawn on the terminal); stderr
// stays unbuffered. Same encoding as Console.Out, without a byte order mark.
using var stdout = Console.OpenStandardOutput();
var encoding = Console.OutputEncoding.CodePage == Encoding.UTF8.CodePage ? new UTF8Encoding(false) : Console.OutputEncoding;
using var output = new StreamWriter(stdout, encoding, 1 << 16, true);

try
{
    return CliRunner.Run(args, output, Console.Error, stdout, terminal ? AnsiConsole.Console : null, cancellation.Token);
}
finally
{
    output.Flush();
}