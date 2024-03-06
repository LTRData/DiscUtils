using System.Text;

namespace DiscUtils.Streams;

public static class EncodingUtilities
{
    /// <summary>
    /// Retrieve the Latin1 encoding. This encoding is also known as iso-8859-1,
    /// by its codepage 28591, and by its Windows codepage 1252.
    /// </summary>
    /// <returns>Encoding</returns>
#if NET6_0_OR_GREATER
    public static Encoding GetLatin1Encoding()
        => Encoding.Latin1;
#else
    public static Encoding GetLatin1Encoding()
        => latin1 ??= Encoding.GetEncoding("Latin1");

    private static Encoding latin1;
#endif
}
