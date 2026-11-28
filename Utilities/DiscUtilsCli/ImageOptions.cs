namespace DiscUtilsCli;

/// <summary>How to open an image: which volume (null: the first with a file system), and the ReFS options.</summary>
internal sealed record ImageOptions(int? Volume = null, bool NoVerify = false, bool Replay = false, int? ReplayTransactions = null);