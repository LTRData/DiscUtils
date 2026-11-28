using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace LibraryTests.ReFs;

/// <summary>
/// The lab dataset's JSON manifests (<c>&lt;image&gt;.manifest.json</c>, written by Windows when it read the image back), read with <see cref="MiniJson"/>:
/// an object of <c>name</c>, <c>scenario</c>, <c>image</c>, <c>complete</c> and entry lists (<c>entries</c>, and <c>before</c> on cut-off copies).
/// Each entry has <c>path</c>, <c>directory</c>, <c>attributes</c>, <c>created</c>/<c>modified</c> (FILETIME), <c>length</c>, <c>sha256</c> and
/// optionally <c>listedLength</c>, <c>reparse</c> and <c>streams</c>. A field a manifest does not record reads as null, false or 0.
/// </summary>
internal static class LabManifest
{
    /// <summary>Reads a manifest file.</summary>
    public static Dictionary<string, object?> Load(string path) => (Dictionary<string, object?>)MiniJson.Parse(File.ReadAllText(path))!;

    /// <summary>Reads the manifest of an image in the fixture folder.</summary>
    public static Dictionary<string, object?> ForImage(string image) => Load(ReFsFixtureTest.FixturePath(image + ".manifest.json"));

    /// <summary>The objects of a list field (empty when the manifest has none).</summary>
    public static List<Dictionary<string, object?>> Entries(Dictionary<string, object?> manifest, string key = "entries") =>
        manifest.TryGetValue(key, out var value) && value is List<object?> list ? list.OfType<Dictionary<string, object?>>().ToList() : [];

    public static string? Str(Dictionary<string, object?> o, string key) => o.TryGetValue(key, out var v) ? v as string : null;

    public static bool Bool(Dictionary<string, object?> o, string key) => o.TryGetValue(key, out var v) && v is true;

    public static long Num(Dictionary<string, object?> o, string key)
    {
        o.TryGetValue(key, out var v);
        return v switch
        {
            long l => l,
            double d => (long)d,
            _ => 0
        };
    }

    /// <summary>The reparse tag of an entry (<c>reparse.tag</c>, "0x..."), or null.</summary>
    public static uint? Reparse(Dictionary<string, object?> entry) =>
        entry.TryGetValue("reparse", out var r) && r is Dictionary<string, object?> reparse && Str(reparse, "tag") is { } tag
            ? uint.Parse(tag.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : null;
}
