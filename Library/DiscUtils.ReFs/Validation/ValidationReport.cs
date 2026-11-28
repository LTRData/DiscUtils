
using DiscUtils.ReFs.Compat;

namespace DiscUtils.ReFs.Validation;

/// <summary>Result of <c>Validate</c>.</summary>
public sealed class ValidationReport
{
    private readonly List<ValidationIssue> _issues = [];
    private readonly List<string> _warnings = [];

    internal ValidationReport(ILogger logger)
    {
        Logger = logger;
    }

    /// <summary>Problems: unreadable or corrupt metadata, unreadable file data, unresolvable references.</summary>
    public IReadOnlyList<ValidationIssue> Issues => _issues;

    /// <summary>Conditions that are not errors but limit what the check can say (e.g. a volume that was not cleanly dismounted).</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    public bool IsValid => _issues.Count == 0;

    /// <summary>Whether metadata page checksum verification was enabled for this run.</summary>
    public bool MetadataChecksumsEnabled { get; internal set; }

    /// <summary>Whether file data was requested and its available integrity checks were enabled.</summary>
    public bool DataChecksumsEnabled { get; internal set; }

    /// <summary>Objects in the object table whose whole tree was successfully read.</summary>
    public int Objects { get; internal set; }

    public int Directories { get; internal set; }

    public int Files { get; internal set; }

    /// <summary>Data streams successfully read (unnamed and named); 0 when file data was not requested.</summary>
    public int Streams { get; internal set; }

    /// <summary>Bytes of file data successfully read; available integrity checks follow <see cref="DataChecksumsEnabled" />.</summary>
    public ulong DataBytes { get; internal set; }

    /// <summary>Uncached metadata page reads during this run, not unique pages. Concurrent reads on the same volume are also included.</summary>
    public ulong MetadataPages { get; internal set; }

    public TimeSpan Elapsed { get; internal set; }

    /// <summary>Where problems are logged as they are found (a validation of a large volume takes long).</summary>
    internal ILogger Logger { get; }

    internal void AddIssue(string location, string message)
    {
        Logs.ValidationIssue(Logger, location, message);
        _issues.Add(new ValidationIssue(location, message));
    }

    internal void AddWarning(string message)
    {
        Logs.ValidationWarning(Logger, message);
        _warnings.Add(message);
    }
}