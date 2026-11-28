namespace DiscUtils.Compression;

/// <summary>
/// Selects how <see cref="XpressLz77"/> treats input on which Windows (<c>RtlDecompressBuffer</c> with
/// <c>COMPRESSION_FORMAT_XPRESS</c>) and [MS-XCA] 2.4 disagree. Input on which they agree decodes the same in both modes:
/// decoding runs to the end-of-stream marker (a match flag at the end of the input), and a missing marker, trailing data, an
/// offset before the start of the output, an extended length below 15 + 7 or anything that does not fit the output is rejected.
/// </summary>
public enum XpressLz77Mode
{
    /// <summary>
    /// Accepts and rejects what Windows does. Unlike [MS-XCA], Windows rejects a stream that decodes to no data, such as one that
    /// holds only an end-of-stream marker.
    /// </summary>
    Windows,

    /// <summary>Follows the [MS-XCA] 2.4 pseudocode, which accepts a stream that holds only an end-of-stream marker as empty data.</summary>
    Strict,
}
