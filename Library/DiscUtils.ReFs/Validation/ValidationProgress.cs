namespace DiscUtils.ReFs.Validation;

/// <summary>
///     A snapshot of a running validation, reported on the validating thread: at each phase change, after each directory, after each
///     stream and every few MiB inside a large stream. Counts are cumulative; <see cref="ObjectsTotal" /> is known from the object phase on.
/// </summary>
public readonly record struct ValidationProgress(
    ValidationPhase Phase,
    int Directories,
    int Files,
    int Streams,
    ulong DataBytes,
    int Issues,
    int Objects,
    int ObjectsTotal,
    string CurrentPath);
