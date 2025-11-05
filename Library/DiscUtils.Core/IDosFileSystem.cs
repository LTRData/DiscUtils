namespace DiscUtils;

public interface IDosFileSystem : IFileSystem
{
    /// <summary>
    /// Gets the short name for a given path.
    /// </summary>
    /// <param name="path">The path to convert.</param>
    /// <returns>The short name.</returns>
    /// <remarks>
    /// This method only gets the short name for the final part of the path, to
    /// convert a complete path, call this method repeatedly, once for each path
    /// segment.  If there is no short name for the given path,<c>null</c> is
    /// returned.
    /// </remarks>
    string? GetShortName(string path);

    /// <summary>
    /// Sets the short name for a given file or directory.
    /// </summary>
    /// <param name="path">The full path to the file or directory to change.</param>
    /// <param name="shortName">The shortName, which should not include a path.</param>
    void SetShortName(string path, string shortName);

    /// <summary>
    /// Gets the standard file information for a file.
    /// </summary>
    /// <param name="path">The full path to the file or directory to query.</param>
    /// <returns>The standard file information.</returns>
    WindowsFileInformation GetFileStandardInformation(string path);

    /// <summary>
    /// Sets the standard file information for a file.
    /// </summary>
    /// <param name="path">The full path to the file or directory to query.</param>
    /// <param name="info">The standard file information.</param>
    void SetFileStandardInformation(string path, WindowsFileInformation info);

    /// <summary>
    /// Gets the file id for a given path.
    /// </summary>
    /// <param name="path">The path to get the id of.</param>
    /// <returns>The file id, or -1.</returns>
    /// <remarks>
    /// The returned file id uniquely identifies the file, and is shared by all hard
    /// links to the same file.  The value -1 indicates no unique identifier is
    /// available, and so it can be assumed the file has no hard links.
    /// </remarks>
    long GetFileId(string path);
}
