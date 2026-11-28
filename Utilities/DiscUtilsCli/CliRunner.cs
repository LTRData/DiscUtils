using DiscUtilsCli.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DiscUtilsCli;

/// <summary>
///     The <c>discutils</c> command line (kept apart from Main so it can be tested): the command table on Spectre.Console.Cli. Exit codes: 0 done,
///     1 failed (an image, path or file could not be read, validation found problems), 2 a usage error.
/// </summary>
internal static class CliRunner
{
    public const string Name = "discutils";
    public const int Ok = 0;
    public const int Failure = 1;
    public const int Usage = 2;

    /// <summary>
    ///     Runs a command. <paramref name="terminal" /> is the console when standard output is an interactive terminal: commands then show
    ///     progress and tables; without it every command writes plain text (as when the output is redirected, and in tests).
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error, Stream binaryOutput, IAnsiConsole? terminal = null,
        CancellationToken cancellationToken = default)
    {
        var cli = new CliContext(output, error, binaryOutput, terminal);

        if (args.Length == 0 || args is ["help"])
        {
            args = ["--help"];
        }

        var app = new CommandApp();
        app.Configure(config =>
        {
            config.SetApplicationName(Name);
            config.ConfigureConsole(terminal ?? PlainConsole(output));
            config.UseStrictParsing();
            config.CaseSensitivity(CaseSensitivity.None);
            config.PropagateExceptions();

            config.AddCommand<InfoCommand>("info").WithData(cli).WithExample("info", @"D:\images\disk.vhdx");
            config.AddCommand<ListCommand>("ls").WithData(cli).WithExample("ls", @"D:\images\disk.vhdx", "Users", "-l");
            config.AddCommand<StatCommand>("stat").WithData(cli);
            config.AddCommand<CatCommand>("cat").WithData(cli);
            config.AddCommand<ExtractCommand>("extract").WithData(cli).WithExample("extract", @"D:\images\disk.vhdx", "Users", @"C:\recovered");
            config.AddCommand<ValidateCommand>("validate").WithData(cli);
            config.AddCommand<LogCommand>("log").WithData(cli);
        });

        try
        {
            return app.Run(args, cancellationToken);
        }
        catch (CommandAppException ex)
        {
            error.WriteLine($"error: {ex.Message}");
            error.WriteLine($"Run '{Name} --help' for usage.");
            return Usage;
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Cancelled.");
            return Failure;
        }
    }

    /// <summary>A console without colours or live displays over a writer: help and errors when the output is not a terminal.</summary>
    internal static IAnsiConsole PlainConsole(TextWriter writer) =>
        AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer)
        });
}