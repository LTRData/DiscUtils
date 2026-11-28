namespace DiscUtilsCli.Interactive;

/// <summary>A stream of a file the browser can show and extract. <see cref="Length" /> is -1 when the file system does not tell it cheaply.</summary>
internal sealed record StreamChoice(string Name, long Length, bool IsSnapshot, bool IsResident, DateTimeOffset? SnapshotTime);