using System.Text;

namespace DiscUtils.CoreCompat;

internal static class EncodingHelper
{
    private static bool _registered;

    public static void RegisterEncodings()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;

#if NETSTANDARD || NETCOREAPP
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
#endif
    }

    private static readonly char[] _invalidFileNameChars = ['\\', '/', '*', '?', ':'];

    public static string SanitizeFileName(this string fileName)
    {
        foreach (var c in _invalidFileNameChars)
        {
            fileName = fileName.Replace(c, '_');
        }

        return fileName;
    }
}
