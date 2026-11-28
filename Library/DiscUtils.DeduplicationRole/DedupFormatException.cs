namespace DiscUtils.DeduplicationRole;

/// <summary>Data Deduplication data (reparse data, a container, a stream map or a chunk) is damaged or not in a supported layout.</summary>
public class DedupFormatException : IOException
{
    public DedupFormatException(string message) : base(message) { }

    public DedupFormatException(string message, Exception inner) : base(message, inner) { }
}