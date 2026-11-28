using DiscUtils;
using DiscUtils.ReFs;

namespace DiscUtilsCli;

/// <summary>Opens file data for the commands that read it (<c>cat</c>, <c>extract</c>, the browser).</summary>
internal static class FileContent
{
    /// <summary>
    ///     The default stream of <paramref name="path" />, or the stream named <paramref name="stream" />. On ReFS any stream <see cref="ReFsFileSystem.GetEntryDetails" />
    ///     lists can be opened, snapshots included; elsewhere named streams go through <c>path:name</c>, where the file system supports them.
    /// </summary>
    public static Stream OpenRead(DiscFileSystem fs, string path, string? stream = null)
    {
        if (fs.DirectoryExists(path))
        {
            throw new IOException($"'{path}' is a directory.");
        }

        if (string.IsNullOrEmpty(stream))
        {
            return fs.OpenFile(path, FileMode.Open, FileAccess.Read);
        }

        if (fs is ReFsFileSystem refs)
        {
            return refs.OpenStream(path, stream!);
        }

        if (fs is not IFileSystemWithAltStreams)
        {
            throw new NotSupportedException($"{fs.FriendlyName} has no named streams.");
        }

        return fs.OpenFile($"{path}:{stream}", FileMode.Open, FileAccess.Read);
    }
}