namespace DiscUtils.ReFs.Entries.Enums;

internal static class ReFsAttributesExtensions
{
    /// <summary>
    ///     The attributes a directory listing reports (0x5AFFB7): Windows masks the stored word with them, which drops
    ///     <see cref="ReFsAttributes.Device" />, the ReFS-only markers and every bit not named here.
    /// </summary>
    private const ReFsAttributes Listed = ReFsAttributes.ReadOnly | ReFsAttributes.Hidden | ReFsAttributes.System | ReFsAttributes.Directory
                                          | ReFsAttributes.Archive | ReFsAttributes.Normal | ReFsAttributes.Temporary | ReFsAttributes.SparseFile
                                          | ReFsAttributes.ReparsePoint | ReFsAttributes.Compressed | ReFsAttributes.Offline | ReFsAttributes.NotContentIndexed
                                          | ReFsAttributes.Encrypted | ReFsAttributes.IntegrityStream | ReFsAttributes.NoScrubData | ReFsAttributes.Pinned
                                          | ReFsAttributes.Unpinned | ReFsAttributes.RecallOnDataAccess;

    /// <summary>The stored word without the ReFS-only markers that Windows never reports (the one place they are stripped).</summary>
    public static ReFsAttributes WithoutMarkers(this ReFsAttributes stored) => stored & ~(ReFsAttributes.DirectoryMarker | ReFsAttributes.ExtendedAttributesMarker);

    /// <summary>
    ///     The attributes of a directory entry as Windows lists them: the stored word masked to <see cref="Listed" />, and
    ///     <see cref="ReFsAttributes.Normal" /> when nothing is left.
    /// </summary>
    public static ReFsAttributes AsListed(this ReFsAttributes stored)
    {
        var listed = stored & Listed;
        return listed == ReFsAttributes.None ? ReFsAttributes.Normal : listed;
    }
}