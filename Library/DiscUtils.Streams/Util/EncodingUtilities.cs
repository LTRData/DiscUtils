using System.Text;

namespace DiscUtils.Streams;

public static class EncodingUtilities
{
    /// <summary>
    /// Retrieve the Latin1 encoding. This encoding is also known as iso-8859-1,
    /// by its codepage 28591, and by its Windows codepage 1252.
    /// </summary>
    /// <returns>Encoding</returns>
    public static Encoding GetLatin1Encoding()
    {
#if NET6_0_OR_GREATER
        return Encoding.Latin1;
#else
        return Encoding.GetEncoding("Latin1");
#endif
    }
}
