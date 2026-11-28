namespace DiscUtils.ReFs.Entries.Files;

internal readonly record struct RunIntegrityRange(ulong Lcn, uint Count, ulong SkippedClusters);