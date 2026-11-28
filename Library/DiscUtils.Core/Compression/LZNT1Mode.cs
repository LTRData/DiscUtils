namespace DiscUtils.Compression;

/// <summary>
/// How <see cref="LZNT1"/> decodes data that [MS-XCA] 2.5 does not allow, or that Windows treats differently. Compression is
/// the same in both modes, and data written by Windows or by <see cref="LZNT1"/> decodes the same way in both.
/// </summary>
public enum LZNT1Mode
{
    /// <summary>
    /// Follows [MS-XCA] 2.5. A chunk header whose signature (bits 12 to 14) is not 3 is rejected. A zero header ends the data,
    /// also as the first header, and input too short for a header decodes to nothing. A chunk followed by another must
    /// decompress to at least 4096 bytes ("compressed in units of 4096 bytes"); a shorter one is rejected. When the output
    /// buffer is too small, it is filled and decoding stops, also in the middle of a back reference.
    /// </summary>
    Strict,

    /// <summary>
    /// Matches <c>RtlDecompressBuffer</c>. The signature is not checked. The first header is always a chunk, even when it is
    /// zero (a 1-byte uncompressed chunk), and input shorter than a header fails. A chunk that decompresses to less than 4096
    /// bytes and is followed by another is padded with zeros to 4096 bytes. A back reference that does not fit the output
    /// buffer fails; literals and uncompressed data fill it and stop.
    /// </summary>
    Windows,
}
