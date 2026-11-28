namespace DiscUtils.Compression;

/// <summary>
/// How <see cref="XpressHuffman"/> decodes data that [MS-XCA] 2.2 does not allow, or that Windows treats differently. Data written by
/// Windows decodes the same way in both modes when the output buffer has the uncompressed size. In both modes, input that ends before
/// the decoder's 32-bit read-ahead (the two 16-bit words after a table, and each word read as bits are used) is rejected, as Windows
/// and the specification do.
/// </summary>
public enum XpressHuffmanMode
{
    /// <summary>
    /// Follows [MS-XCA] 2.2.4. A table of code lengths that is not a complete prefix code is rejected, including a table with a single
    /// symbol. Decoding stops as soon as the output buffer is full, and when the buffer is too small, it is filled, also in the middle of
    /// a match.
    /// </summary>
    Strict,

    /// <summary>
    /// Matches <c>RtlDecompressBufferEx</c>. A table that is not a complete prefix code is rejected, except one in which a single symbol
    /// has a 1-bit code: either bit value then decodes to that symbol. A match that does not fit the output buffer fails. Once the output
    /// buffer is full (or from the start, if it is empty), decoding goes on as the outer loop of [MS-XCA] 2.2.4 does: while 260 or more
    /// bytes of input are left, a table is read, an invalid one fails, and the table and its first two words are skipped.
    /// </summary>
    Windows,
}
