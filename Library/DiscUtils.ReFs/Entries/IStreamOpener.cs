namespace DiscUtils.ReFs.Entries;

/// <summary>
///     Opens a data stream of a file for reading. <see cref="ReFsVolume" /> opens streams as stored; an overlay (such as <c>ReFs.Dedup</c>'s reader) opens
///     them as Windows reads them with a filter loaded. <see cref="ReFsVolume.Validate(bool, IProgress{Validation.ValidationProgress}?, IStreamOpener?, CancellationToken)" />
///     reads file data through one.
/// </summary>
internal interface IStreamOpener
{
    /// <summary>Opens the default stream (<paramref name="stream" /> null or empty) or the named stream of <paramref name="entry" />.</summary>
    Stream OpenRead(ReFsEntry entry, string? stream = null);
}